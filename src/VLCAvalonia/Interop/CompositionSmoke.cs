using System.Diagnostics;

namespace VLCAvalonia.Interop;

internal static class CompositionSmoke
{
    private static readonly TimeSpan PlaybackStageTimeout =
        TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ControlStageTimeout =
        TimeSpan.FromSeconds(5);

    public static int Run(string? expectedPath)
    {
        if (expectedPath is not (
            "hardware-opaque-gpu" or
            "software-bgra-gpu-upload" or
            "cpu-bitmap"))
        {
            Console.Error.WriteLine(
                "[composition-smoke] --expect-path is required and must be " +
                "hardware-opaque-gpu, software-bgra-gpu-upload, or cpu-bitmap.");
            return 2;
        }

        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine($"Smoke media not found: {mediaPath}");
            return 2;
        }

        try
        {
            using CompositionPluginBridge bridge = new();
            bool expectHardware = expectedPath == "hardware-opaque-gpu";
            bool expectGPU = expectedPath != "cpu-bitmap";
            if (expectGPU)
            {
                CompositionPluginBridge.AdapterLuid adapter =
                    D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
                bridge.ConfigureAdapter(adapter);
                bridge.EnableGPUFrames();
            }
            else
            {
                bridge.ClearAdapterSelection();
            }
            bridge.ForceCPUOutput(!expectHardware);
            CompositionPluginBridge.PoolTelemetry before =
                bridge.ReadPoolTelemetry();
            bool receivedFrame = false;
            string? actualPath = null;
            using (VLCCorePlayer player = new(
                hardwareDecoding: expectHardware))
            {
                player.StateChanged += (_, state) =>
                    Console.WriteLine(
                        $"[composition-smoke] {state.Status}: {state.Error}");
                player.Open(mediaPath);

                Stopwatch timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(15))
                {
                    if (expectGPU && bridge.TryTakeLatestGPU(
                            out CompositionPluginBridge.GPUFrame? GPUFrame) &&
                        GPUFrame is not null)
                    {
                        string probe =
                            D3D11SharedTextureProbe.OpenAndSynchronize(GPUFrame);
                        actualPath = expectHardware
                            ? "hardware-opaque-gpu"
                            : "software-bgra-gpu-upload";
                        Console.WriteLine(
                            $"[composition-smoke] path={actualPath} " +
                            $"size={GPUFrame.Width}x{GPUFrame.Height} " +
                            $"pts={GPUFrame.PresentationTimestamp} " +
                            $"probe={probe}");
                        receivedFrame = true;
                        break;
                    }
                    if (!expectGPU && bridge.TryTakeLatest(
                            out CompositionPluginBridge.Frame? frame) &&
                        frame is not null)
                    {
                        actualPath = "cpu-bitmap";
                        Console.WriteLine(
                            "[composition-smoke] path=cpu-bitmap " +
                            $"size={frame.Width}x{frame.Height} " +
                            $"row-bytes={frame.RowBytes} " +
                            $"pts={frame.PresentationTimestamp}");
                        receivedFrame = true;
                        break;
                    }

                    Thread.Sleep(20);
                }
                player.Stop();
            }

            CompositionPluginBridge.PoolTelemetry after =
                bridge.ReadPoolTelemetry();
            Console.WriteLine(after.ToMachineReadableString());
            if (!receivedFrame || actualPath != expectedPath)
            {
                Console.Error.WriteLine(
                    $"[composition-smoke] Expected path={expectedPath}, " +
                    $"actual path={actualPath ?? "none"}.");
                return 1;
            }
            if (expectHardware &&
                (after.OpaquePicturesReceived <= before.OpaquePicturesReceived ||
                 after.CpuUploads != before.CpuUploads))
            {
                Console.Error.WriteLine(
                    "[composition-smoke] Hardware path did not receive an " +
                    "opaque picture with zero CPU uploads.");
                return 1;
            }
            if (expectedPath == "software-bgra-gpu-upload" &&
                (after.CpuPicturesReceived <= before.CpuPicturesReceived ||
                 after.CpuUploads <= before.CpuUploads))
            {
                Console.Error.WriteLine(
                    "[composition-smoke] Software upload path counters did " +
                    "not advance.");
                return 1;
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[composition-smoke] {exception}");
            return 3;
        }
    }

    public static int RunOpaque()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine(
                $"[opaque-smoke] Smoke media not found: {mediaPath}");
            return 2;
        }

        try
        {
            using CompositionPluginBridge monitor = new();
            CompositionPluginBridge.AdapterLuid adapter =
                D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
            monitor.ConfigureAdapter(adapter);
            monitor.ForceCPUOutput(false);
            monitor.EnableGPUFrames();
            CompositionPluginBridge.PoolTelemetry before =
                monitor.ReadPoolTelemetry();
            using (VLCCorePlayer player = new())
            {
                OpenAndWait(player, mediaPath, "opaque-open");
                WaitForOpaquePool(monitor, before, "opaque-open");
                StopAndWait(player, "opaque-stop");
            }

            WaitForReleaseBalance(monitor, before.ReleaseBalance, "opaque");
            CompositionPluginBridge.PoolTelemetry after =
                monitor.ReadPoolTelemetry();
            Console.WriteLine(after.ToMachineReadableString());
            if (after.CpuUploads != before.CpuUploads)
            {
                throw new InvalidOperationException(
                    "Opaque playback performed a CPU upload.");
            }
            Console.WriteLine(
                $"[opaque-smoke] PASS adapter-luid={adapter} " +
                "path=hardware-opaque-gpu");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[opaque-smoke] FAIL: {exception}");
            return 1;
        }
    }

    public static int RunEngine(int cycles)
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine(
                $"[engine-smoke] Smoke media not found: {mediaPath}");
            return 2;
        }

        VLCCorePlayer? player = null;
        try
        {
            using CompositionPluginBridge monitor = new();
            CompositionPluginBridge.AdapterLuid adapter =
                D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
            monitor.ConfigureAdapter(adapter);
            monitor.ForceCPUOutput(false);
            monitor.EnableGPUFrames();
            CompositionPluginBridge.PoolTelemetry initial =
                monitor.ReadPoolTelemetry();

            player = new VLCCorePlayer();
            OpenAndWait(player, mediaPath, "open");
            WaitForOpaquePool(monitor, initial, "open");

            RunAction("pause", player.TogglePause);
            WaitForState(
                player,
                "pause",
                state => state.Status == Player.Contracts.PlaybackStatus.Paused,
                ControlStageTimeout);

            const double seekTarget = 0.35;
            RunAction("seek", () => player.Seek(seekTarget));
            WaitForState(
                player,
                "seek",
                state => state.CanSeek &&
                    Math.Abs(state.Position - seekTarget) < 0.12,
                ControlStageTimeout);

            RunAction("resume", player.TogglePause);
            WaitForState(
                player,
                "resume",
                state => state.Status == Player.Contracts.PlaybackStatus.Playing,
                ControlStageTimeout);

            const double volumeTarget = 0.42;
            RunAction("volume", () => player.SetVolume(volumeTarget));
            WaitForState(
                player,
                "volume",
                state => Math.Abs(state.Volume - volumeTarget) < 0.03,
                ControlStageTimeout);

            RunAction("mute-on", () => player.SetMuted(true));
            WaitForState(player, "mute-on", state => state.IsMuted, ControlStageTimeout);
            RunAction("mute-off", () => player.SetMuted(false));
            WaitForState(player, "mute-off", state => !state.IsMuted, ControlStageTimeout);

            double beforeSkip = player.State.Position;
            RunAction("skip-forward", () => player.Skip(TimeSpan.FromSeconds(2)));
            WaitForState(player, "skip-forward", state => state.Position >= beforeSkip, ControlStageTimeout);

            RunAction("rate-1.5x", () => player.SetPlaybackRate(1.5));
            WaitForState(player, "rate-1.5x", state => Math.Abs(state.PlaybackRate - 1.5) < .03, ControlStageTimeout);
            RunAction("rate-nominal", () => player.SetPlaybackRate(1));
            WaitForState(player, "rate-nominal", state => Math.Abs(state.PlaybackRate - 1) < .03, ControlStageTimeout);

            StopAndWait(player, "stop");
            OpenAndWait(player, mediaPath, "reopen-after-stop");
            StopAndWait(player, "stop-after-reopen");
            DisposeAndWait(player, "dispose");
            player = null;

            player = new VLCCorePlayer();
            OpenAndWait(player, mediaPath, "reopen-after-dispose");
            StopAndWait(player, "final-stop");
            DisposeAndWait(player, "final-dispose");
            player = null;

            WaitForReleaseBalance(
                monitor,
                initial.ReleaseBalance,
                "control lifecycle");

            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                monitor.TryTakeLatestGPU(out _);
                CompositionPluginBridge.PoolTelemetry before =
                    monitor.ReadPoolTelemetry();
                using VLCCorePlayer cyclePlayer = new();
                OpenAndWait(
                    cyclePlayer,
                    mediaPath,
                    $"stress-{cycle}-open");
                WaitForOpaquePool(
                    monitor,
                    before,
                    $"stress-{cycle}");
                StopAndWait(cyclePlayer, $"stress-{cycle}-stop");
                cyclePlayer.Dispose();
                WaitForReleaseBalance(
                    monitor,
                    before.ReleaseBalance,
                    $"stress-{cycle}");
                Console.WriteLine(
                    $"[engine-smoke] PASS stress-cycle={cycle}/{cycles}");
            }

            CompositionPluginBridge.PoolTelemetry final =
                monitor.ReadPoolTelemetry();
            Console.WriteLine(final.ToMachineReadableString());
            if (final.CpuUploads != initial.CpuUploads)
                throw new InvalidOperationException(
                    "Hardware opaque lifecycle performed a CPU upload.");
            Console.WriteLine(
                $"[engine-smoke] PASS lifecycle cycles={cycles} " +
                $"adapter-luid={adapter}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[engine-smoke] FAIL: {exception}");
            return 1;
        }
        finally
        {
            if (player is not null)
            {
                try
                {
                    player.Dispose();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"[engine-smoke] Cleanup failed: {exception.Message}");
                }
            }
        }
    }

    private static void WaitForOpaquePool(
        CompositionPluginBridge monitor,
        CompositionPluginBridge.PoolTelemetry before,
        string stage)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < PlaybackStageTimeout)
        {
            CompositionPluginBridge.PoolTelemetry current =
                monitor.ReadPoolTelemetry();
            if (current.OpaquePoolGenerations >
                    before.OpaquePoolGenerations &&
                current.OpaquePicturesReceived >
                    before.OpaquePicturesReceived &&
                monitor.TryTakeLatestGPU(
                    out CompositionPluginBridge.GPUFrame? frame) &&
                frame is not null)
            {
                try
                {
                    string probe =
                        D3D11SharedTextureProbe.OpenAndSynchronize(frame);
                    Console.WriteLine(
                        $"[engine-smoke] PASS {stage}: " +
                        $"opaque-pictures=" +
                        $"{current.OpaquePicturesReceived - before.OpaquePicturesReceived} " +
                        $"pool-generation={current.PoolGenerations} " +
                        $"path=hardware-opaque-gpu probe={probe}");
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    // A callback descriptor is non-owning and can be replaced
                    // while the previous vout tears down. Ignore that stale
                    // descriptor and require a frame from the live resource.
                    Console.WriteLine(
                        $"[engine-smoke] {stage}: discarded stale GPU frame: " +
                        exception.Message);
                }
            }
            Thread.Sleep(20);
        }

        CompositionPluginBridge.PoolTelemetry last =
            monitor.ReadPoolTelemetry();
        Console.Error.WriteLine(last.ToMachineReadableString());
        throw new TimeoutException(
            $"Stage '{stage}' did not receive an opaque D3D11 picture " +
            $"within {PlaybackStageTimeout.TotalSeconds:0.#} seconds.");
    }

    private static void WaitForReleaseBalance(
        CompositionPluginBridge monitor,
        long expectedBalance,
        string stage)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < ControlStageTimeout)
        {
            CompositionPluginBridge.PoolTelemetry current =
                monitor.ReadPoolTelemetry();
            if (current.ReleaseBalance == expectedBalance)
                return;
            Thread.Sleep(20);
        }

        CompositionPluginBridge.PoolTelemetry last =
            monitor.ReadPoolTelemetry();
        Console.Error.WriteLine(last.ToMachineReadableString());
        throw new InvalidOperationException(
            $"Stage '{stage}' left opaque picture release balance " +
            $"{last.ReleaseBalance}; expected {expectedBalance}.");
    }

    private static void OpenAndWait(
        VLCCorePlayer player,
        string mediaPath,
        string stage)
    {
        RunAction(stage, () => player.Open(mediaPath));
        WaitForState(
            player,
            stage,
            state => state.Status == Player.Contracts.PlaybackStatus.Playing &&
                state.CanSeek,
            PlaybackStageTimeout);
    }

    private static void StopAndWait(VLCCorePlayer player, string stage)
    {
        RunAction(stage, player.Stop, TimeSpan.FromSeconds(12));
        WaitForState(
            player,
            stage,
            state => state.Status == Player.Contracts.PlaybackStatus.Stopped,
            ControlStageTimeout);
    }

    private static void DisposeAndWait(VLCCorePlayer player, string stage) =>
        RunAction(stage, player.Dispose, TimeSpan.FromSeconds(12));

    private static void RunAction(
        string stage,
        Action action,
        TimeSpan? timeout = null)
    {
        Task task = Task.Run(action);
        TimeSpan limit = timeout ?? ControlStageTimeout;
        if (!task.Wait(limit))
            throw new TimeoutException(
                $"Stage '{stage}' did not complete within " +
                $"{limit.TotalSeconds:0.#} seconds.");
        if (task.IsFaulted)
            throw new InvalidOperationException(
                $"Stage '{stage}' failed.",
                task.Exception?.GetBaseException());
        Console.WriteLine($"[engine-smoke] PASS {stage}");
    }

    private static void WaitForState(
        VLCCorePlayer player,
        string stage,
        Func<Player.Contracts.MediaEngineState, bool> predicate,
        TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            Player.Contracts.MediaEngineState state = player.State;
            if (state.Status == Player.Contracts.PlaybackStatus.Faulted)
            {
                throw new InvalidOperationException(
                    $"Stage '{stage}' faulted: {state.Error}");
            }
            if (predicate(state))
            {
                Console.WriteLine(
                    $"[engine-smoke] PASS {stage}: status={state.Status} " +
                    $"position={state.Position:F3} volume={state.Volume:F2}");
                return;
            }
            Thread.Sleep(20);
        }

        Player.Contracts.MediaEngineState last = player.State;
        throw new TimeoutException(
            $"Stage '{stage}' timed out after {timeout.TotalSeconds:0.#} " +
            $"seconds. Last state: status={last.Status}, " +
            $"position={last.Position:F3}, volume={last.Volume:F2}, " +
            $"canSeek={last.CanSeek}, error={last.Error ?? "<none>"}.");
    }
}

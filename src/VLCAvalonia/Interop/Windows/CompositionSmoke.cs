#if SMOKE
using System.Diagnostics;
using System.Runtime.Versioning;
using static VLCAvalonia.Interop.EngineSmoke;

namespace VLCAvalonia.Interop.Windows;

/// <summary>Native smokes for the Windows D3D11 video output.</summary>
[SupportedOSPlatform("windows")]
internal static class CompositionSmoke
{
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
            using D3D11PluginBridge bridge = new();
            bool expectHardware = expectedPath == "hardware-opaque-gpu";
            bool expectGPU = expectedPath != "cpu-bitmap";
            if (expectGPU)
            {
                D3D11PluginBridge.AdapterLuid adapter =
                    D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
                bridge.ConfigureAdapter(adapter);
                bridge.EnableGPUFrames();
            }
            else
            {
                bridge.ClearAdapterSelection();
            }
            bridge.ForceCPUOutput(!expectHardware);
            D3D11PluginBridge.PoolTelemetry before =
                bridge.ReadPoolTelemetry();
            bool receivedFrame = false;
            string? actualPath = null;
            using (VlcCorePlayer player = new(
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
                            out D3D11PluginBridge.GPUFrame? GPUFrame) &&
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
                            out D3D11PluginBridge.Frame? frame) &&
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

            D3D11PluginBridge.PoolTelemetry after =
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

    /// <summary>The engine lifecycle smoke with D3D11 opaque-picture checks.</summary>
    public static int RunEngine(int cycles) => EngineSmoke.RunEngine(cycles, () => new D3D11EngineVideo());

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
            using D3D11PluginBridge monitor = new();
            D3D11PluginBridge.AdapterLuid adapter =
                D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
            monitor.ConfigureAdapter(adapter);
            monitor.ForceCPUOutput(false);
            monitor.EnableGPUFrames();
            D3D11PluginBridge.PoolTelemetry before =
                monitor.ReadPoolTelemetry();
            using (VlcCorePlayer player = new())
            {
                OpenAndWait(player, mediaPath, "opaque-open");
                WaitForOpaquePool(monitor, before, "opaque-open");
                StopAndWait(player, "opaque-stop");
            }

            WaitForReleaseBalance(monitor, before.ReleaseBalance, "opaque");
            D3D11PluginBridge.PoolTelemetry after =
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

    /// <summary>
    /// Requires opaque D3D11 pictures on the compositor's adapter for every
    /// stage, balanced picture releases and zero CPU uploads.
    /// </summary>
    private sealed class D3D11EngineVideo : IEngineSmokeVideo
    {
        private readonly D3D11PluginBridge _monitor = new();
        private readonly D3D11PluginBridge.AdapterLuid _adapter;
        private readonly D3D11PluginBridge.PoolTelemetry _initial;
        private D3D11PluginBridge.PoolTelemetry _mark;

        public D3D11EngineVideo()
        {
            try
            {
                _adapter = D3D11SharedTextureProbe.GetDefaultHardwareAdapterForSmoke();
                _monitor.ConfigureAdapter(_adapter);
                _monitor.ForceCPUOutput(false);
                _monitor.EnableGPUFrames();
                _initial = _mark = _monitor.ReadPoolTelemetry();
            }
            catch
            {
                _monitor.Dispose();
                throw;
            }
        }

        public string VideoOutput => VlcCorePlayer.DefaultVideoOutput;
        public bool HardwareDecoding => true;
        public string Description => $"adapter-luid={_adapter}";

        public void Mark()
        {
            _monitor.TryTakeLatestGPU(out _);
            _mark = _monitor.ReadPoolTelemetry();
        }

        public void WaitForFrames(string stage) => WaitForOpaquePool(_monitor, _mark, stage);
        public void WaitForRelease(string stage) => WaitForReleaseBalance(_monitor, _mark.ReleaseBalance, stage);

        public void Complete()
        {
            D3D11PluginBridge.PoolTelemetry final = _monitor.ReadPoolTelemetry();
            Console.WriteLine(final.ToMachineReadableString());
            if (final.CpuUploads != _initial.CpuUploads)
                throw new InvalidOperationException(
                    "Hardware opaque lifecycle performed a CPU upload.");
        }

        public void Dispose() => _monitor.Dispose();
    }

    private static void WaitForOpaquePool(
        D3D11PluginBridge monitor,
        D3D11PluginBridge.PoolTelemetry before,
        string stage)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < PlaybackStageTimeout)
        {
            D3D11PluginBridge.PoolTelemetry current =
                monitor.ReadPoolTelemetry();
            if (current.OpaquePoolGenerations >
                    before.OpaquePoolGenerations &&
                current.OpaquePicturesReceived >
                    before.OpaquePicturesReceived &&
                monitor.TryTakeLatestGPU(
                    out D3D11PluginBridge.GPUFrame? frame) &&
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

        D3D11PluginBridge.PoolTelemetry last =
            monitor.ReadPoolTelemetry();
        Console.Error.WriteLine(last.ToMachineReadableString());
        throw new TimeoutException(
            $"Stage '{stage}' did not receive an opaque D3D11 picture " +
            $"within {PlaybackStageTimeout.TotalSeconds:0.#} seconds.");
    }

    private static void WaitForReleaseBalance(
        D3D11PluginBridge monitor,
        long expectedBalance,
        string stage)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < ControlStageTimeout)
        {
            D3D11PluginBridge.PoolTelemetry current =
                monitor.ReadPoolTelemetry();
            if (current.ReleaseBalance == expectedBalance)
                return;
            Thread.Sleep(20);
        }

        D3D11PluginBridge.PoolTelemetry last =
            monitor.ReadPoolTelemetry();
        Console.Error.WriteLine(last.ToMachineReadableString());
        throw new InvalidOperationException(
            $"Stage '{stage}' left opaque picture release balance " +
            $"{last.ReleaseBalance}; expected {expectedBalance}.");
    }

}
#endif // SMOKE

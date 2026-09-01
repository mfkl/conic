using System.Diagnostics;

namespace Conic.Interop;

internal static class CompositionSmoke
{
    private static readonly TimeSpan PlaybackStageTimeout =
        TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ControlStageTimeout =
        TimeSpan.FromSeconds(5);

    public static int Run()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine($"Smoke media not found: {mediaPath}");
            return 2;
        }

        try
        {
            using CompositionPluginBridge bridge = new();
            bridge.ClearAdapterSelection();
            using VLCCorePlayer player = new();
            player.StateChanged += (_, state) =>
                Console.WriteLine(
                    $"[composition-smoke] {state.Status}: {state.Error}");
            player.Open(mediaPath);

            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (bridge.TryTakeLatest(
                        out CompositionPluginBridge.Frame? frame) &&
                    frame is not null)
                {
                    Console.WriteLine(
                        "[composition-smoke] path=cpu-bgra " +
                        $"size={frame.Width}x{frame.Height} " +
                        $"row-bytes={frame.RowBytes} " +
                        $"pts={frame.PresentationTimestamp}");
                    Thread.Sleep(500);
                    return 0;
                }

                Thread.Sleep(20);
            }

            Console.Error.WriteLine(
                "[composition-smoke] No CPU fallback frame arrived within " +
                "15 seconds.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[composition-smoke] {exception}");
            return 3;
        }
    }

    public static int RunEngine()
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
            player = new VLCCorePlayer();
            OpenAndWait(player, mediaPath, "open");

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

            Console.WriteLine("[engine-smoke] PASS lifecycle");
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

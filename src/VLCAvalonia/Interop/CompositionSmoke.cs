using System.Diagnostics;
using System.Runtime.Versioning;
using Player.Contracts;
using Player.Application;
using VLCLR;

namespace VLCAvalonia.Interop;

[SupportedOSPlatform("windows")]
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

    public static int RunQueue(bool verboseLogging = false) =>
        RunQueueAsync(verboseLogging).GetAwaiter().GetResult();

    private static async Task<int> RunQueueAsync(bool verboseLogging)
    {
        string fixture = RepositoryPaths.TestMediaPath;
        if (!File.Exists(fixture)) { Console.Error.WriteLine($"[queue-smoke] Smoke media not found: {fixture}"); return 2; }
        string directory = Path.Combine(Path.GetTempPath(), "vlc-avalonia-queue-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string[] copies = Enumerable.Range(1, 3).Select(index => Path.Combine(directory, $"queue-{index}.mkv")).ToArray();
            foreach (string copy in copies) File.Copy(fixture, copy);
            string[] uris = copies.Select(path => new Uri(path).AbsoluteUri).ToArray();
            VLCCorePlayer player = new(verboseLogging: verboseLogging);
            player.OpenEmptyQueueSessionForSmoke();
            WaitForQueueState(player, "queue empty session", state => state.Status == QueueLoadState.Empty, PlaybackStageTimeout);
            QueueSmokeDiagnostics adapter = new(player, player);
            VLCPlaylistSmokeReport report = await VLCPlaylistQueueSmoke.RunAsync(adapter, uris, async token => { await Task.Delay(80, token); }, new VLCPlaylistSmokeOptions(80));
            if (!report.Succeeded)
            {
                Console.Error.WriteLine($"[queue-smoke] FAIL phase={report.Phase} result={report.Result.Code} detail={report.Detail}; trace={adapter.StructuredDetail}");
                DisposeQueueSmokePlayer(player);
                return 1;
            }
            player.Enqueue([uris[0]]);
            WaitForQueueState(player, "queue recovery enqueue", state => state.Status == QueueLoadState.Ready && state.Items.Count == 1, PlaybackStageTimeout);
            player.SelectAndPlay(player.QueueState.Items[0].Token);
            WaitForState(player, "queue recovery play", state => state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused, PlaybackStageTimeout);
            if (!DisposeQueueSmokePlayer(player)) return 1;
            Console.WriteLine("[queue-smoke] PASS add clear reorder select remove previous next repeat shuffle clear");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"[queue-smoke] FAIL {exception}"); return 1; }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); Console.WriteLine("[queue-smoke] temporary copies cleaned"); }
            catch (Exception exception) { Console.Error.WriteLine($"[queue-smoke] cleanup failed: {exception.Message}"); }
        }
    }

    private static bool DisposeQueueSmokePlayer(VLCCorePlayer player)
    {
        Stopwatch total = Stopwatch.StartNew();
        try
        {
            player.Dispose();
            Console.WriteLine($"[queue-smoke] teardown=pass attempt=1 elapsedMs={total.ElapsedMilliseconds}");
            return true;
        }
        catch (TimeoutException exception)
        {
            Console.Error.WriteLine($"[queue-smoke] teardown=timeout attempt=1 elapsedMs={total.ElapsedMilliseconds}; {exception.Message}");
        }

        Stopwatch retry = Stopwatch.StartNew();
        try
        {
            player.Dispose();
            Console.WriteLine($"[queue-smoke] teardown=pass attempt=2 totalMs={total.ElapsedMilliseconds} retryMs={retry.ElapsedMilliseconds}");
            return true;
        }
        catch (TimeoutException exception)
        {
            Console.Error.WriteLine($"[queue-smoke] teardown=timeout attempt=2 totalMs={total.ElapsedMilliseconds} retryMs={retry.ElapsedMilliseconds}; {exception.Message}");
            return false;
        }
    }

    private static void WaitForQueueState(
        VLCCorePlayer player,
        string stage,
        Func<PlaybackQueueState, bool> condition,
        TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            PlaybackQueueState state = player.QueueState;
            if (condition(state))
            {
                Console.WriteLine($"[queue-smoke] PASS {stage}: generation={state.Generation} status={state.Status} count={state.Items.Count}");
                return;
            }
            Thread.Sleep(20);
        }
        PlaybackQueueState last = player.QueueState;
        throw new TimeoutException($"Queue stage '{stage}' timed out: generation={last.Generation} status={last.Status} count={last.Items.Count}.");
    }

    /// <summary>Bounded diagnostic wrapper for the native queue contract.</summary>
    private sealed class QueueSmokeDiagnostics(
        IVLCPlaylistQueueSmokeAdapter inner,
        VLCCorePlayer player) : IVLCPlaylistQueueSmokeAdapter
    {
        private readonly Queue<string> _samples = new();
        private string _stage = "initial";
        private int _polls;
        private int _stagePolls;
        private int? _selectedItemId;
        private int? _removedItemId;
        private bool _recordedSelectCompletion;
        private bool _recordedRemoveCompletion;
        public string StructuredDetail => string.Join(" | ", _samples);
        public VLCPlaylistSnapshot? TryGetSnapshot(out VLCPlaylistResult result)
        {
            VLCPlaylistSnapshot? snapshot = inner.TryGetSnapshot(out result);
            _polls++; _stagePolls++;
            bool important = snapshot is null || _stagePolls <= 2 || _stagePolls % 8 == 0;
            if (important)
                AddSnapshot($"snapshot-poll={_polls}", snapshot, result);
            if (!_recordedRemoveCompletion && _stage == "remove" && snapshot is not null && _removedItemId is int removed && snapshot.Items.All(item => item.Id != removed))
            {
                _recordedRemoveCompletion = true;
                AddSnapshot("after-remove", snapshot, result);
            }
            return snapshot;
        }
        public bool TryGetActiveCurrentItemId(out int? itemId, out VLCPlaylistResult result)
        {
            bool available = inner.TryGetActiveCurrentItemId(out itemId, out result);
            _stagePolls++;
            if (!_recordedSelectCompletion && _stage == "select" && itemId == _selectedItemId)
            {
                _recordedSelectCompletion = true;
                CaptureFreshSnapshot("select-complete");
            }
            bool sampled = !available || _stagePolls <= 5 || _stagePolls % 8 == 0;
            if (sampled)
                CaptureFreshSnapshot($"active-probe={_stagePolls}");
            if (sampled)
                Add($"stage={_stage};activeCurrent={itemId?.ToString() ?? "null"};available={available};result={result.Code};engine={player.State.Status}/g{player.State.Generation}");
            return available;
        }
        public VLCPlaylistResult Add(IEnumerable<string> uris, bool playFirst = false) { SetStage("add"); return inner.Add(uris, playFirst); }
        public VLCPlaylistResult Clear(VLCPlaylistSnapshot snapshot)
        {
            bool isFinalClear = _stage == "order:Random";
            if (isFinalClear)
            {
                long oldGeneration = player.QueueState.Generation;
                player.ClearQueue();
                PlaybackQueueState cleared = player.QueueState;
                Add($"clear-reset;oldGeneration={oldGeneration};newGeneration={cleared.Generation};status={cleared.Status};count={cleared.Items.Count}");
                if (cleared.Generation <= oldGeneration || cleared.Status != QueueLoadState.Empty || cleared.Items.Count != 0)
                    return new VLCPlaylistResult(VLCPlaylistResultCode.NativeFailure);
                player.OpenEmptyQueueSessionForSmoke();
                try
                {
                    WaitForQueueState(player, "queue reset session", state => state.Status == QueueLoadState.Empty && state.Generation > cleared.Generation, PlaybackStageTimeout);
                    SetStage("clear");
                    return VLCPlaylistResult.Success;
                }
                catch (Exception exception)
                {
                    Add($"clear-reset-failed={exception.Message}");
                    return new VLCPlaylistResult(VLCPlaylistResultCode.NativeFailure);
                }
            }
            SetStage("clear");
            return inner.Clear(snapshot);
        }
        public VLCPlaylistResult Move(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item, int destinationIndex) { SetStage("move"); return inner.Move(snapshot, item, destinationIndex); }
        public VLCPlaylistResult SelectAndPlay(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) { SetStage("select"); _selectedItemId = item.Id; VLCPlaylistResult result = inner.SelectAndPlay(snapshot, item); Add($"select-dispatched={result.Code};selected={item.Id};engine={player.State.Status}/g{player.State.Generation}"); return result; }
        public VLCPlaylistResult Remove(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) { SetStage("remove"); _removedItemId = item.Id; VLCPlaylistResult result = inner.Remove(snapshot, item); Add($"remove-result={result.Code};item={item.Id};engine={player.State.Status}/g{player.State.Generation}"); return result; }
        public VLCPlaylistResult Next() { SetStage("next"); CaptureFreshSnapshot("before-next"); VLCPlaylistResult result = inner.Next(); Add($"next-dispatched={result.Code};engine={player.State.Status}/g{player.State.Generation}"); return result; }
        public VLCPlaylistResult Previous() { SetStage("previous"); return inner.Previous(); }
        public VLCPlaylistModeSnapshot? TryGetModes(out VLCPlaylistResult result)
        {
            VLCPlaylistModeSnapshot? modes = inner.TryGetModes(out result);
            _stagePolls++;
            if (_stagePolls <= 3 || _stagePolls % 8 == 0 || modes is null)
                Add($"stage={_stage};modes={(modes is { } mode ? $"repeat={mode.Repeat},order={mode.Order},repeatVar={mode.RepeatVariable},loopVar={mode.LoopVariable}" : "null")};result={result.Code};engine={player.State.Status}/g{player.State.Generation}");
            return modes;
        }
        public VLCPlaylistResult SetRepeatMode(VLCPlaylistRepeatMode mode)
        {
            SetStage($"repeat:{mode}");
            player.SetRepeatMode(mode switch { VLCPlaylistRepeatMode.CurrentItem => QueueRepeatMode.CurrentItem, VLCPlaylistRepeatMode.All => QueueRepeatMode.All, _ => QueueRepeatMode.None });
            return VLCPlaylistResult.Success;
        }
        public VLCPlaylistResult SetOrder(VLCPlaylistOrder order)
        {
            SetStage($"order:{order}");
            player.SetOrder(order == VLCPlaylistOrder.Random ? QueueOrder.Random : QueueOrder.Normal);
            return VLCPlaylistResult.Success;
        }
        private void SetStage(string stage) { _stage = stage; _stagePolls = 0; }
        private void CaptureFreshSnapshot(string point)
        {
            using VLCPlaylistSnapshot? snapshot = inner.TryGetSnapshot(out VLCPlaylistResult result);
            AddSnapshot(point, snapshot, result);
        }
        private void AddSnapshot(string point, VLCPlaylistSnapshot? snapshot, VLCPlaylistResult result)
        {
            string queue = snapshot is null ? "snapshot=null" : $"v={snapshot.Version},current={snapshot.CurrentItemId?.ToString() ?? "null"},index={snapshot.CurrentIndex},canNext={snapshot.CanNext},count={snapshot.Items.Count}";
            Add($"stage={_stage};{point};{queue};result={result.Code};engine={player.State.Status}/g{player.State.Generation}");
        }
        private void Add(string value) { if (_samples.Count == 40) _samples.Dequeue(); _samples.Enqueue(value); }
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

    public static int RunMediaCapabilities()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath)) { Console.Error.WriteLine($"[media-capabilities-smoke] FAIL fixture missing: {mediaPath}"); return 2; }
        string attachment = Path.Combine(Path.GetTempPath(), $"vlc-avalonia-{Guid.NewGuid():N}.srt");
        VLCCorePlayer? player = null;
        try
        {
            File.WriteAllText(attachment, "1\n00:00:00,000 --> 00:00:01,000\nCapability smoke\n");
            player = new VLCCorePlayer(hardwareDecoding: false);
            OpenAndWait(player, mediaPath, "media-capabilities-open");
            IVLCMediaCapabilitySmokeAdapter adapter = new DispatchingMediaCapabilitySmokeAdapter(player);
            VLCTitleChapterSnapshot? titleSnapshot = adapter.TryGetTitlesAndChapters(out VLCMediaResult titleRead);
            VLCMediaSmokeReport report = VLCMediaCapabilitiesSmoke.RunAsync(
                adapter,
                _ => { player.PollMediaCapabilitiesForSmoke(); return ValueTask.CompletedTask; },
                new VLCMediaSmokeAttachment(attachment, VLCExternalTrackKind.Subtitle)).AsTask().GetAwaiter().GetResult();
            int passed = 0, skipped = 0, failed = 0;
            foreach (VLCMediaSmokeStep step in report.Steps)
            {
                switch (step.Disposition) { case VLCMediaSmokeDisposition.Passed: passed++; break; case VLCMediaSmokeDisposition.Skipped: skipped++; break; default: failed++; break; }
                Console.WriteLine($"[media-capabilities-smoke] {step.Disposition} phase={step.Phase} result={step.Result.Code} detail={step.Detail ?? "none"}");
            }
            // VLCLR's compact smoke reports title/chapter selection as one operation; make fixture absences explicit in the product ledger.
            if (titleSnapshot is not null && titleSnapshot.Titles.Count == 0)
            {
                skipped++;
                Console.WriteLine("[media-capabilities-smoke] Skipped phase=Titles result=Success detail=Fixture exposes no titles.");
            }
            if (titleSnapshot is not null && titleSnapshot.Chapters.Count == 0)
            {
                skipped++;
                Console.WriteLine("[media-capabilities-smoke] Skipped phase=Chapters result=Success detail=Fixture exposes no chapters.");
            }
            if (titleSnapshot is null)
            {
                failed++;
                Console.WriteLine($"[media-capabilities-smoke] Failed phase=Titles result={titleRead.Code} detail=Initial title/chapter readback failed.");
            }
            Console.WriteLine($"[media-capabilities-smoke] completed passed={passed} skipped={skipped} failed={failed}; skipped phases are not parity assertions.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception exception) { Console.Error.WriteLine($"[media-capabilities-smoke] FAIL: {exception}"); return 1; }
        finally
        {
            try { player?.Dispose(); } catch (Exception exception) { Console.Error.WriteLine($"[media-capabilities-smoke] cleanup player: {exception.Message}"); }
            try { if (File.Exists(attachment)) File.Delete(attachment); } catch (Exception exception) { Console.Error.WriteLine($"[media-capabilities-smoke] cleanup attachment: {exception.Message}"); }
        }
    }

    public static int RunPlayerCapabilities()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath)) { Console.Error.WriteLine($"[player-capabilities-smoke] FAIL fixture missing: {mediaPath}"); return 2; }
        VLCCorePlayer? player = null;
        try
        {
            player = new VLCCorePlayer(hardwareDecoding: false);
            OpenAndWait(player, mediaPath, "player-capabilities-open");
            VLCPlayerSmokeReport report = player.RunPlayerCapabilitiesForSmoke();
            int passed = 0, skipped = 0, failed = 0;
            foreach (VLCPlayerSmokeStep step in report.Steps)
            {
                if (step.Disposition == VLCPlayerSmokeDisposition.Passed) passed++; else if (step.Disposition == VLCPlayerSmokeDisposition.Skipped) skipped++; else failed++;
                Console.WriteLine($"[player-capabilities-smoke] {step.Disposition} phase={step.Phase} result={step.Result.Code} detail={step.Detail}");
            }
            Console.WriteLine($"[player-capabilities-smoke] completed passed={passed} skipped={skipped} failed={failed}; skipped features are not parity assertions.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception exception) { Console.Error.WriteLine($"[player-capabilities-smoke] FAIL: {exception}"); return 1; }
        finally { try { player?.Dispose(); } catch (Exception exception) { Console.Error.WriteLine($"[player-capabilities-smoke] cleanup player: {exception.Message}"); } }
    }

    private sealed class DispatchingMediaCapabilitySmokeAdapter(VLCCorePlayer player) : IVLCMediaCapabilitySmokeAdapter
    {
        public VLCTrackSnapshot? TryGetTracks(out VLCMediaResult result)
        {
            (VLCTrackSnapshot? Snapshot, VLCMediaResult Result) value = player.InvokeMediaCapabilityForSmoke(adapter => (adapter.TryGetTracks(out VLCMediaResult read), read)); result = value.Result; return value.Snapshot;
        }
        public VLCTitleChapterSnapshot? TryGetTitlesAndChapters(out VLCMediaResult result)
        {
            (VLCTitleChapterSnapshot? Snapshot, VLCMediaResult Result) value = player.InvokeMediaCapabilityForSmoke(adapter => (adapter.TryGetTitlesAndChapters(out VLCMediaResult read), read)); result = value.Result; return value.Snapshot;
        }
        public VLCMediaDelaySnapshot? TryGetDelays(out VLCMediaResult result)
        {
            (VLCMediaDelaySnapshot? Snapshot, VLCMediaResult Result) value = player.InvokeMediaCapabilityForSmoke(adapter => (adapter.TryGetDelays(out VLCMediaResult read), read)); result = value.Result; return value.Snapshot;
        }
        public VLCMediaResult SelectTrack(VLCTrackSnapshot snapshot, VLCTrackKind kind, long id) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.SelectTrack(snapshot, kind, id));
        public VLCMediaResult SelectTitle(VLCTitleChapterSnapshot snapshot, long id) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.SelectTitle(snapshot, id));
        public VLCMediaResult SelectChapter(VLCTitleChapterSnapshot snapshot, long id) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.SelectChapter(snapshot, id));
        public VLCMediaResult SetDelay(VLCMediaDelayKind kind, TimeSpan delay) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.SetDelay(kind, delay));
        public VLCMediaResult ResetDelay(VLCMediaDelayKind kind) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.ResetDelay(kind));
        public VLCExternalTrackAttachmentResult AttachExternalTrack(string path, VLCExternalTrackKind kind) => player.InvokeMediaCapabilityForSmoke(adapter => adapter.AttachExternalTrack(path, kind));
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

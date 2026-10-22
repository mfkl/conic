#if SMOKE
using System.Diagnostics;
using System.Runtime.Versioning;
using Player.Contracts;
using Player.Application;
using VLCLR;

namespace Conic.Interop;

/// <summary>
/// Checks one stage's video output during <see cref="EngineSmoke.RunEngine"/>.
/// <see cref="Mark"/> sets the baseline the other checks compare against.
/// </summary>
internal interface IEngineSmokeVideo : IDisposable
{
    /// <summary>The VLC <c>--vout</c> module the players use.</summary>
    string VideoOutput { get; }
    /// <summary>Whether the players request hardware decoding.</summary>
    bool HardwareDecoding { get; }
    /// <summary>Machine-readable summary for the final PASS line.</summary>
    string Description { get; }
    void Mark();
    void WaitForFrames(string stage);
    void WaitForRelease(string stage);
    /// <summary>Final whole-run checks.</summary>
    void Complete();
}

/// <summary>
/// No video checks: frames go to VLC's dummy output. For targets whose GPU
/// video output is not implemented; this qualifies the engine only. Decoding
/// stays in software: hardware decoding belongs to the GPU backend's
/// qualification, and without a GPU (WSL) VLC 3 can hang tearing it down.
/// </summary>
internal sealed class NoEngineSmokeVideo : IEngineSmokeVideo
{
    public string VideoOutput => "dummy";
    public bool HardwareDecoding => false;
    public string Description => "video=dummy (engine only; no video output qualified)";
    public void Mark() { }
    public void WaitForFrames(string stage) { }
    public void WaitForRelease(string stage) { }
    public void Complete() { }
    public void Dispose() { }
}

/// <summary>Headless engine checks shared by every target.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static class EngineSmoke
{
    internal static readonly TimeSpan PlaybackStageTimeout =
        TimeSpan.FromSeconds(12);
    internal static readonly TimeSpan ControlStageTimeout =
        TimeSpan.FromSeconds(5);

    /// <summary>
    /// The video output for checks that do not inspect video. Windows keeps
    /// its composition output; other targets discard frames.
    /// </summary>
    internal static string HeadlessVideoOutput =>
        OperatingSystem.IsWindows() ? VlcCorePlayer.DefaultVideoOutput : "dummy";

    /// <summary>Hardware decoding needs a GPU video output; see <see cref="NoEngineSmokeVideo"/>.</summary>
    internal static bool HeadlessHardwareDecoding => OperatingSystem.IsWindows();

    public static int RunQueue(bool verboseLogging = false) =>
        RunQueueAsync(verboseLogging).GetAwaiter().GetResult();

    private static async Task<int> RunQueueAsync(bool verboseLogging)
    {
        string fixture = RepositoryPaths.TestMediaPath;
        if (!File.Exists(fixture)) { Console.Error.WriteLine($"[queue-smoke] Smoke media not found: {fixture}"); return 2; }
        string directory = Path.Combine(Path.GetTempPath(), "conic-queue-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string[] copies = Enumerable.Range(1, 3).Select(index => Path.Combine(directory, $"queue-{index}.mkv")).ToArray();
            foreach (string copy in copies) File.Copy(fixture, copy);
            string[] uris = copies.Select(path => new Uri(path).AbsoluteUri).ToArray();
            VlcCorePlayer player = new(hardwareDecoding: HeadlessHardwareDecoding, verboseLogging: verboseLogging, videoOutput: HeadlessVideoOutput);
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
            // The playback state names the item VLC plays: history, the title and
            // Continue follow it when the queue moves on within one open.
            // The shuffle phase may leave either copy first.
            string[] pair = [copies[0], copies[1]];
            player.OpenQueue(pair);
            WaitForState(player, "queue first item", state => state.Status == PlaybackStatus.Playing && pair.Any(path => SamePath(state.Source, path)), PlaybackStageTimeout);
            long queueGeneration = player.State.Generation;
            string other = pair.First(path => !SamePath(player.State.Source, path));
            WaitForQueueState(player, "queue pair ready", state => state.Status == QueueLoadState.Ready && state.Items.Count == 2, PlaybackStageTimeout);
            player.SelectAndPlay(player.QueueState.Items.First(item => SamePath(new Uri(item.Source).LocalPath, other)).Token);
            WaitForState(player, "queue next item", state => state.Status == PlaybackStatus.Playing && SamePath(state.Source, other), PlaybackStageTimeout);
            if (player.State.Generation != queueGeneration || player.State.Title != Path.GetFileNameWithoutExtension(other))
                throw new InvalidOperationException($"Queue item change reported generation={player.State.Generation} (expected {queueGeneration}) title={player.State.Title}.");
            if (!DisposeQueueSmokePlayer(player)) return 1;
            Console.WriteLine("[queue-smoke] PASS add clear reorder select remove previous next repeat shuffle clear item-source");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine($"[queue-smoke] FAIL {exception}"); return 1; }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); Console.WriteLine("[queue-smoke] temporary copies cleaned"); }
            catch (Exception exception) { Console.Error.WriteLine($"[queue-smoke] cleanup failed: {exception.Message}"); }
        }
    }

    private static bool SamePath(string? source, string path) =>
        source is not null && string.Equals(Path.GetFullPath(source), Path.GetFullPath(path), StringComparison.Ordinal);

    private static bool DisposeQueueSmokePlayer(VlcCorePlayer player)
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
        VlcCorePlayer player,
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
        VlcCorePlayer player) : IVLCPlaylistQueueSmokeAdapter
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

    /// <summary>
    /// Transport, volume, rate and stop/reopen/dispose lifecycle, then
    /// <paramref name="cycles"/> open/stop/dispose cycles. The video monitor
    /// checks each stage's frames and picture balance.
    /// </summary>
    public static int RunEngine(int cycles, Func<IEngineSmokeVideo> createVideo)
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine(
                $"[engine-smoke] Smoke media not found: {mediaPath}");
            return 2;
        }

        VlcCorePlayer? player = null;
        try
        {
            using IEngineSmokeVideo video = createVideo();
            player = new VlcCorePlayer(hardwareDecoding: video.HardwareDecoding, videoOutput: video.VideoOutput);
            OpenAndWait(player, mediaPath, "open");
            video.WaitForFrames("open");

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

            foreach (double volumeTarget in new[] { 0.42, 1.5, 2.0 })
            {
                string stage = $"volume-{volumeTarget:0.##}";
                RunAction(stage, () => player.SetVolume(volumeTarget));
                WaitForState(
                    player,
                    stage,
                    state => Math.Abs(state.Volume - volumeTarget) < 0.03,
                    ControlStageTimeout);
            }

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

            player = new VlcCorePlayer(hardwareDecoding: video.HardwareDecoding, videoOutput: video.VideoOutput);
            OpenAndWait(player, mediaPath, "reopen-after-dispose");
            StopAndWait(player, "final-stop");
            DisposeAndWait(player, "final-dispose");
            player = null;

            video.WaitForRelease("control lifecycle");

            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                video.Mark();
                using VlcCorePlayer cyclePlayer = new(hardwareDecoding: video.HardwareDecoding, videoOutput: video.VideoOutput);
                OpenAndWait(
                    cyclePlayer,
                    mediaPath,
                    $"stress-{cycle}-open");
                video.WaitForFrames($"stress-{cycle}");
                StopAndWait(cyclePlayer, $"stress-{cycle}-stop");
                cyclePlayer.Dispose();
                video.WaitForRelease($"stress-{cycle}");
                Console.WriteLine(
                    $"[engine-smoke] PASS stress-cycle={cycle}/{cycles}");
            }

            video.Complete();
            Console.WriteLine(
                $"[engine-smoke] PASS lifecycle cycles={cycles} " +
                video.Description);
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
        string attachment = Path.Combine(Path.GetTempPath(), $"conic-{Guid.NewGuid():N}.srt");
        VlcCorePlayer? player = null;
        try
        {
            File.WriteAllText(attachment, "1\n00:00:00,000 --> 00:00:01,000\nCapability smoke\n");
            player = new VlcCorePlayer(hardwareDecoding: false, videoOutput: HeadlessVideoOutput);
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
        VlcCorePlayer? player = null;
        try
        {
            player = new VlcCorePlayer(hardwareDecoding: false, videoOutput: HeadlessVideoOutput);
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

    private sealed class DispatchingMediaCapabilitySmokeAdapter(VlcCorePlayer player) : IVLCMediaCapabilitySmokeAdapter
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

    internal static void OpenAndWait(
        VlcCorePlayer player,
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

    internal static void StopAndWait(VlcCorePlayer player, string stage)
    {
        RunAction(stage, player.Stop, TimeSpan.FromSeconds(12));
        WaitForState(
            player,
            stage,
            state => state.Status == Player.Contracts.PlaybackStatus.Stopped,
            ControlStageTimeout);
    }

    internal static void DisposeAndWait(VlcCorePlayer player, string stage) =>
        RunAction(stage, player.Dispose, TimeSpan.FromSeconds(12));

    internal static void RunAction(
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

    internal static void WaitForState(
        VlcCorePlayer player,
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
            $"canSeek={last.CanSeek}, error={last.Error ?? "<none>"}, " +
            $"generation={last.Generation}, source={last.Source ?? "<none>"}.");
    }
}
#endif // SMOKE

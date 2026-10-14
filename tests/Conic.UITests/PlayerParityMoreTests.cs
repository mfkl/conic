using Player.Application;
using Player.Contracts;
using Conic.Services;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

/// <summary>A-B loop, programs, queue search and sort, snapshots, live subtitles, Hide UI and single instance.</summary>
public sealed class PlayerParityMoreTests
{
    [Fact]
    public void ABLoopMarksASetsBAndGoesBackToA()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20);

        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        Assert.Equal("A-B Loop: Set B", vm.ABLoopLabel);
        vm.ApplyEngineState(State(30));
        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        Assert.Equal([.2], transport.Seeks);
        Assert.True(vm.IsABLoopActive);

        vm.ApplyEngineState(State(25));
        Assert.Single(transport.Seeks);
        vm.ApplyEngineState(State(30.1));
        Assert.Equal([.2, .2], transport.Seeks);

        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        Assert.False(vm.IsABLoopActive);
        Assert.Equal("A-B Loop: Set A", vm.ABLoopLabel);
        vm.ApplyEngineState(State(31));
        Assert.Equal(2, transport.Seeks.Count);
    }

    [Fact]
    public void BBeforeASwapsThem()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 40);
        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        vm.ApplyEngineState(State(10));
        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        Assert.Equal([.1], transport.Seeks);
    }

    [Fact]
    public void AnotherItemClearsTheLoop()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20);
        vm.Registry.Execute(PlayerCommandId.ToggleABLoop);
        vm.OpenSource("b.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(30) with { Source = "b.mp4", Generation = 2 });
        Assert.Equal("A-B Loop: Set A", vm.ABLoopLabel);
    }

    [Fact]
    public void ProgramsCycleRoundAndHideWhenThereIsOne()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20);
        vm.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 0)) { Programs = [new("10", "News", false), new("20", "Sports", true)] });

        Assert.True(vm.HasPrograms);
        vm.Registry.Execute(PlayerCommandId.CycleProgram);
        Assert.Equal(["program 10"], transport.Calls);
        Assert.Equal("Program: News", vm.ShortcutFeedbackText(PlayerCommandId.CycleProgram));

        vm.ApplyMediaCapabilities(new(1, 2, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 0)) { Programs = [new("10", "News", true)] });
        Assert.False(vm.HasPrograms);
        Assert.False(vm.Registry.CanExecute(PlayerCommandId.CycleProgram));
    }

    [Fact]
    public void SortByNameIsNaturalAndKeepsWhatPlays()
    {
        OpeningParityTests.QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { });
        vm.ApplyQueueState(new(1, 1, QueueLoadState.Ready,
            [new("t1", "Episode 10.mkv", "e10", 0, false), new("t2", "episode 2.mkv", "e2", 1, true), new("t3", "Episode 1.mkv", "e1", 2, false)], 1, QueueRepeatMode.None, QueueOrder.Normal));

        Assert.Equal([new QueueMove("t3", 0), new QueueMove("t2", 1)], vm.QueueSortMoves());

        // Already sorted, a second press reverses.
        vm.ApplyQueueState(new(1, 2, QueueLoadState.Ready,
            [new("t3", "Episode 1.mkv", "e1", 0, false), new("t2", "episode 2.mkv", "e2", 1, true), new("t1", "Episode 10.mkv", "e10", 2, false)], 1, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.Equal([new QueueMove("t1", 0), new QueueMove("t2", 1)], vm.QueueSortMoves());
    }

    [Fact]
    public void QueueSearchShowsTheMatchingItems()
    {
        OpeningParityTests.QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { });
        vm.ApplyQueueState(new(1, 1, QueueLoadState.Ready,
            [new("t1", "Holiday.mkv", "h", 0, true), new("t2", "Concert.mp4", "c", 1, false), new("t3", "holiday 2.mkv", "h2", 2, false)], 0, QueueRepeatMode.None, QueueOrder.Normal));

        vm.QueueSearch = "HOLI";
        Assert.Equal(["t1", "t3"], vm.VisibleQueueItems.Select(item => item.Token));
        vm.QueueSearch = "zzz";
        Assert.True(vm.HasNoQueueMatch);
        vm.QueueSearch = "";
        Assert.Equal(3, vm.VisibleQueueItems.Count);
    }

    [Fact]
    public void SnapshotAsksTheEngineAndSaysWhere()
    {
        Transport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { SnapshotFolder = "D:/Shots" });
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20, settings);

        vm.Registry.Execute(PlayerCommandId.Snapshot);

        Assert.Equal(["snapshot"], transport.Calls);
        Assert.Equal("Snapshot saved to D:/Shots.", vm.MediaControlsNotice);
    }

    [Fact]
    public void ALiveSubtitleSaysItWasAdded()
    {
        Transport transport = new() { Live = true };
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20);
        vm.OpenDroppedSources(["C:/movie.srt"]);
        Assert.Equal("Added to the playing media.", vm.MediaControlsNotice);
    }

    [Fact]
    public void HideUIAndHelpAskTheWindow()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20);
        List<string> asked = [];
        vm.MinimalInterfaceRequested += (_, _) => asked.Add("hide ui");
        vm.HelpRequested += (_, _) => asked.Add("help");
        vm.Registry.Execute(PlayerCommandId.ToggleMinimalInterface);
        vm.Registry.Execute(PlayerCommandId.ShowHelp);
        Assert.Equal(["hide ui", "help"], asked);
    }

    [Fact]
    public void MinimalInterfaceOverlaysAndHidesTheBarsInAWindow()
    {
        ChromeVisibilityController controller = new();
        controller.SetPlaybackState(isPlaying: true, autoHide: true);
        Assert.False(controller.TryHide(false, false, false, false));
        controller.SetMinimal(true);
        Assert.True(controller.OverlaysVideo);
        Assert.True(controller.TryHide(false, false, false, false));
        controller.SetMinimal(false);
        Assert.True(controller.IsVisible);
        Assert.False(controller.OverlaysVideo);
    }

    [Fact]
    public void TheShortcutEditorLeavesOutCommandsThatNeedAValue()
    {
        Transport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel vm = Playing(transport, elapsed: 20, settings);
        IReadOnlyList<EditablePlayerShortcut> items = new PlayerShortcutEditor(settings, vm.Registry).Items();

        Assert.DoesNotContain(items, item => item.Command is PlayerCommandId.Seek or PlayerCommandId.SetVolume or PlayerCommandId.SelectAudioTrack or PlayerCommandId.QueueMove);
        Assert.Contains(items, item => item.Command == PlayerCommandId.VolumeUp);
        Assert.Contains(items, item => item.Command == PlayerCommandId.CycleAudioTrack);
    }

    [Fact]
    public async Task ASecondStartHandsItsMediaToTheFirst()
    {
        string pipe = "conic-test-" + Guid.NewGuid().ToString("N")[..8];
        TaskCompletionSource<IReadOnlyList<string>> received = new();
        using SingleInstance listener = SingleInstance.Listen(sources => received.TrySetResult(sources), pipe);

        bool sent = false;
        for (int attempt = 0; attempt < 20 && !sent; attempt++) sent = SingleInstance.TrySend(["C:/a.mkv", "https://example.com/b.mp4"], pipe, 200);

        Assert.True(sent);
        Assert.Equal(["C:/a.mkv", "https://example.com/b.mp4"], await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void NoFirstStartMeansThisOneRuns() =>
        Assert.False(SingleInstance.TrySend(["a.mkv"], "conic-test-nobody-" + Guid.NewGuid().ToString("N")[..8], 50));

    private static PlayerPresentationViewModel Playing(Transport transport, double elapsed, PlayerSettingsService? settings = null)
    {
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()));
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(elapsed));
        vm.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 0)));
        return vm;
    }

    private static MediaEngineState State(double elapsed) =>
        new(1, PlaybackStatus.Playing, "a.mp4", elapsed / 100, TimeSpan.FromSeconds(elapsed), TimeSpan.FromSeconds(100), .5, true, null);

    private sealed class Transport : IPlayerTransport, IMediaCapabilitiesTransport, IVideoSnapshotTransport
    {
        public bool Live { get; init; }
        public List<double> Seeks { get; } = [];
        public List<string> Calls { get; } = [];
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) => Seeks.Add(Math.Round(normalizedPosition, 4));
        public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
        public bool RequestSnapshot() { Calls.Add("snapshot"); return true; }
        public MediaCapabilitiesState MediaCapabilities => MediaCapabilitiesState.Initial;
        public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged { add { } remove { } }
        public void SelectTrack(MediaTrackKind kind, string id) { } public void SelectTitle(string id) { } public void SelectChapter(string id) { }
        public void SelectProgram(string id) => Calls.Add($"program {id}");
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) { } public void ResetDelay(MediaTrackKind kind) { }
        public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) => new(true, !Live, false);
    }

    private sealed class NoPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class SettingsStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }
}

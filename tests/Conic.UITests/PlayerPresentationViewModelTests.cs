using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

public sealed class PlayerPresentationViewModelTests
{
    [Fact]
    public void EnginePollingDoesNotOverwriteAnActiveSeekDrag()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel viewModel = CreateViewModel(transport);
        viewModel.ApplyEngineState(State(0.25, PlaybackStatus.Playing, canSeek: true));
        viewModel.BeginSeek();

        viewModel.Position = 0.75;
        viewModel.ApplyEngineState(State(0.30, PlaybackStatus.Playing, canSeek: true));

        Assert.Equal(0.75, viewModel.Position);
        Assert.Empty(transport.Seeks);

        viewModel.CommitSeek();

        Assert.Equal(new[] { 0.75 }, transport.Seeks);
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, false)]
    [InlineData(PlaybackStatus.Stopped, false)]
    public void EngineStateControlsThePlayPauseGlyphState(
        PlaybackStatus status,
        bool expectedIsPlaying)
    {
        PlayerPresentationViewModel viewModel = CreateViewModel(new RecordingTransport());

        viewModel.ApplyEngineState(State(0, status, canSeek: false));

        Assert.Equal(expectedIsPlaying, viewModel.IsPlaying);
    }

    [Fact]
    public void TransportCommandsReflectEngineStateAndDispatchSharedOperations()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel viewModel = CreateViewModel(transport);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, canSeek: true) with
        {
            IsMuted = true, PlaybackRate = 1.5, Title = "movie"
        });

        viewModel.Registry.Execute(PlayerCommandId.SkipBackward);
        viewModel.Registry.Execute(PlayerCommandId.SkipForward);
        viewModel.Registry.Execute(PlayerCommandId.ToggleMute);
        viewModel.Registry.Execute(PlayerCommandId.SetPlaybackRate, 100d);

        Assert.Equal(new[] { TimeSpan.FromSeconds(-10), TimeSpan.FromSeconds(10) }, transport.Skips);
        Assert.False(transport.Muted);
        Assert.Equal(31.25, transport.Rate);
        Assert.Equal("movie", viewModel.MediaTitle);
    }

    [Fact]
    public void LiveSkipSettingsUpdateCommandsLabelsAndNotifications()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel viewModel = new(transport, new EmptyPicker(), () => { }, settings: settings);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true));
        List<string?> changed = []; viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        settings.Update(s => s with { SkipBackwardSeconds = 4, SkipForwardSeconds = 17 });
        viewModel.Registry.Execute(PlayerCommandId.SkipBackward);
        viewModel.Registry.Execute(PlayerCommandId.SkipForward);
        Assert.Equal([TimeSpan.FromSeconds(-4), TimeSpan.FromSeconds(17)], transport.Skips);
        Assert.Equal("−4", viewModel.SkipBackwardLabel); Assert.Equal("+17", viewModel.SkipForwardLabel);
        Assert.Contains(nameof(PlayerPresentationViewModel.SkipBackwardLabel), changed);
    }

    [Fact]
    public void DirectPlayingEstablishesGenerationAndIgnoresOlderSameSource()
    {
        RecordingTransport transport = new(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.05, PlaybackStatus.Playing, true) with { Generation = 2, Source = "a.mp4" });
        vm.ApplyEngineState(State(.8, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Equal("0:05", vm.ElapsedText);
    }

    [Fact]
    public void DifferentSourceDoesNotEstablishGeneration()
    {
        RecordingTransport transport = new(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.05, PlaybackStatus.Playing, true) with { Generation = 2, Source = "b.mp4" });
        vm.ApplyEngineState(State(.06, PlaybackStatus.Playing, true) with { Generation = 3, Source = "a.mp4" });
        Assert.Equal("0:06", vm.ElapsedText);
    }

    [Fact]
    public void ResumeAcceptUsesCurrentDurationOnce()
    {
        RecordingTransport transport = new(); HistoryStorage storage = new(); MediaHistoryService history = new(storage);
        MediaSource source = new("a.mp4", MediaSourceKind.File, "a"); history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20);
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.1, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4", Duration = TimeSpan.FromSeconds(200) });
        vm.Registry.Execute(PlayerCommandId.AcceptResume); vm.Registry.Execute(PlayerCommandId.AcceptResume);
        Assert.Single(transport.Seeks); Assert.Equal(.2, transport.Seeks[0]); Assert.Null(history.FindResume(source));
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, 1)]
    [InlineData(PlaybackStatus.Paused, 1)]
    [InlineData(PlaybackStatus.Opening, 0)]
    [InlineData(PlaybackStatus.Stopped, 0)]
    [InlineData(PlaybackStatus.Faulted, 0)]
    public void RecentIsRecordedOnlyForUsableMatchingStates(PlaybackStatus status, int expected)
    {
        RecordingTransport transport = new(); HistoryStorage storage = new(); MediaHistoryService history = new(storage);
        PlayerPresentationViewModel vm = CreateOpeningViewModel(transport, history); vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.2, status, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Equal(expected, history.Recent.Count);
    }

    [Fact]
    public void MismatchedSourceNeverRecordsRecent()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "b.mp4" });
        Assert.Empty(history.Recent);
    }

    [Fact]
    public void ResumeIsHiddenUntilMatchingUsableStateAndDeclineDoesNotSeek()
    {
        RecordingTransport transport = new(); HistoryStorage storage = new(); MediaHistoryService history = SeedHistory(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport, history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); Assert.False(vm.HasPendingResume);
        vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); Assert.True(vm.HasPendingResume);
        vm.Registry.Execute(PlayerCommandId.DeclineResume); Assert.Empty(transport.Seeks); Assert.Null(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
    }

    [Theory]
    [InlineData(false, true, 40, 100, false)]
    [InlineData(true, false, 40, 100, false)]
    [InlineData(true, true, 29, 100, false)]
    [InlineData(true, true, 80, 100, false)]
    [InlineData(true, true, 30, 100, true)]
    [InlineData(true, true, 79, 100, true)]
    public void ResumeEligibilityHonorsPreferencesAndThresholdBoundaries(bool resumePlayback, bool rememberHistory, int position, int duration, bool expected)
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a");
        history.Checkpoint(source, TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), true, 0, 0);
        PlayerSettingsService settings = new(new SettingsStorage()); settings.Update(s => s with { ResumePlayback = resumePlayback, RememberResumeHistory = rememberHistory, ResumeMinimumElapsedSeconds = 30, ResumeMaximumRemainingSeconds = 20 });
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.1, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Equal(expected, vm.HasPendingResume);
    }

    private static PlayerPresentationViewModel CreateOpeningViewModel(RecordingTransport transport, MediaHistoryService? history = null) => new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
    private static MediaHistoryService SeedHistory()
    {
        MediaHistoryService history = new(new HistoryStorage()); history.Checkpoint(new("a.mp4", MediaSourceKind.File, "a"), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); return history;
    }


    private static PlayerPresentationViewModel CreateViewModel(
        RecordingTransport transport) => new(transport, new EmptyPicker(), () => { });

    private static MediaEngineState State(
        double position,
        PlaybackStatus status,
        bool canSeek) => new(
        1,
        status,
        "test.mp4",
        position,
        TimeSpan.FromSeconds(position * 100),
        TimeSpan.FromSeconds(100),
        0.5,
        canSeek,
        null);

    private sealed class RecordingTransport : IPlayerTransport
    {
        public List<double> Seeks { get; } = new();
        public List<TimeSpan> Skips { get; } = new();
        public bool Muted { get; private set; }
        public double Rate { get; private set; }
        public void Open(string source) { }
        public void TogglePause() { }
        public void Stop() { }
        public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) => Muted = muted;
        public void Skip(TimeSpan offset) => Skips.Add(offset);
        public void SetPlaybackRate(double playbackRate) => Rate = playbackRate;
    }

    private sealed class EmptyPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SettingsStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }
    private sealed class ExistingFiles : IMediaFileSystem { public bool FileExists(string path) => true; public bool DirectoryExists(string path) => false; public string GetFullPath(string path) => path; }
    private sealed class HistoryStorage : IMediaHistoryStorage { public string? Content; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; }
}

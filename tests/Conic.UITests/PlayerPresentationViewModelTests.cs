using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

public sealed class PlayerPresentationViewModelTests
{
    [Fact]
    public void StopThenPlayReopensFromTheBeginningAndAcceptsTheNewGenerationWithoutResumePrompt()
    {
        RecordingTransport transport = new();
        var vm = CreateOpeningViewModel(transport, SeedHistory());
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        var playing = State(.4, PlaybackStatus.Playing, true) with { Source = "a.mp4" };
        vm.ApplyEngineState(playing);
        vm.StopCommand.Execute(null);
        vm.ApplyEngineState(playing with { Status = PlaybackStatus.Stopped });
        vm.TogglePauseCommand.Execute(null);
        Assert.Equal(new[] { "a.mp4", "a.mp4" }, transport.Opened);
        Assert.Equal(PlaybackStatus.Opening, vm.PlaybackStatus);
        Assert.False(vm.TogglePauseCommand.CanExecute(null));
        vm.ApplyEngineState(playing with { Generation = 2, Position = 0, Elapsed = TimeSpan.Zero });
        Assert.True(vm.IsPlaying);
        Assert.Equal(0, vm.Position);
        Assert.False(vm.HasPendingResume);
    }

    [Fact]
    public void StopThenPlayUsesTheSelectedQueueFileAndDecodesItsFileUri()
    {
        QueueTransport transport = new();
        var vm = CreateOpeningViewModel(transport);
        string selected = Path.GetFullPath("second movie.mp4");
        vm.OpenSource("first.mp4", MediaSourceKind.File);
        var playing = State(.3, PlaybackStatus.Playing, true) with { Source = "first.mp4" };
        vm.ApplyEngineState(playing);
        transport.Publish(new(1, 1, QueueLoadState.Ready,
            [new("one", "First", "first.mp4", 0, false), new("two", "Second", new Uri(selected).AbsoluteUri, 1, true)],
            1, QueueRepeatMode.None, QueueOrder.Normal));
        vm.StopCommand.Execute(null);
        transport.Publish(new(1, 2, QueueLoadState.Unavailable, [], -1, QueueRepeatMode.None, QueueOrder.Normal));
        vm.ApplyEngineState(playing with { Status = PlaybackStatus.Stopped });
        vm.TogglePauseCommand.Execute(null);
        Assert.Equal(selected, Assert.Single(transport.OpenedQueues[1]));
    }

    [Fact]
    public void PlayAtEndOfMediaRestartsTheSelectedItemInTheExistingQueue()
    {
        QueueTransport transport = new();
        var vm = CreateOpeningViewModel(transport);
        vm.ApplyEngineState(State(1, PlaybackStatus.Stopped, false));
        transport.Publish(new(1, 1, QueueLoadState.Ready,
            [new("one", "First", "first.mp4", 0, false), new("two", "Second", "second.mp4", 1, true)],
            1, QueueRepeatMode.None, QueueOrder.Normal));
        vm.TogglePauseCommand.Execute(null);
        Assert.Equal("two", transport.Selected);
        Assert.Empty(transport.OpenedQueues);
    }

    [Fact]
    public void QueueInputChangeClearsInformationAndRejectsLateInformationFromThePreviousInput()
    {
        CapabilityTransport transport = new();
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { });
        var old = new MediaInformationState(1, 1, MediaInformationLoadState.Ready, "first", MediaInformationState.Unavailable.Metadata,
            CodecStreams: [new MediaCodecStream(0, new Dictionary<string, string> { ["Codec"] = "H264" })]);
        transport.PublishInformation(old);
        Assert.True(vm.MediaInformation.HasStreams);
        transport.Publish(MediaCapabilitiesState.Initial with { QueueGeneration = 1, MediaGeneration = 2 });
        Assert.False(vm.MediaInformation.HasStreams);
        Assert.Equal(MediaInformationLoadState.Stale, vm.MediaInformation.Status);
        transport.PublishInformation(old);
        Assert.False(vm.MediaInformation.HasStreams);
        transport.PublishInformation(old with { MediaGeneration = 2, Name = "second" });
        Assert.Equal("second", vm.MediaInformation.Name);
    }

    [Fact]
    public void SwitchingInformationTabNotifiesItsVisibilityBinding()
    {
        var vm = CreateViewModel(new RecordingTransport());
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.DrawerTab = "Information";
        Assert.True(vm.IsInformationTab);
        Assert.Contains(nameof(vm.IsInformationTab), changed);
        changed.Clear();
        vm.DrawerTab = "Queue";
        Assert.False(vm.IsInformationTab);
        Assert.Contains(nameof(vm.IsInformationTab), changed);
    }

    [Fact]
    public async Task CopyDiagnosticsWaitsForClipboardCompletionAndSurfacesResult()
    {
        Clipboard clipboard = new();
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, clipboard: clipboard);
        vm.CopyDiagnosticsCommand.Execute(null);
        Assert.Equal(string.Empty, vm.CopyStatus);
        clipboard.Completion.SetResult(null);
        await clipboard.Called.Task;
        await Task.Delay(10);
        Assert.Equal("Diagnostics copied.", vm.CopyStatus);
        clipboard = new(); vm = new PlayerPresentationViewModel(new RecordingTransport(), new EmptyPicker(), () => { }, clipboard: clipboard);
        vm.CopyDiagnosticsCommand.Execute(null); clipboard.Completion.SetResult("denied"); await clipboard.Called.Task; await Task.Delay(10);
        Assert.Contains("denied", vm.CopyStatus);
    }
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
        Assert.Equal(expectedIsPlaying ? "Pause" : "Play", viewModel.PlayPauseName);
    }

    [Theory]
    [InlineData(PlaybackStatus.Closed, false)]
    [InlineData(PlaybackStatus.Opening, false)]
    [InlineData(PlaybackStatus.Faulted, false)]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, true)]
    [InlineData(PlaybackStatus.Stopped, true)]
    public void PlayPauseAvailabilityFollowsEffectivePlaybackState(PlaybackStatus status, bool expected)
    {
        PlayerPresentationViewModel viewModel = CreateViewModel(new RecordingTransport());
        viewModel.ApplyEngineState(State(0, status, false));
        Assert.Equal(expected, viewModel.TogglePauseCommand.CanExecute(null));
    }

    [Fact]
    public void VolumeClampsToTwoHundredPercentAndMarksAmplification()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel viewModel = CreateViewModel(transport);
        viewModel.ApplyEngineState(State(0, PlaybackStatus.Playing, false));
        viewModel.Volume = 150;
        Assert.True(viewModel.IsVolumeAmplified);
        Assert.Contains("Amplified", viewModel.VolumeText);
        viewModel.Volume = 250;
        viewModel.Volume = -10;
        Assert.Equal(new[] { 1.5, 2, 0 }, transport.Volumes);
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

    [Fact]
    public void CheckpointThrottleAndForcedLifecycleBoundaries()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch; HistoryStorage storage = new(); MediaHistoryService history = new(storage);
        RecordingTransport transport = new(); PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, clock: () => now);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        string first = storage.Content!; vm.ApplyEngineState(State(.41, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); Assert.Equal(first, storage.Content);
        now += TimeSpan.FromSeconds(10); vm.ApplyEngineState(State(.42, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); Assert.NotEqual(first, storage.Content);
        vm.Registry.Execute(PlayerCommandId.Stop); Assert.True(transport.Stopped); vm.OnClosing();
    }

    [Theory]
    [InlineData(PlaybackStatus.Opening, true)]
    [InlineData(PlaybackStatus.Stopped, true)]
    [InlineData(PlaybackStatus.Faulted, true)]
    [InlineData(PlaybackStatus.Playing, false)]
    public void ForcedCheckpointRejectsIneligibleStates(PlaybackStatus status, bool seekable)
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, status, seekable) with { Generation = 1, Source = "a.mp4" }); vm.CheckpointCurrent(true);
        Assert.Empty(history.Resume);
    }

    [Fact]
    public void SourceReplacementFlushesPriorPositionOnly()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        vm.OpenSource("b.mp4", MediaSourceKind.File);
        Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a"))); Assert.Null(history.FindResume(new("b.mp4", MediaSourceKind.File, "b")));
    }

    [Fact]
    public void StopAndClosingFlushActivePosition()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); RecordingTransport transport = new(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport, history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); vm.Registry.Execute(PlayerCommandId.Stop);
        Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a"))); Assert.True(transport.Stopped);
        vm.OnClosing(); Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
    }

    [Fact]
    public void OldGenerationAfterReplacementCannotCheckpointNewSource()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); vm.OpenSource("b.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.8, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); vm.CheckpointCurrent(true);
        Assert.Null(history.FindResume(new("b.mp4", MediaSourceKind.File, "b")));
    }

    [Fact]
    public void NearEndPollAndForceRemoveResume()
    {
        HistoryStorage storage = new(); MediaHistoryService history = SeedHistory(); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.9, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Null(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
        history.Checkpoint(new("a.mp4", MediaSourceKind.File, "a"), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); vm.CheckpointCurrent(true);
        Assert.Null(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
    }

    [Fact]
    public void ClosingFreshEligiblePlayerPersistsExactElapsedPosition()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Opening, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Empty(history.Resume); vm.ApplyEngineState(State(.45, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); history.ClearResume();
        vm.OnClosing(); ResumeEntry entry = Assert.Single(history.Resume); Assert.Equal(TimeSpan.FromSeconds(45), entry.Position);
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, true, 0d, 100d, true)]
    [InlineData(PlaybackStatus.Playing, true, .1d, 100d, true)]
    [InlineData(PlaybackStatus.Playing, true, .4d, 100d, false)]
    public void ForcedCheckpointRejectsDurationThresholdAndPrivacy(PlaybackStatus status, bool seekable, double position, double duration, bool rememberHistory)
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerSettingsService settings = new(new SettingsStorage()); settings.Update(s => s with { RememberResumeHistory = rememberHistory, ResumeMinimumElapsedSeconds = 30 });
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(new(1, status, "a.mp4", position, TimeSpan.FromSeconds(position * 100), TimeSpan.FromSeconds(duration), .5, seekable, null)); vm.CheckpointCurrent(true);
        Assert.Empty(history.Resume);
    }

    [Fact]
    public void StopWritesHistoryBeforeTransportStop()
    {
        List<string> events = []; OrderedStorage storage = new(events); MediaHistoryService history = new(storage); OrderedTransport transport = new(events); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport, history);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); vm.Registry.Execute(PlayerCommandId.Stop);
        Assert.True(events.IndexOf("write") < events.IndexOf("stop"));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void ResumeRemovalFailureDoesNotRepeatSeek(string kind)
    {
        Exception error = kind == "io" ? new IOException("remove detail") : new UnauthorizedAccessException("remove detail"); SwitchStorage storage = new(error); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a");
        storage.Error = null; history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); storage.Error = error; RecordingTransport transport = new(); List<string> diagnostic = [];
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, diagnostic: diagnostic.Add); vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        vm.Registry.Execute(PlayerCommandId.AcceptResume); vm.Registry.Execute(PlayerCommandId.AcceptResume);
        Assert.Empty(transport.Seeks); Assert.Equal("Media history could not be saved.", vm.HistoryError); Assert.Contains(diagnostic, x => x.Contains("remove detail"));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void CheckpointFailureRecoversWithoutThrottleDelay(string kind)
    {
        Exception error = kind == "io" ? new IOException("checkpoint detail") : new UnauthorizedAccessException("checkpoint detail"); DateTimeOffset now = DateTimeOffset.UnixEpoch; SwitchStorage storage = new(error); MediaHistoryService history = new(storage); List<string> diagnostics = [];
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, clock: () => now, diagnostic: diagnostics.Add);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); Assert.NotNull(vm.HistoryError);
        storage.Error = null; vm.CheckpointCurrent(false); Assert.Null(vm.HistoryError); Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a"))); Assert.Contains(diagnostics, x => x.Contains("checkpoint detail"));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void DeclineFailureRemainsActionableUntilRecovery(string kind)
    {
        Exception error = kind == "io" ? new IOException("decline detail") : new UnauthorizedAccessException("decline detail"); SwitchStorage storage = new(); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a"); history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); storage.Error = error; List<string> diagnostic = [];
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, diagnostic: diagnostic.Add); vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); vm.Registry.Execute(PlayerCommandId.DeclineResume);
        Assert.True(vm.HasPendingResume); Assert.NotNull(vm.HistoryError); Assert.Contains(diagnostic, x => x.Contains("decline detail")); storage.Error = null; vm.Registry.Execute(PlayerCommandId.DeclineResume);
        Assert.False(vm.HasPendingResume); Assert.Null(vm.HistoryError); Assert.Null(history.FindResume(source));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void RecentRecordingFailureRecoversOnLaterMatchingState(string kind)
    {
        Exception error = kind == "io" ? new IOException("recent detail") : new UnauthorizedAccessException("recent detail"); SwitchStorage storage = new(error); MediaHistoryService history = new(storage); List<string> diagnostic = [];
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, diagnostic: diagnostic.Add); vm.OpenSource("a.mp4", MediaSourceKind.File); MediaEngineState state = State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }; vm.ApplyEngineState(state);
        Assert.NotNull(vm.HistoryError); Assert.Contains(diagnostic, x => x.Contains("recent detail")); storage.Error = null; vm.ApplyEngineState(state);
        Assert.Null(vm.HistoryError); Assert.Single(history.Recent);
    }

    [Fact]
    public void UrlPanelAndSubmitCommandsAreSeparate()
    {
        int shown = 0; RecordingTransport transport = new(); PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), showUrlPanel: () => shown++);
        vm.Registry.Execute(PlayerCommandId.OpenUrl); Assert.Equal(1, shown); Assert.Empty(transport.Opened); vm.UrlText = "https://example.com/a"; vm.Registry.Execute(PlayerCommandId.SubmitUrl); Assert.Equal("https://example.com/a", Assert.Single(transport.Opened));
    }

    [Fact]
    public void FolderIsCapabilityGatedWithoutQueueAndSingleDropStillOpens()
    {
        RecordingTransport transport = new(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport); Assert.False(vm.OpenFolderCommand.CanExecute(null)); vm.OpenDroppedSources([]); Assert.Contains("one local", vm.OpenError);
        vm.OpenDroppedSources(["a.mp4", "b.mp4"]); Assert.Empty(transport.Opened); vm.OpenDroppedSources(["a.mp4"]); Assert.Single(transport.Opened);
    }

    [Fact]
    public void RecentAndResumeClearCommandsRemainIndependent()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); MediaSource a = new("a.mp4", MediaSourceKind.File, "a"); MediaSource b = new("b.mp4", MediaSourceKind.File, "b"); history.RecordOpened(a, true); history.RecordOpened(b, true); history.Checkpoint(a, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20);
        PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history); vm.Registry.Execute(PlayerCommandId.RemoveRecent, "a.mp4"); Assert.Single(history.Recent); Assert.NotNull(history.FindResume(a)); vm.Registry.Execute(PlayerCommandId.ClearRecent); Assert.Empty(history.Recent); Assert.NotNull(history.FindResume(a)); vm.Registry.Execute(PlayerCommandId.ClearResumeHistory); Assert.Empty(history.Resume);
    }

    [Fact]
    public void MatchingSuccessRecordsRecentAndNotifies()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history); List<string?> changed = []; vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.True(vm.HasRecentItems); Assert.Contains(nameof(PlayerPresentationViewModel.RecentItems), changed); Assert.Contains(nameof(PlayerPresentationViewModel.HasRecentItems), changed); Assert.True(vm.ClearRecentCommand.CanExecute(null));
    }

    [Fact]
    public void UnavailableRecentIsRemovedWithSafeErrorAndNotification()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); history.RecordOpened(new("gone.mp4", MediaSourceKind.File, "gone"), true); List<string> diagnostics = []; PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new MissingFiles()), history: history, diagnostic: diagnostics.Add); List<string?> changed = []; vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(vm.ClearRecentCommand.CanExecute(null)); vm.Registry.Execute(PlayerCommandId.OpenRecent, "gone.mp4"); Assert.Empty(history.Recent); Assert.False(vm.ClearRecentCommand.CanExecute(null)); Assert.NotEmpty(vm.OpenError); Assert.NotEmpty(diagnostics); Assert.Contains(nameof(PlayerPresentationViewModel.RecentItems), changed); Assert.Contains(nameof(PlayerPresentationViewModel.HasRecentItems), changed);
    }

    [Fact]
    public void DisablingRecentPrivacyClearsImmediatelyAndStaysClear()
    {
        HistoryStorage storage = new(); MediaHistoryService history = new(storage); history.RecordOpened(new("a.mp4", MediaSourceKind.File, "a"), true); PlayerSettingsService settings = new(new SettingsStorage()); PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
        settings.Update(x => x with { RememberRecentMedia = false }); Assert.False(vm.HasRecentItems); Assert.False(vm.ClearRecentCommand.CanExecute(null)); settings.Update(x => x with { SkipForwardSeconds = 11 }); Assert.Empty(history.Recent);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void ClearRecentFailurePreservesStateUntilRecovery(string kind)
    {
        Exception error = kind == "io" ? new IOException("clear detail") : new UnauthorizedAccessException("clear detail"); SwitchStorage storage = new(); MediaHistoryService history = new(storage); history.RecordOpened(new("a.mp4", MediaSourceKind.File, "a"), true); storage.Error = error; List<string> diagnostic = [];
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, diagnostic: diagnostic.Add); vm.Registry.Execute(PlayerCommandId.ClearRecent);
        Assert.True(vm.HasRecentItems); Assert.NotNull(vm.HistoryError); Assert.Contains(diagnostic, x => x.Contains("clear detail")); storage.Error = null; vm.Registry.Execute(PlayerCommandId.ClearRecent); Assert.False(vm.HasRecentItems); Assert.Null(vm.HistoryError);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void RemoveRecentFailurePreservesItemUntilRecovery(string kind)
    {
        Exception error = kind == "io" ? new IOException("remove recent") : new UnauthorizedAccessException("remove recent"); SwitchStorage storage = new(); MediaHistoryService history = new(storage); history.RecordOpened(new("a.mp4", MediaSourceKind.File, "a"), true); storage.Error = error; PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
        vm.Registry.Execute(PlayerCommandId.RemoveRecent, "a.mp4"); Assert.True(vm.HasRecentItems); Assert.NotNull(vm.HistoryError); storage.Error = null; vm.Registry.Execute(PlayerCommandId.RemoveRecent, "a.mp4"); Assert.False(vm.HasRecentItems); Assert.Null(vm.HistoryError);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void ClearResumeFailureKeepsVisiblePromptUntilRecovery(string kind)
    {
        Exception error = kind == "io" ? new IOException("clear resume") : new UnauthorizedAccessException("clear resume"); SwitchStorage storage = new(); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a"); history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); storage.Error = error; PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history); vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        vm.Registry.Execute(PlayerCommandId.ClearResumeHistory); Assert.True(vm.HasPendingResume); Assert.NotNull(history.FindResume(source)); storage.Error = null; vm.Registry.Execute(PlayerCommandId.ClearResumeHistory); Assert.False(vm.HasPendingResume); Assert.Empty(history.Resume); Assert.Null(vm.HistoryError);
    }

    [Fact]
    public void ResumePrivacyHidesPromptEvenWhenClearFails()
    {
        SwitchStorage storage = new(); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a"); history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); PlayerSettingsService settings = new(new SettingsStorage()); PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history); vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); storage.Error = new IOException("privacy clear");
        settings.Update(x => x with { RememberResumeHistory = false }); Assert.False(vm.HasPendingResume); Assert.NotNull(vm.HistoryError); storage.Error = null; settings.Update(x => x with { SkipForwardSeconds = 11 }); Assert.Empty(history.Resume); Assert.Null(vm.HistoryError);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public void HistoryFailuresAreSafeAndDiagnostic(string failure)
    {
        Exception error = failure == "io" ? new IOException("disk detail") : new UnauthorizedAccessException("denied detail"); ThrowingHistoryStorage storage = new(error); MediaHistoryService history = new(storage); List<string> diagnostics = [];
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, diagnostic: diagnostics.Add);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Equal("Media history could not be saved.", vm.HistoryError); Assert.Contains(diagnostics, x => x.Contains("detail"));
    }

    [Fact]
    public void ResumeHistoryStateNotifiesAndUpdatesClearCommand()
    {
        MediaHistoryService history = new(new HistoryStorage());
        PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        List<string?> changed = [];
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });

        Assert.True(vm.HasResumeItems);
        Assert.True(vm.ClearResumeHistoryCommand.CanExecute(null));
        Assert.Contains(nameof(PlayerPresentationViewModel.HasResumeItems), changed);
        vm.Registry.Execute(PlayerCommandId.ClearResumeHistory);
        Assert.False(vm.HasResumeItems);
        Assert.False(vm.ClearResumeHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void HistoryErrorNotifiesPresenceAndRecovery()
    {
        SwitchStorage storage = new() { Error = new IOException("write detail") };
        MediaHistoryService history = new(storage);
        PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        List<string?> changed = [];
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.True(vm.HasHistoryError);
        Assert.Contains(nameof(PlayerPresentationViewModel.HasHistoryError), changed);

        storage.Error = null;
        vm.CheckpointCurrent(true);
        Assert.False(vm.HasHistoryError);
    }

    [Fact]
    public void QueueSnapshotsRejectStaleGenerationsAndCommandsUseCurrentTokens()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { });
        transport.Publish(new(4, 2, QueueLoadState.Ready, [new("live", "Live", "live.mp4", 0, true), new("next", "Next", "next.mp4", 1, false)], 0, QueueRepeatMode.None, QueueOrder.Normal));
        transport.Publish(new(3, 99, QueueLoadState.Empty, [], -1, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.Equal(2, vm.QueueItems.Count); Assert.True(vm.NextCommand.CanExecute(null)); Assert.False(vm.PreviousCommand.CanExecute(null));
        vm.Registry.Execute(PlayerCommandId.QueueSelect, "next");
        vm.Registry.Execute(PlayerCommandId.QueueRepeat);
        Assert.Equal("next", transport.Selected); Assert.Equal(QueueRepeatMode.CurrentItem, transport.Repeat);
    }

    [Fact]
    public void QueueDrawerStatesExposeLoadingEmptyFailureAndReplayObservedModes()
    {
        QueueTransport transport = new(); PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { });
        transport.Publish(new(6, 1, QueueLoadState.Loading, [], -1, QueueRepeatMode.None, QueueOrder.Normal)); Assert.True(vm.IsQueueLoading);
        transport.Publish(new(6, 2, QueueLoadState.Empty, [], -1, QueueRepeatMode.None, QueueOrder.Normal)); Assert.True(vm.HasEmptyQueue);
        transport.Publish(new(6, 3, QueueLoadState.Failed, [], -1, QueueRepeatMode.None, QueueOrder.Normal, "Only 2 of 3 items were queued.")); Assert.True(vm.HasQueueError); Assert.Contains("2 of 3", vm.QueueError);
        transport.Publish(new(7, 1, QueueLoadState.Ready, [new("a", "A", "a", 0, true)], 0, QueueRepeatMode.All, QueueOrder.Random)); Assert.Equal("Repeat all", vm.RepeatLabel); Assert.Equal("Shuffle on", vm.ShuffleLabel);
    }

    [Fact]
    public void MediaCapabilitiesRejectStaleGenerationsAndOnlyReadbackChangesDelay()
    {
        CapabilityTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { });
        transport.Publish(new(3, 2, MediaCapabilityLoadState.Ready, [new("1", "English", false, true)], [], [], [], new(0, 0)));
        transport.Publish(new(2, 99, MediaCapabilityLoadState.Empty, [], [], [], [], new(0, 0)));
        Assert.Single(vm.AudioTracks);
        vm.Registry.Execute(PlayerCommandId.AdjustAudioDelay, 100);
        Assert.Equal(100, transport.LastAudioDelay);
        Assert.Equal(0, vm.AudioDelayMilliseconds);
        transport.Publish(new(3, 2, MediaCapabilityLoadState.Ready, [], [], [], [], new(100, 0)));
        Assert.Equal(100, vm.AudioDelayMilliseconds);
    }

    [Fact]
    public async Task ExternalSubtitleCancellationAndPendingLanguageAreHonest()
    {
        CapabilityTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new ExternalPicker(null), () => { });
        vm.Registry.Execute(PlayerCommandId.AttachExternalSubtitle);
        await Task.Yield();
        Assert.False(vm.HasMediaControlsNotice);
        vm = new PlayerPresentationViewModel(transport, new ExternalPicker("caption.srt"), () => { });
        vm.Registry.Execute(PlayerCommandId.AttachExternalSubtitle);
        await Task.Yield();
        Assert.Contains("next media open", vm.MediaControlsNotice);
        Assert.Contains("not a current live track", vm.MediaControlsNotice);
    }

    [Fact]
    public void OpeningAFileUsesReplaceQueueFlowWhenQueueIsAvailable()
    {
        QueueTransport transport = new(); PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));
        vm.OpenSource("movie.mp4", MediaSourceKind.File);
        Assert.Equal(["movie.mp4"], Assert.Single(transport.OpenedQueues));
    }

    [Fact]
    public async Task LoadPlaylistUsesReplaceQueueAndPickerCancellationLeavesTheQueueUntouched()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"conic-playlist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string playlist = Path.Combine(directory, "items.m3u");
            await File.WriteAllTextAsync(playlist, "#EXTM3U\nrelative-video.mp4\nhttps://example.test/video.mp4\n");
            QueueTransport transport = new();
            PlayerPresentationViewModel vm = new(transport, new PlaylistPicker(playlist), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

            vm.Registry.Execute(PlayerCommandId.LoadPlaylist);
            await WaitForAsync(() => transport.OpenedQueues.Count == 1);

            Assert.Equal([Path.Combine(directory, "relative-video.mp4"), "https://example.test/video.mp4"], Assert.Single(transport.OpenedQueues));
            PlayerPresentationViewModel cancelled = new(transport, new PlaylistPicker(null), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));
            cancelled.Registry.Execute(PlayerCommandId.LoadPlaylist);
            await Task.Yield();
            Assert.Single(transport.OpenedQueues);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SavePlaylistWritesBoundedM3uFromQueueItems()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"conic-playlist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string playlist = Path.Combine(directory, "saved.m3u8");
            QueueTransport transport = new();
            PlayerPresentationViewModel vm = new(transport, new PlaylistPicker(null, playlist), () => { });
            transport.Publish(new(1, 1, QueueLoadState.Ready, [new("one", "One", "first.mp4", 0, true), new("two", "Two", "https://example.test/second.mp4", 1, false)], 0, QueueRepeatMode.None, QueueOrder.Normal));

            vm.Registry.Execute(PlayerCommandId.SavePlaylist);
            string content = await WaitForReadableTextAsync(playlist);

            Assert.Equal(["first.mp4", "https://example.test/second.mp4"], M3uPlaylistDocument.Parse(content));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "The asynchronous playlist operation did not complete.");
    }

    private static async Task<string> WaitForReadableTextAsync(string path)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try { return await File.ReadAllTextAsync(path); }
            catch (IOException) { await Task.Delay(10); }
        }
        return await File.ReadAllTextAsync(path);
    }

    [Fact]
    public void LibraryCommandsExposeExplicitFavoritesAndClearEveryStoredSection()
    {
        HistoryStorage storage = new();
        MediaHistoryService history = new(storage);
        PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.Registry.Execute(PlayerCommandId.AddFavorite);
        history.RecordOpened(new("b.mp4", MediaSourceKind.File, "b"), true);
        history.Checkpoint(new("b.mp4", MediaSourceKind.File, "b"), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20);

        Assert.True(vm.HasFavoriteItems);
        Assert.True(vm.HasLibraryItems);
        Assert.True(vm.IsCurrentFavorite);
        Assert.True(vm.ClearLibraryCommand.CanExecute(null));
        Assert.True(vm.RemoveResumeCommand.CanExecute("b.mp4"));
        Assert.False(vm.RemoveResumeCommand.CanExecute(null));
        vm.Registry.Execute(PlayerCommandId.RemoveResume, "b.mp4");
        Assert.False(vm.HasResumeItems);
        vm.Registry.Execute(PlayerCommandId.ClearLibrary);

        Assert.False(vm.HasLibraryItems);
        Assert.False(vm.IsCurrentFavorite);
        Assert.Empty(history.Recent);
        Assert.Empty(history.Resume);
        Assert.Empty(history.Favorites);
    }

    [Fact]
    public void ClosingAndReopenBoundaryPersistsPositionAndDoesNotReuseItAsLiveState()
    {
        HistoryStorage storage = new();
        MediaHistoryService history = new(storage);
        PlayerPresentationViewModel vm = CreateOpeningViewModel(new RecordingTransport(), history);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });

        vm.OnClosing(); // The deterministic close/reopen boundary; this is not an OS suspend simulation.
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.1, PlaybackStatus.Opening, true) with { Generation = 2, Source = "a.mp4" });

        Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
        Assert.False(vm.HasPendingResume);
        vm.ApplyEngineState(State(.1, PlaybackStatus.Playing, true) with { Generation = 2, Source = "a.mp4" });
        Assert.True(vm.HasPendingResume);
    }

    [Fact]
    public void LibraryAndPlaybackDrawersAreMutuallyExclusive()
    {
        PlayerPresentationViewModel vm = CreateViewModel(new RecordingTransport());

        vm.IsQueueOpen = true;
        vm.IsLibraryOpen = true;
        Assert.True(vm.IsLibraryOpen);
        Assert.False(vm.IsQueueOpen);

        vm.IsQueueOpen = true;
        Assert.True(vm.IsQueueOpen);
        Assert.False(vm.IsLibraryOpen);
    }

    private static PlayerPresentationViewModel CreateOpeningViewModel(IPlayerTransport transport, MediaHistoryService? history = null) => new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);
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
        public List<string> Opened { get; } = new();
        public bool Muted { get; private set; }
        public double Rate { get; private set; }
        public List<double> Volumes { get; } = new();
        public void Open(string source) => Opened.Add(source);
        public void TogglePause() { }
        public bool Stopped { get; private set; }
        public void Stop() => Stopped = true;
        public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) => Volumes.Add(normalizedVolume);
        public void SetMuted(bool muted) => Muted = muted;
        public void Skip(TimeSpan offset) => Skips.Add(offset);
        public void SetPlaybackRate(double playbackRate) => Rate = playbackRate;
    }

    private sealed class Clipboard : IClipboardService
    {
        public TaskCompletionSource<string?> Completion { get; } = new();
        public TaskCompletionSource Called { get; } = new();
        public Task<string?> SetTextAsync(string text) { Called.SetResult(); return Completion.Task; }
    }

    private sealed class QueueTransport : IPlayerTransport, IQueueTransport
    {
        public PlaybackQueueState QueueState { get; private set; } = PlaybackQueueState.Initial;
        public event EventHandler<PlaybackQueueState>? QueueStateChanged;
        public string? Selected { get; private set; }
        public QueueRepeatMode Repeat { get; private set; }
        public List<IReadOnlyList<string>> OpenedQueues { get; } = [];
        public void Publish(PlaybackQueueState state) { QueueState = state; QueueStateChanged?.Invoke(this, state); }
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) { } public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
        public void OpenQueue(IReadOnlyList<string> sources) => OpenedQueues.Add(sources.ToArray()); public void Enqueue(IEnumerable<string> sources) { } public void SelectAndPlay(string token) => Selected = token; public void Move(string token, int destinationIndex) { } public void Remove(string token) { } public void ClearQueue() { } public void Previous() { } public void Next() { } public void SetRepeatMode(QueueRepeatMode mode) => Repeat = mode; public void SetOrder(QueueOrder order) { }
    }

    private sealed class CapabilityTransport : IPlayerTransport, IMediaCapabilitiesTransport, IMediaInformationTransport
    {
        public MediaInformationState MediaInformation { get; private set; } = MediaInformationState.Unavailable;
        public event EventHandler<MediaInformationState>? MediaInformationChanged;
        public void RefreshMediaInformation() { }
        public void PublishInformation(MediaInformationState state) { MediaInformation = state; MediaInformationChanged?.Invoke(this, state); }
        public MediaCapabilitiesState MediaCapabilities { get; private set; } = MediaCapabilitiesState.Initial;
        public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged;
        public int LastAudioDelay { get; private set; }
        public void Publish(MediaCapabilitiesState state) { MediaCapabilities = state; MediaCapabilitiesChanged?.Invoke(this, state); }
        public void SelectTrack(MediaTrackKind kind, string id) { } public void SelectTitle(string id) { } public void SelectChapter(string id) { }
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) { if (kind == MediaTrackKind.Audio) LastAudioDelay = milliseconds; }
        public void ResetDelay(MediaTrackKind kind) { }
        public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) => new(true, true, false);
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) { } public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
    }

    private sealed class EmptyPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ExternalPicker(string? path) : IMediaSourcePicker, IExternalTrackPicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExternalTrackAsync(MediaTrackKind kind, CancellationToken cancellationToken = default) => Task.FromResult(path);
    }

    private sealed class PlaylistPicker(string? openPath, string? savePath = null) : IMediaSourcePicker, IPlaylistDocumentPicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickPlaylistToOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(openPath);
        public Task<string?> PickPlaylistToSaveAsync(CancellationToken cancellationToken = default) => Task.FromResult(savePath);
    }

    private sealed class SettingsStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }
    private sealed class ExistingFiles : IMediaFileSystem { public bool FileExists(string path) => true; public bool DirectoryExists(string path) => false; public string GetFullPath(string path) => path; }
    private sealed class MissingFiles : IMediaFileSystem { public bool FileExists(string path) => false; public bool DirectoryExists(string path) => false; public string GetFullPath(string path) => path; }
    private sealed class HistoryStorage : IMediaHistoryStorage { public string? Content; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; }
    private sealed class ThrowingHistoryStorage(Exception error) : IMediaHistoryStorage { public string? Read() => null; public void WriteAtomically(string content) => throw error; }
    private sealed class SwitchStorage(Exception? error = null) : IMediaHistoryStorage { public Exception? Error = error; public string? Content; public string? Read() => Content; public void WriteAtomically(string content) { if (Error is not null) throw Error; Content = content; } }
    private sealed class OrderedStorage(List<string> events) : IMediaHistoryStorage { public string? Read() => null; public void WriteAtomically(string content) => events.Add("write"); }
    private sealed class OrderedTransport(List<string> events) : IPlayerTransport
    {
        public void Open(string source) { } public void TogglePause() { } public void Stop() => events.Add("stop"); public void Seek(double normalizedPosition) { }
        public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
    }
}

using Player.Application;
using Player.Contracts;
using VLCAvalonia.Services;
using VLCAvalonia.ViewModels;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class PlayerPresentationViewModelTests
{
    [Theory]
    [InlineData(MediaTrackKind.Audio)]
    [InlineData(MediaTrackKind.Subtitle)]
    public void DisableTrackIsASelectableVlcChoiceAndSelectionWaitsForReadback(MediaTrackKind kind)
    {
        CapabilityTransport transport = new();
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { });
        MediaTrack[] tracks = [new("-1", "Disable", true, false), new("1", "English", false, true)];
        var state = new MediaCapabilitiesState(1, 1, MediaCapabilityLoadState.Ready, tracks, tracks, [], [], new(0, 0));
        transport.Publish(state);
        var command = kind == MediaTrackKind.Audio ? vm.SelectAudioTrackCommand : vm.SelectSubtitleTrackCommand;
        Assert.True(command.CanExecute("-1"));
        Assert.False(command.CanExecute("unknown"));
        command.Execute("-1");
        Assert.Equal((kind, "-1"), transport.LastTrack);
        Assert.False(vm.AudioTracks[0].IsSelected);
        Assert.False(vm.SubtitleTracks[0].IsSelected);
        transport.Publish(state with { Status = MediaCapabilityLoadState.Loading });
        Assert.False(command.CanExecute("-1"));
    }

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
    [Theory]
    [InlineData("Queue", "Queue,Chapters")]
    [InlineData("Audio", "Audio,Subtitles")]
    [InlineData("Subtitles", "Audio,Subtitles")]
    [InlineData("Playback", "Playback,Video,Information")]
    public void EachBarButtonOpensItsOwnTabSet(string opener, string tabs)
    {
        var vm = CreateViewModel(new RecordingTransport());
        vm.ToggleDrawerPanelCommand.Execute(opener);
        Assert.True(vm.IsQueueOpen);
        Assert.Equal(opener, vm.DrawerTab);
        Assert.Equal(tabs.Split(','), vm.VisibleDrawerTabs);
        Assert.Equal(tabs.Split(','), PlayerPresentationViewModel.TabsFor(opener));
    }

    [Theory]
    [InlineData("Chapters", "Queue,Chapters")]
    [InlineData("Video", "Playback,Video,Information")]
    [InlineData("Information", "Playback,Video,Information")]
    public void MenusAndShortcutsShowTheTabSetOfTheirTab(string tab, string tabs)
    {
        var vm = new PlayerPresentationViewModel(new CapabilityTransport(), new EmptyPicker(), () => { });
        if (tab == "Information") vm.MediaInformationCommand.Execute(null);
        else vm.ToggleMediaControlsCommand.Execute(tab);
        Assert.True(vm.IsQueueOpen);
        Assert.Equal(tab, vm.DrawerTab);
        Assert.Equal(tabs.Split(','), vm.VisibleDrawerTabs);
    }

    [Fact]
    public void BarButtonsShowTheirActiveStateAndToggleTheirPanel()
    {
        var vm = CreateViewModel(new RecordingTransport());
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        bool[] Active() => [vm.IsQueuePanelOpen, vm.IsAudioPanelOpen, vm.IsSubtitlePanelOpen, vm.IsPlaybackPanelOpen];

        vm.ToggleDrawerPanel("Audio");
        Assert.Equal([false, true, false, false], Active());
        Assert.Contains(nameof(vm.IsAudioPanelOpen), changed);
        // The other track button switches tabs instead of closing.
        vm.ToggleDrawerPanel("Subtitles");
        Assert.True(vm.IsQueueOpen);
        Assert.Equal([false, false, true, false], Active());
        vm.ToggleDrawerPanel("Subtitles");
        Assert.False(vm.IsQueueOpen);
        Assert.Equal([false, false, false, false], Active());

        // The queue button stays active on its Chapters tab and closes from it.
        vm.ToggleDrawerPanel("Queue");
        vm.DrawerTab = "Chapters";
        Assert.Equal([true, false, false, false], Active());
        vm.ToggleDrawerPanel("Queue");
        Assert.False(vm.IsQueueOpen);

        // Another panel's button switches panels.
        vm.ToggleDrawerPanel("Playback");
        vm.DrawerTab = "Information";
        Assert.Equal([false, false, false, true], Active());
        vm.ToggleDrawerPanel("Queue");
        Assert.True(vm.IsQueueOpen);
        Assert.Equal("Queue", vm.DrawerTab);
        Assert.Equal([true, false, false, false], Active());
    }

    [Fact]
    public void QueueShortcutOpensTheQueuePanelOrClosesIt()
    {
        var vm = new PlayerPresentationViewModel(new QueueTransport(), new EmptyPicker(), () => { });
        vm.DrawerTab = "Video";
        vm.IsQueueOpen = true;
        vm.ToggleQueueCommand.Execute(null);
        Assert.True(vm.IsQueueOpen);
        Assert.Equal("Queue", vm.DrawerTab);
        vm.DrawerTab = "Chapters";
        vm.ToggleQueueCommand.Execute(null);
        Assert.False(vm.IsQueueOpen);
        vm.ToggleQueueCommand.Execute(null);
        Assert.True(vm.IsQueueOpen);
        Assert.Equal("Chapters", vm.DrawerTab);
    }

    [Fact]
    public void SpeedSliderShowsTheClampedRateAndWritesThroughTheRateCommand()
    {
        RecordingTransport transport = new();
        var vm = CreateViewModel(transport);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, canSeek: true) with { PlaybackRate = 1.5 });
        Assert.Equal(1.5, vm.PlaybackSpeed);
        Assert.Equal("1.5×", vm.PlaybackRateText);
        Assert.Contains(nameof(vm.PlaybackSpeed), changed);
        Assert.Contains(nameof(vm.PlaybackRateText), changed);
        Assert.Equal(0, transport.Rate);

        vm.PlaybackSpeed = .8;
        Assert.Equal(.75, transport.Rate);
        vm.PlaybackSpeed = 5;
        Assert.Equal(2, transport.Rate);

        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, canSeek: true) with { PlaybackRate = 4 });
        Assert.Equal(2, vm.PlaybackSpeed);
        Assert.Equal("4×", vm.PlaybackRateText);
    }

    [Fact]
    public void QueueHeaderCountsItemsAndShowsRepeatAndShuffleStates()
    {
        QueueTransport transport = new();
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { });
        Assert.Equal("0 items", vm.QueueSummaryText);
        transport.Publish(new(1, 1, QueueLoadState.Ready, [new("a", "a.mp4", "a.mp4", 0, true)], 0, QueueRepeatMode.CurrentItem, QueueOrder.Normal));
        Assert.Equal("1 item", vm.QueueSummaryText);
        Assert.True(vm.IsRepeatOn);
        Assert.True(vm.IsRepeatItem);
        Assert.False(vm.IsShuffleOn);
        transport.Publish(new(1, 2, QueueLoadState.Ready, [new("a", "a.mp4", "a.mp4", 0, true), new("b", "b.mp4", "b.mp4", 1, false)], 0, QueueRepeatMode.None, QueueOrder.Random));
        Assert.Equal("2 items", vm.QueueSummaryText);
        Assert.False(vm.IsRepeatOn);
        Assert.True(vm.IsShuffleOn);
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
    public void RememberVolumeRestoresTheLastSessionAndWritesOnlyChanges()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        Assert.True(settings.Current.RememberVolume);
        Assert.Equal(SettingApplyMode.Restart, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.RememberVolume)));
        MemoryVolume memory = new() { Saved = new(60, true) };
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { }, settings: settings, volumeMemory: memory);
        Assert.Equal((60d, true), (vm.Volume, vm.IsMuted));

        // Windows kept another volume for the app's audio session: the remembered one is sent.
        MediaEngineState playing = State(0, PlaybackStatus.Playing, false) with { Volume = .4, IsMuted = false };
        vm.ApplyEngineState(playing with { Status = PlaybackStatus.Opening });
        Assert.Empty(transport.Volumes);
        vm.ApplyEngineState(playing);
        Assert.Equal([.6], transport.Volumes);
        Assert.True(transport.Muted);
        vm.ApplyEngineState(playing with { Volume = .6, IsMuted = true });
        vm.ApplyEngineState(playing with { Volume = .6, IsMuted = true });
        Assert.Single(transport.Volumes);
        vm.SaveVolume();
        Assert.Equal(0, memory.Writes);

        // The volume the user sets is the one to remember, written once.
        vm.Volume = 80;
        vm.ApplyEngineState(playing with { Volume = .8, IsMuted = true });
        vm.SaveVolume();
        vm.SaveVolume();
        Assert.Equal(new RememberedVolume(80, true), memory.Saved);
        Assert.Equal(1, memory.Writes);

        // The next open keeps the volume; nothing more is sent. Closing writes what changed.
        vm.ApplyEngineState(playing with { Generation = 2, Volume = .8, IsMuted = true });
        Assert.Equal([.6, .8], transport.Volumes);
        vm.Registry.Execute(PlayerCommandId.ToggleMute);
        vm.ApplyEngineState(playing with { Generation = 2, Volume = .8, IsMuted = false });
        vm.OnClosing();
        Assert.Equal(new RememberedVolume(80, false), memory.Saved);
        Assert.Equal(2, memory.Writes);
    }

    [Fact]
    public void WithRememberVolumeOffPlaybackStartsAtTheDefaultAndNothingIsWritten()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { RememberVolume = false });
        MemoryVolume memory = new() { Saved = new(30, true) };
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { }, settings: settings, volumeMemory: memory);
        Assert.Equal((100d, false), (vm.Volume, vm.IsMuted));

        MediaEngineState playing = State(0, PlaybackStatus.Playing, false) with { Volume = .3, IsMuted = true };
        vm.ApplyEngineState(playing);
        Assert.Equal([1d], transport.Volumes);
        Assert.False(transport.Muted);
        vm.ApplyEngineState(playing with { Volume = 1, IsMuted = false });
        vm.Volume = 70;
        vm.ApplyEngineState(playing with { Volume = .7, IsMuted = false });
        vm.OnClosing();

        Assert.Equal(0, memory.Writes);
        Assert.Equal(new RememberedVolume(30, true), memory.Saved);
    }

    [Fact]
    public void WithRememberVolumeOffPlaybackStartsAtTheStartupVolumeWithinTheMaximum()
    {
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { RememberVolume = false, StartupVolume = 60 });
        var vm = new PlayerPresentationViewModel(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, volumeMemory: new MemoryVolume { Saved = new(30, true) });
        Assert.Equal((60d, false), (vm.Volume, vm.IsMuted));

        // A remembered or startup volume above the maximum starts at the maximum.
        settings.Update(s => s with { RememberVolume = true, MaximumVolume = 150 });
        vm = new PlayerPresentationViewModel(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings, volumeMemory: new MemoryVolume { Saved = new(250, false) });
        Assert.Equal(150d, vm.Volume);
    }

    [Fact]
    public void MaximumVolumeAndStepFollowTheSettingsAtOnce()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { }, settings: settings);
        vm.ApplyEngineState(State(0, PlaybackStatus.Playing, false));
        List<string?> changes = [];
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.Equal((200d, 2d), (vm.MaximumVolume, vm.VolumeStep));

        settings.Update(s => s with { MaximumVolume = 300, VolumeStep = 5 });
        vm.Volume = 280;
        Assert.Equal((300d, 5d, 280d), (vm.MaximumVolume, vm.VolumeStep, vm.Volume));
        Assert.Contains(nameof(PlayerPresentationViewModel.MaximumVolume), changes);
        Assert.Contains("0 to 300 percent", vm.VolumeHelpText);
        Assert.Equal(2.8, transport.Volumes[^1], 3);

        // A lower maximum brings the volume down to it and tells VLC.
        settings.Update(s => s with { MaximumVolume = 125 });
        Assert.Equal(125d, vm.Volume);
        Assert.Equal(1.25, transport.Volumes[^1], 3);
        vm.Volume = 180;
        Assert.Equal(125d, vm.Volume);
    }

    [Fact]
    public void VolumeRestoreStopsWhenTheEngineKeepsItsOwnVolume()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        MemoryVolume memory = new() { Saved = new(60, false) };
        var vm = new PlayerPresentationViewModel(transport, new EmptyPicker(), () => { }, settings: settings, volumeMemory: memory);
        MediaEngineState playing = State(0, PlaybackStatus.Playing, false) with { Volume = .4 };

        for (int i = 0; i < 8; i++) vm.ApplyEngineState(playing with { Position = i / 100d });

        Assert.Equal(5, transport.Volumes.Count);
        vm.SaveVolume();
        Assert.Equal(new RememberedVolume(40, false), memory.Saved);
    }

    [Theory]
    [InlineData(0, false, VolumeLevel.Mute)]
    [InlineData(20, false, VolumeLevel.Low)]
    [InlineData(50, false, VolumeLevel.Medium)]
    [InlineData(80, false, VolumeLevel.High)]
    [InlineData(150, false, VolumeLevel.High)]
    [InlineData(80, true, VolumeLevel.Mute)]
    public void VolumeIconLevelFollowsVolumeAndMute(double volume, bool muted, VolumeLevel expected)
    {
        PlayerPresentationViewModel viewModel = CreateViewModel(new RecordingTransport());

        viewModel.ApplyEngineState(State(0, PlaybackStatus.Playing, false) with { Volume = volume / 100, IsMuted = muted });

        Assert.Equal(expected, viewModel.VolumeLevel);
    }

    [Fact]
    public void VolumeIconLevelIsRaisedWhenVolumeOrMuteChanges()
    {
        PlayerPresentationViewModel viewModel = CreateViewModel(new RecordingTransport());
        MediaEngineState playing = State(0, PlaybackStatus.Playing, false) with { Volume = .2 };
        viewModel.ApplyEngineState(playing);
        int raised = 0;
        viewModel.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(PlayerPresentationViewModel.VolumeLevel) ? 1 : 0;

        viewModel.ApplyEngineState(playing with { Volume = .9 });
        viewModel.ApplyEngineState(playing with { Volume = .9, IsMuted = true });

        Assert.Equal(2, raised);
        Assert.Equal(VolumeLevel.Mute, viewModel.VolumeLevel);
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
    public void SkipSizesHaveVlcsKeysAndFollowTheSettings()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel viewModel = new(transport, new EmptyPicker(), () => { }, settings: settings);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true));
        settings.Update(s => s with { VeryShortSkipSeconds = 2, MediumSkipSeconds = 45, LongSkipSeconds = 600 });

        foreach (PlayerCommandId command in new[] { PlayerCommandId.SkipBackwardVeryShort, PlayerCommandId.SkipForwardVeryShort, PlayerCommandId.SkipBackwardMedium, PlayerCommandId.SkipForwardMedium, PlayerCommandId.SkipBackwardLong, PlayerCommandId.SkipForwardLong })
            viewModel.Registry.Execute(command);

        Assert.Equal([-2, 2, -45, 45, -600, 600], transport.Skips.Select(skip => skip.TotalSeconds));
        IReadOnlyDictionary<PlayerCommandId, string> defaults = PlayerShortcutPolicy.Effective(viewModel.Registry, null);
        Assert.Equal(PlayerShortcutPolicy.PlatformDefault("Shift+Left", PlayerShortcutPolicy.UsesCommandKey), defaults[PlayerCommandId.SkipBackwardVeryShort]);
        Assert.Equal(PlayerShortcutPolicy.PlatformDefault("Ctrl+Right", PlayerShortcutPolicy.UsesCommandKey), defaults[PlayerCommandId.SkipForwardMedium]);
        Assert.Equal(PlayerShortcutPolicy.PlatformDefault("Ctrl+Alt+Left", PlayerShortcutPolicy.UsesCommandKey), defaults[PlayerCommandId.SkipBackwardLong]);
        // Every default is unique.
        Assert.True(PlayerShortcutPolicy.TryValidate(defaults.Select(pair => new PlayerShortcut(pair.Key, pair.Value)), out string? error), error);
    }

    [Fact]
    public void ShortcutFeedbackSaysWhatTheShortcutDoes()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel viewModel = new(transport, new EmptyPicker(), () => { }, settings: settings);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { IsMuted = false });

        Assert.Equal("Pause", viewModel.ShortcutFeedbackText(PlayerCommandId.TogglePause));
        Assert.Equal("Muted", viewModel.ShortcutFeedbackText(PlayerCommandId.ToggleMute));
        Assert.Equal("−10 s", viewModel.ShortcutFeedbackText(PlayerCommandId.SkipBackward));
        Assert.Equal("+300 s", viewModel.ShortcutFeedbackText(PlayerCommandId.SkipForwardLong));
        Assert.Null(viewModel.ShortcutFeedbackText(PlayerCommandId.ToggleFullscreen));
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Paused, true));
        Assert.Equal("Play", viewModel.ShortcutFeedbackText(PlayerCommandId.TogglePause));
    }

    [Fact]
    public void TheWheelMovesTheVolumeOrThePosition()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel viewModel = new(transport, new EmptyPicker(), () => { }, settings: settings);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Volume = .5 });

        Assert.Equal("Volume 54%", viewModel.ApplyWheel(new WheelStep(WheelTarget.Volume, 4)));
        Assert.Equal(.54, transport.Volumes[^1], 3);
        Assert.Equal("−3 s", viewModel.ApplyWheel(new WheelStep(WheelTarget.Seek, -3)));
        Assert.Equal(TimeSpan.FromSeconds(-3), transport.Skips[^1]);
        // Not above the maximum volume.
        viewModel.ApplyWheel(new WheelStep(WheelTarget.Volume, 500));
        Assert.Equal(PlayerSettings.Default.MaximumVolume / 100d, transport.Volumes[^1], 3);
        Assert.Null(viewModel.ApplyWheel(default));
    }

    [Fact]
    public void KeepOnTopIsTheRememberedAlwaysOnTopAndTheTitleCanHideTheFileName()
    {
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { KeepOnTop = true });
        PlayerPresentationViewModel viewModel = new(new RecordingTransport(), new EmptyPicker(), () => { }, settings: settings);
        Assert.True(viewModel.IsAlwaysOnTop);

        viewModel.SetAlwaysOnTopCommand.Execute(false);
        Assert.False(settings.Current.KeepOnTop);
        settings.Update(s => s with { KeepOnTop = true });
        Assert.True(viewModel.IsAlwaysOnTop);

        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Title = "movie.mkv" });
        Assert.Equal("movie.mkv", viewModel.TitleBarText);
        List<string?> changed = []; viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        settings.Update(s => s with { ShowFileNameInTitle = false });
        Assert.Equal("VLC Avalonia", viewModel.TitleBarText);
        Assert.Contains(nameof(PlayerPresentationViewModel.TitleBarText), changed);
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

    [Fact]
    public void DismissClosesTheResumePromptAndKeepsTheSavedPosition()
    {
        RecordingTransport transport = new(); MediaHistoryService history = SeedHistory(); PlayerPresentationViewModel vm = CreateOpeningViewModel(transport, history);
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.2, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" }); Assert.True(vm.HasPendingResume);
        vm.Registry.Execute(PlayerCommandId.DismissResume);
        Assert.False(vm.HasPendingResume); Assert.False(vm.Registry.CanExecute(PlayerCommandId.DismissResume)); Assert.Empty(transport.Seeks);
        Assert.NotNull(history.FindResume(new("a.mp4", MediaSourceKind.File, "a")));
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

    [Fact]
    public void CheckpointReplacesOnlyTheChangedHomeCard()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch; MediaHistoryService history = new(new HistoryStorage());
        history.RecordOpened(new MediaSource("b.mp4", MediaSourceKind.File, "b"), true);
        PlayerPresentationViewModel vm = new(new RecordingTransport(), new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history, clock: () => now);
        vm.OpenSource("a.mp4", MediaSourceKind.File); vm.ApplyEngineState(State(.4, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        IReadOnlyList<HomeMediaItem> cards = vm.HomeItems; HomeMediaItem other = Assert.Single(cards, x => x.Source.EndsWith("b.mp4", StringComparison.Ordinal));
        now += TimeSpan.FromSeconds(10); vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Same(cards, vm.HomeItems);
        Assert.Same(other, Assert.Single(vm.HomeItems, x => x.Source.EndsWith("b.mp4", StringComparison.Ordinal)));
        Assert.Equal(.5, Assert.Single(vm.HomeItems, x => x.HasProgress).Progress);
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
        string directory = Path.Combine(Path.GetTempPath(), $"vlcavalonia-playlist-{Guid.NewGuid():N}");
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
        string directory = Path.Combine(Path.GetTempPath(), $"vlcavalonia-playlist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string playlist = Path.Combine(directory, "saved.m3u8");
            QueueTransport transport = new();
            PlayerPresentationViewModel vm = new(transport, new PlaylistPicker(null, playlist), () => { });
            transport.Publish(new(1, 1, QueueLoadState.Ready, [new("one", "One", "first.mp4", 0, true), new("two", "Two", "https://example.test/second.mp4", 1, false)], 0, QueueRepeatMode.None, QueueOrder.Normal));

            Assert.True(vm.SavePlaylistCommand.CanExecute(null));
            // Unix can read a newly created file before its asynchronous write
            // finishes. Await completion instead of relying on Windows sharing.
            await vm.SavePlaylistAsync();
            string content = await File.ReadAllTextAsync(playlist);

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


    [Fact]
    public void ContinueAlwaysResumesWithoutAsking()
    {
        MediaHistoryService history = new(new HistoryStorage()); MediaSource source = new("a.mp4", MediaSourceKind.File, "a");
        history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 0, 0);
        PlayerSettingsService settings = new(new SettingsStorage()); settings.Update(s => s with { ResumePlayback = true, ResumeAutomatically = true, ResumeMinimumElapsedSeconds = 30, ResumeMaximumRemainingSeconds = 20 });
        RecordingTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: history);

        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.1, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });

        Assert.False(vm.HasPendingResume);
        Assert.Equal([.4], transport.Seeks);
    }

    [Fact]
    public void RepeatAndShuffleChosenInThePlayerAreRemembered()
    {
        PlayerSettingsService settings = new(new SettingsStorage());
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, settings: settings);

        vm.Registry.Execute(PlayerCommandId.QueueRepeat);
        vm.Registry.Execute(PlayerCommandId.QueueShuffle);

        Assert.Equal(QueueRepeatMode.CurrentItem, transport.Repeat);
        Assert.Equal(("One", true), (settings.Current.QueueRepeat, settings.Current.QueueShuffle));
    }

    [Fact]
    public void AddToTheQueueInsteadOfReplacingItJoinsThePlayingQueue()
    {
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { EnqueueOnOpen = true });
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        // Nothing plays: the first open starts a queue.
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        Assert.Equal(["a.mp4"], Assert.Single(transport.OpenedQueues));
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Source = "a.mp4" });
        transport.Publish(new PlaybackQueueState(1, 1, QueueLoadState.Ready, [new QueueItem("1:1:a", "a", "a.mp4", 0, true)], 0, QueueRepeatMode.None, QueueOrder.Normal));

        // While it plays, a file, a URL or a drop joins it; a recent item replaces it.
        vm.OpenSource("b.mp4", MediaSourceKind.File);
        vm.OpenDroppedSources(["c.mp4"]);
        Assert.Equal([["b.mp4"], ["c.mp4"]], transport.Enqueued);
        Assert.Single(transport.OpenedQueues);
        Assert.True(vm.IsPlaying);
        vm.OpenSource("d.mp4", MediaSourceKind.Recent);
        Assert.Equal(2, transport.OpenedQueues.Count);

        // Off, a file replaces the queue.
        settings.Update(s => s with { EnqueueOnOpen = false });
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Generation = 2, Source = "d.mp4" });
        vm.OpenSource("e.mp4", MediaSourceKind.File);
        Assert.Equal(3, transport.OpenedQueues.Count);
    }

    [Fact]
    public void AddToTheQueueDoesNotJoinAQueueThatIsStopping()
    {
        PlayerSettingsService settings = new(new SettingsStorage());
        settings.Update(s => s with { EnqueueOnOpen = true });
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new ExistingFiles()));
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Source = "a.mp4" });
        transport.Publish(new PlaybackQueueState(1, 1, QueueLoadState.Ready, [new QueueItem("1:1:a", "a", "a.mp4", 0, true)], 0, QueueRepeatMode.None, QueueOrder.Normal));

        // Stop asked, the engine not stopped yet: the next file starts a new queue.
        vm.StopCommand.Execute(null);
        vm.OpenSource("b.mp4", MediaSourceKind.File);

        Assert.Empty(transport.Enqueued);
        Assert.Equal([["a.mp4"], ["b.mp4"]], transport.OpenedQueues);
    }

    [Fact]
    public async Task DroppedFoldersAreReadWithTheLibraryFolderSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "vlc-avalonia-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = Path.Combine(root, "show");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(folder, "sub", "b.mp4"), "x");
            string single = Path.Combine(root, "z.mp4");
            File.WriteAllText(single, "x");
            PlayerSettingsService settings = new(new SettingsStorage());
            settings.Update(s => s with { FolderOpening = "FolderOnly" });
            QueueTransport transport = new();
            PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, settings: settings, sourceValidator: new MediaSourceValidator(new SystemMediaFileSystem()));
            async Task<string[]> NextQueue(int count)
            {
                for (int i = 0; i < 200 && transport.OpenedQueues.Count < count; i++) await Task.Delay(25);
                return transport.OpenedQueues[count - 1].Select(source => Path.GetRelativePath(root, source).Replace('\\', '/')).ToArray();
            }

            // One folder, then a folder with a file: this folder only, in drop order.
            vm.OpenDroppedSources([folder]);
            Assert.Equal(["show/a.mp4"], await NextQueue(1));
            settings.Update(s => s with { FolderOpening = "Subfolders" });
            vm.OpenDroppedSources([folder, single]);
            Assert.Equal(["show/a.mp4", "show/sub/b.mp4", "z.mp4"], await NextQueue(2));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void PlaybackFinishedIsRaisedWhenPlaybackEndsByItselfOnly()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));
        int finished = 0;
        vm.PlaybackFinished += (_, _) => finished++;

        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Generation = 1, Source = "a.mp4" });
        vm.ApplyEngineState(State(1, PlaybackStatus.Stopped, true) with { Generation = 1, Source = "a.mp4" });
        vm.ApplyEngineState(State(1, PlaybackStatus.Stopped, true) with { Generation = 1, Source = "a.mp4" });
        Assert.Equal(1, finished);

        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Generation = 2, Source = "a.mp4" });
        vm.Registry.Execute(PlayerCommandId.Stop);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Stopped, true) with { Generation = 2, Source = "a.mp4" });
        Assert.Equal(1, finished);
    }

    [Fact]
    public void QueueEditingReachesTheQueueWithTheRowsTokenWhenItCanRun()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));
        int finished = 0;
        vm.PlaybackFinished += (_, _) => finished++;
        QueueItem[] items = [new("a", "A", "a.mp4", 0, true), new("b", "B", "b.mp4", 1, false), new("c", "C", "c.mp4", 2, false)];
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(State(.5, PlaybackStatus.Playing, true) with { Source = "a.mp4" });
        transport.Publish(new(1, 1, QueueLoadState.Ready, items, 0, QueueRepeatMode.None, QueueOrder.Normal));

        // The first item has nothing before it; the second has.
        Assert.False(vm.PreviousCommand.CanExecute(null));
        vm.PreviousCommand.Execute(null);
        transport.Publish(new(1, 2, QueueLoadState.Ready, [items[0] with { IsCurrent = false }, items[1] with { IsCurrent = true }, items[2]], 1, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.True(vm.PreviousCommand.CanExecute(null));
        vm.PreviousCommand.Execute(null);

        // Move and Remove act on a row of the current queue only.
        Assert.False(vm.MoveQueueItemCommand.CanExecute(new QueueMove("gone", 0)));
        vm.MoveQueueItemCommand.Execute(new QueueMove("gone", 0));
        vm.MoveQueueItemCommand.Execute(new QueueMove("c", 0));
        vm.MoveQueueItemCommand.Execute(new QueueMove("a", 1));
        Assert.False(vm.RemoveQueueItemCommand.CanExecute("gone"));
        vm.RemoveQueueItemCommand.Execute("gone");
        vm.RemoveQueueItemCommand.Execute("b");
        vm.ClearQueueCommand.Execute(null);
        Assert.Equal(["previous", "move c 0", "move a 1", "remove b", "clear"], transport.Calls);

        // Clearing stops playback by request: it is not the end of the queue.
        vm.ApplyEngineState(State(.5, PlaybackStatus.Stopped, true) with { Source = "a.mp4" });
        transport.Publish(new(1, 3, QueueLoadState.Empty, [], -1, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.Equal(0, finished);
        Assert.False(vm.ClearQueueCommand.CanExecute(null));
        Assert.False(vm.PreviousCommand.CanExecute(null));
    }

    [Fact]
    public async Task APickedFileOrFolderOpensAndLeavesHome()
    {
        string root = Path.Combine(Path.GetTempPath(), "vlc-avalonia-pick-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = Path.Combine(root, "show");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(folder, "sub", "b.mp4"), "x");
            string file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, "x");
            QueueTransport transport = new();
            MediaPicker picker = new(file, folder);
            PlayerPresentationViewModel vm = new(transport, picker, () => { }, sourceValidator: new MediaSourceValidator(new SystemMediaFileSystem()));
            Assert.True(vm.IsHomeOpen);

            // Open File: the picked file becomes the queue and the player shows.
            vm.OpenMediaCommand.Execute(null);
            await WaitForAsync(() => transport.OpenedQueues.Count == 1);
            Assert.Equal([file], transport.OpenedQueues[0]);
            Assert.Equal((false, false, PlaybackStatus.Opening), (vm.IsHomeOpen, vm.HasOpenError, vm.PlaybackStatus));

            // Open Folder: the picked folder's media, subfolders included, in name order.
            vm.ShowHomeCommand.Execute(null);
            vm.OpenFolderCommand.Execute(null);
            await WaitForAsync(() => transport.OpenedQueues.Count == 2);
            Assert.Equal([Path.Combine(folder, "a.mp4"), Path.Combine(folder, "sub", "b.mp4")], transport.OpenedQueues[1]);
            // Home gives way when the engine starts opening the first item.
            vm.ApplyEngineState(State(0, PlaybackStatus.Opening, false) with { Generation = 2, Source = Path.Combine(folder, "a.mp4") });
            Assert.Equal((false, false), (vm.IsHomeOpen, vm.HasOpenError));
            Assert.Equal(["media", "folder"], picker.Requests);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RecentLibraryAndHomeRowsOpenAnExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "vlc-avalonia-recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string recent = Path.Combine(root, "recent.mp4"), resumed = Path.Combine(root, "resumed.mp4");
            File.WriteAllText(recent, "x");
            File.WriteAllText(resumed, "x");
            MediaHistoryService history = new(new HistoryStorage());
            history.RecordOpened(new(recent, MediaSourceKind.File, "recent"), true);
            history.Checkpoint(new(resumed, MediaSourceKind.File, "resumed"), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20);
            QueueTransport transport = new();
            PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { }, sourceValidator: new MediaSourceValidator(new SystemMediaFileSystem()), history: history);

            // A recent row (More menu, library drawer) opens its file and keeps it in the list.
            vm.OpenRecentCommand.Execute(recent);
            Assert.Equal([recent], Assert.Single(transport.OpenedQueues));
            Assert.Equal((false, false), (vm.IsHomeOpen, vm.HasOpenError));
            Assert.Contains(vm.RecentItems, item => item.Source == recent);

            // A Home card opens the same way; a Continue watching one offers its position once it plays.
            vm.ShowHomeCommand.Execute(null);
            vm.SelectHomeSectionCommand.Execute("Continue watching");
            HomeMediaItem card = Assert.Single(vm.HomeItems);
            vm.OpenRecentCommand.Execute(card.Source);
            Assert.Equal([resumed], transport.OpenedQueues[1]);
            Assert.False(vm.IsHomeOpen);
            vm.ApplyEngineState(State(.1, PlaybackStatus.Playing, true) with { Generation = 2, Source = resumed });
            Assert.True(vm.HasPendingResume);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DelayStepsResetsAndTitleAndChapterRowsReachTheEngine()
    {
        CapabilityTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new EmptyPicker(), () => { });
        Assert.False(vm.AdjustSubtitleDelayCommand.CanExecute("100"));
        transport.Publish(new(1, 1, MediaCapabilityLoadState.Ready, [], [],
            [new("0", "Title 1", true), new("1", "Title 2", false)], [new("0", "Chapter 1", true), new("1", "Chapter 2", false)], new(250, -300)));

        // The drawer's −100 and +100 pass their step as text; each step starts from VLC's readback.
        vm.AdjustSubtitleDelayCommand.Execute("100");
        vm.AdjustSubtitleDelayCommand.Execute("-100");
        vm.ResetSubtitleDelayCommand.Execute(null);
        vm.ResetAudioDelayCommand.Execute(null);
        Assert.False(vm.SelectTitleCommand.CanExecute("9"));
        vm.SelectTitleCommand.Execute("9");
        vm.SelectTitleCommand.Execute("1");
        Assert.False(vm.SelectChapterCommand.CanExecute("9"));
        vm.SelectChapterCommand.Execute("1");
        Assert.Equal(["Subtitle delay -200", "Subtitle delay -400", "reset Subtitle", "reset Audio", "title 1", "chapter 1"], transport.Calls);

        // Steps stop at the 10 s limit.
        transport.Publish(new(1, 2, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 9_950)));
        vm.AdjustSubtitleDelayCommand.Execute("100");
        Assert.Equal("Subtitle delay 10000", transport.Calls[^1]);
    }

    [Fact]
    public async Task AddAudioFileAttachesAnAudioFileForTheNextOpenOnly()
    {
        CapabilityTransport transport = new();
        transport.Publish(new(1, 1, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 0)));
        PlayerPresentationViewModel vm = new(transport, new ExternalPicker("notes.txt"), () => { });
        vm.AttachExternalAudioCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("Choose a supported audio file.", vm.MediaControlsNotice);
        Assert.Empty(transport.Calls);

        vm = new PlayerPresentationViewModel(transport, new ExternalPicker("music.wav"), () => { });
        vm.AttachExternalAudioCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(["attach Audio music.wav"], transport.Calls);
        Assert.Equal("Added for the next media open. It is not a current live track.", vm.MediaControlsNotice);
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
        public List<IReadOnlyList<string>> Enqueued { get; } = [];
        public List<string> Calls { get; } = [];
        public void Publish(PlaybackQueueState state) { QueueState = state; QueueStateChanged?.Invoke(this, state); }
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) { } public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
        public void OpenQueue(IReadOnlyList<string> sources) => OpenedQueues.Add(sources.ToArray()); public void Enqueue(IEnumerable<string> sources) => Enqueued.Add(sources.ToArray()); public void SelectAndPlay(string token) => Selected = token; public void Move(string token, int destinationIndex) => Calls.Add($"move {token} {destinationIndex}"); public void Remove(string token) => Calls.Add($"remove {token}"); public void ClearQueue() => Calls.Add("clear"); public void Previous() => Calls.Add("previous"); public void Next() => Calls.Add("next"); public void SetRepeatMode(QueueRepeatMode mode) => Repeat = mode; public void SetOrder(QueueOrder order) { }
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
        public (MediaTrackKind Kind, string Id)? LastTrack { get; private set; }
        public void Publish(MediaCapabilitiesState state) { MediaCapabilities = state; MediaCapabilitiesChanged?.Invoke(this, state); }
        public void SelectTrack(MediaTrackKind kind, string id) => LastTrack = (kind, id);
        public List<string> Calls { get; } = [];
        public void SelectTitle(string id) => Calls.Add($"title {id}"); public void SelectChapter(string id) => Calls.Add($"chapter {id}");
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) { if (kind == MediaTrackKind.Audio) LastAudioDelay = milliseconds; Calls.Add($"{kind} delay {milliseconds}"); }
        public void ResetDelay(MediaTrackKind kind) => Calls.Add($"reset {kind}");
        public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) { Calls.Add($"attach {kind} {path}"); return new(true, true, false); }
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) { } public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
    }

    private sealed class EmptyPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MediaPicker(string? file, string? folder) : IMediaSourcePicker
    {
        public List<string> Requests { get; } = [];
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) { Requests.Add("media"); return Task.FromResult(file); }
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) { Requests.Add("folder"); return Task.FromResult(folder); }
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

    private sealed class MemoryVolume : IVolumeMemory
    {
        public RememberedVolume? Saved { get; set; }
        public int Writes { get; private set; }
        public RememberedVolume? Load() => Saved;
        public void Save(RememberedVolume value) { Saved = value; Writes++; }
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

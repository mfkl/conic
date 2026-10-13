using Player.Application;
using Conic.ViewModels;
using Conic;
using Avalonia.Controls;
using Xunit;

namespace Conic.UITests;

public sealed class SettingsViewModelTests
{
    // Defaults are written with Ctrl and shown with Cmd on macOS.
    private static string DisplayedDefault(string gesture) =>
        PlayerShortcutPolicy.Display(PlayerShortcutPolicy.PlatformDefault(gesture, PlayerShortcutPolicy.UsesCommandKey));

    [Fact]
    public void ShortcutWriteFailuresKeepPendingEditsAndShowAnInlineError()
    {
        PlayerSettingsService service = new(new ThrowingStorage());
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        SettingsViewModel viewModel = new(service, registry);
        ShortcutRow row = Assert.Single(viewModel.EditableShortcutRows);
        row.PendingGesture = "Ctrl+M";
        Assert.False(viewModel.SaveShortcut(row));
        Assert.True(viewModel.HasError);
        Assert.Equal("Ctrl+M", row.PendingGesture);
        viewModel.ResetShortcut(PlayerCommandId.OpenMedia);
        Assert.True(viewModel.HasError);
        Assert.Equal("Ctrl+M", row.PendingGesture);
        viewModel.ResetAllShortcuts();
        Assert.True(viewModel.HasError);
        Assert.Equal(PlayerShortcutPolicy.PlatformDefault("Ctrl+O", PlayerShortcutPolicy.UsesCommandKey), PlayerShortcutPolicy.Effective(registry, service.Current.ShortcutOverrides)[PlayerCommandId.OpenMedia]);
    }

    [Fact]
    public void TrackShortcutsOpenTheirOwnDrawerTab()
    {
        PlayerCommandRegistry registry = new();
        object? openedTab = null;
        registry.Register(new(PlayerCommandId.ToggleMediaControls, "Tracks and synchronization", "Playback"), p => openedTab = p);
        SettingsViewModel viewModel = new(new PlayerSettingsService(new MemoryStorage()), registry);
        viewModel.OpenSubtitleControlsCommand.Execute(null);
        Assert.Equal("Subtitles", openedTab);
        viewModel.OpenAudioControlsCommand.Execute(null);
        Assert.Equal("Audio", openedTab);
    }

    [Fact]
    public void EmptySearchHasNoCategoryContentAndNullSelectionKeepsAValidPage()
    {
        SettingsViewModel viewModel = Create(out _);
        viewModel.Search = "no-such-setting-abcdef";
        Assert.False(viewModel.HasSettingsResults);
        viewModel.SelectedPage = null!;
        Assert.NotNull(viewModel.SelectedPage);
        viewModel.Search = "skip";
        Assert.True(viewModel.HasSettingsResults);
        Assert.True(viewModel.IsControls);
    }

    [Fact]
    public void ShortcutGroupsListPlaybackFirstAndFollowTheSearch()
    {
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        registry.Register(new(PlayerCommandId.ToggleFullscreen, "Fullscreen", "Window", "F11"), _ => { });
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ => { });
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => { });
        SettingsViewModel viewModel = new(new PlayerSettingsService(new MemoryStorage()), registry);

        Assert.Equal(["Playback", "Media", "Window"], viewModel.EditableShortcutGroups.Select(group => group.Category));
        Assert.Equal(["Play or pause", "Stop"], viewModel.EditableShortcutGroups[0].Rows.Select(row => row.Name));
        Assert.Equal([false, true, true], viewModel.EditableShortcutGroups.Select(group => group.HasDivider));
        Assert.Equal(viewModel.EditableShortcutRows.Count, viewModel.EditableShortcutGroups.Sum(group => group.Rows.Count));

        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.ShortcutSearch = "full";
        Assert.Contains(nameof(SettingsViewModel.EditableShortcutGroups), changed);
        ShortcutGroup only = Assert.Single(viewModel.EditableShortcutGroups);
        Assert.Equal("Window", only.Category);
        Assert.False(only.HasDivider);
    }

    [Fact]
    public void ConfirmedResetRefreshesVisibleShortcutRows()
    {
        PlayerSettingsService service = new(new MemoryStorage());
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        SettingsViewModel viewModel = new(service, registry);
        ShortcutRow row = Assert.Single(viewModel.EditableShortcutRows);
        row.PendingGesture = "Ctrl+M";
        Assert.True(viewModel.SaveShortcut(row));
        viewModel.ResetAllCommand.Execute(null);
        Assert.Equal("Ctrl+M", row.PendingGesture);
        viewModel.ResetAllCommand.Execute(null);
        Assert.Equal(DisplayedDefault("Ctrl+O"), row.PendingGesture);
        Assert.False(viewModel.ConfirmReset);
        viewModel.ShortcutSearch = "missing";
        Assert.False(viewModel.HasShortcutResults);
    }

    [Theory]
    [InlineData("theme", "General")]
    [InlineData("accent", "General")]
    [InlineData("skip", "Controls")]
    [InlineData("keyboard", "Controls")]
    [InlineData("volume", "Audio")]
    [InlineData("history", "Privacy")]
    [InlineData("reduce motion", "Interface")]
    [InlineData("speed", "Playback")]
    [InlineData("diagnostics", "Advanced")]
    [InlineData("shuffle", "Playback")]
    [InlineData("deinterlace", "Video")]
    [InlineData("hardware", "Performance")]
    [InlineData("proxy", "Network")]
    [InlineData("caching", "Network")]
    [InlineData("mouse", "Controls")]
    [InlineData("title bar", "Interface")]
    [InlineData("encoding", "Subtitles")]
    [InlineData("hidden", "Library")]
    public void SearchSelectsTheMatchingPage(string query, string page)
    {
        SettingsViewModel viewModel = Create(out _);
        viewModel.Search = query;
        Assert.Equal(page, viewModel.SelectedPage.Name);
    }

    [Fact]
    public void ExposesPersistedValuesOptionsAndApplyModes()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        service.Update(s => s with { Theme = "Dark", SkipForwardSeconds = 22 });
        viewModel.Refresh();
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Contains("Compact", viewModel.DensityOptions);
        Assert.True(viewModel.IsGeneral);
        Assert.Equal("Live", viewModel.LabelFor(nameof(PlayerSettings.Theme)));
        Assert.Equal("Next open", viewModel.LabelFor(nameof(PlayerSettings.DefaultSpeed)));
        Assert.Equal("Restart", viewModel.LabelFor("Unknown"));
    }

    [Fact]
    public void PagesAreTheDesignCategoriesWithWorkingSettings()
    {
        SettingsViewModel viewModel = Create(out _);

        Assert.Equal(["General", "Interface", "Privacy", "Controls", "Playback", "Video", "Audio", "Subtitles", "Library", "Network", "Performance", "Advanced"], viewModel.Pages.Select(page => page.Name));
        Assert.Equal(["App", "App", "App", "App", "Media", "Media", "Media", "Media", "Media", "System", "System", "System"], viewModel.Pages.Select(page => page.Group));
        Assert.Equal(["General", "Playback", "Network"], viewModel.Pages.Where(page => page.ShowsGroupHeader).Select(page => page.Name));
        Assert.Equal("General Settings", viewModel.DialogTitle);
        viewModel.SelectedPage = viewModel.Pages[6];
        Assert.True(viewModel.IsAudio);
        Assert.Equal("Audio Settings", viewModel.DialogTitle);
    }

    [Fact]
    public void GroupHeadersMoveToTheFirstPageThatMatchesTheSearch()
    {
        SettingsViewModel viewModel = Create(out _);

        viewModel.Search = "tracks";

        Assert.Equal(["Audio", "Subtitles"], viewModel.FilteredPages.Select(page => page.Name));
        Assert.Equal(["Audio"], viewModel.Pages.Where(page => page.ShowsGroupHeader).Select(page => page.Name));
        viewModel.Search = "";
        Assert.Equal(["General", "Playback", "Network"], viewModel.Pages.Where(page => page.ShowsGroupHeader).Select(page => page.Name));
    }

    [Fact]
    public void ThemeAndAccentChoicesApplyAndMirrorTheSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        Assert.Equal(["Theme Dark", "Theme Light", "Theme System"], viewModel.ThemeChoices.Select(choice => choice.AutomationName));
        Assert.Equal(AccentPalette.Colors.Select(color => $"Accent color {color.Name}"), viewModel.AccentChoices.Select(choice => choice.AutomationName));
        Assert.Equal("Dark", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
        Assert.Equal(AccentPalette.DefaultHex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);

        viewModel.ThemeChoices[1].IsSelected = true;
        viewModel.AccentChoices[5].IsSelected = true;
        // A radio button that is unchecked writes false; that changes nothing.
        viewModel.ThemeChoices[1].IsSelected = false;

        Assert.Equal("Light", service.Current.Theme);
        Assert.Equal(AccentPalette.Colors[5].Hex, service.Current.Accent);
        Assert.Equal("Light", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
        Assert.Equal(AccentPalette.Colors[5].Hex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);

        service.Update(s => s with { Theme = "System", Accent = AccentPalette.Colors[2].Hex });
        viewModel.Refresh();
        Assert.Equal("System", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
        Assert.Equal(AccentPalette.Colors[2].Hex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);
    }

    [Fact]
    public void FailedChoiceWriteKeepsTheCheckedChoiceInEffect()
    {
        SettingsViewModel viewModel = new(new PlayerSettingsService(new ThrowingStorage()));

        viewModel.AccentChoices[3].IsSelected = true;

        Assert.True(viewModel.HasError);
        Assert.Equal(AccentPalette.DefaultHex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);
    }

    [Fact]
    public void EachPageResetsOnlyItsOwnSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        PlayerSettings changed = PlayerSettings.Default with
        {
            Theme = "Light", Accent = "#1B5DD3", Density = "Compact", ReduceMotion = true, RememberRecentMedia = false,
            SkipForwardSeconds = 30, ResumePlayback = false, DefaultSpeed = 2, RememberVolume = false, AudioOutputModule = "waveout",
        };
        void Reset(string page)
        {
            service.Update(_ => changed);
            viewModel.SelectedPage = viewModel.Pages.Single(candidate => candidate.Name == page);
            Assert.True(viewModel.CanResetPage);
            viewModel.ResetPageCommand.Execute(null);
        }

        Reset("General");
        Assert.Equal(("Dark", AccentPalette.DefaultHex, "Compact"), (service.Current.Theme, service.Current.Accent, service.Current.Density));
        Reset("Interface");
        Assert.Equal(("Comfortable", false, "Light"), (service.Current.Density, service.Current.ReduceMotion, service.Current.Theme));
        Reset("Privacy");
        Assert.Equal((true, 30), (service.Current.RememberRecentMedia, service.Current.SkipForwardSeconds));
        Reset("Controls");
        Assert.Equal((10, false), (service.Current.SkipForwardSeconds, service.Current.ResumePlayback));
        Reset("Playback");
        Assert.Equal((true, 1d, false), (service.Current.ResumePlayback, service.Current.DefaultSpeed, service.Current.RememberVolume));
        Reset("Audio");
        Assert.Equal((true, "Default", 2d), (service.Current.RememberVolume, service.Current.AudioOutputModule, service.Current.DefaultSpeed));
        viewModel.SelectedPage = viewModel.Pages.Single(candidate => candidate.Name == "Advanced");
        Assert.False(viewModel.CanResetPage);
    }

    [Fact]
    public void SubtitlesControlsInterfaceAndLibraryResetOnlyTheirOwnRows()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        PlayerSettings changed = PlayerSettings.Default with
        {
            SubtitlesEnabled = false, SubtitleLanguage = "fr", SubtitleColor = "#FFFF00", SubtitleDelay = 500, ShortcutFeedback = false,
            MediumSkipSeconds = 30, ClickAction = "PlayPause", ReverseWheel = true,
            KeepOnTop = true, ShowFileNameInTitle = false, ShowControlsInFullscreen = false,
            EnqueueOnOpen = true, FolderOpening = "FolderOnly", ShowHiddenFiles = true,
        };
        PlayerSettings Reset(string page)
        {
            service.Update(_ => changed);
            viewModel.SelectedPage = viewModel.Pages.Single(candidate => candidate.Name == page);
            Assert.True(viewModel.CanResetPage);
            viewModel.ResetPageCommand.Execute(null);
            return service.Current with { ShortcutOverrides = null };
        }

        Assert.Equal(changed with
        {
            SubtitlesEnabled = true, SubtitleLanguage = "", SubtitleColor = "#FFFFFF", SubtitleDelay = 0, ShortcutFeedback = true,
        }, Reset("Subtitles"));
        Assert.Equal(changed with { MediumSkipSeconds = 60, ClickAction = "None", ReverseWheel = false }, Reset("Controls"));
        Assert.Equal(changed with { KeepOnTop = false, ShowFileNameInTitle = true, ShowControlsInFullscreen = true }, Reset("Interface"));
        Assert.Equal(changed with { EnqueueOnOpen = false, FolderOpening = "Subfolders", ShowHiddenFiles = false }, Reset("Library"));
    }

    [Fact]
    public void SubtitleColorsSlidersAndChoicesMapToTheSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        // Colors: any case, with or without #; anything else keeps the value in effect.
        viewModel.SubtitleColor = "ff8800";
        Assert.Equal("#FF8800", service.Current.SubtitleColor);
        viewModel.SubtitleColor = "orange";
        Assert.Equal("#FF8800", service.Current.SubtitleColor);
        Assert.Equal("#FF8800", viewModel.SubtitleColor);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF8800"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(viewModel.SubtitleColorSwatch).Color);

        // Sliders: whole percents, half seconds, whole seconds.
        viewModel.SubtitleOpacity = 42.4;
        viewModel.SubtitleBackgroundOpacity = 80.6;
        viewModel.ShortcutFeedbackSeconds = 1.3;
        viewModel.FileNameSeconds = 6.7;
        Assert.Equal((42, 81, 1.5, 7), (service.Current.SubtitleOpacity, service.Current.SubtitleBackgroundOpacity, service.Current.ShortcutFeedbackSeconds, service.Current.FileNameSeconds));

        // Choices hold the values the options map.
        viewModel.SubtitleMatching = viewModel.SubtitleMatchings.Single(option => option.Value == "4");
        viewModel.SubtitleEncoding = viewModel.SubtitleEncodings.Single(option => option.Value == "Windows-1251");
        viewModel.HorizontalWheelAction = viewModel.WheelActions.Single(option => option.Value == "Volume");
        viewModel.FolderOpening = viewModel.FolderOpenings.Single(option => option.Value == "FolderOnly");
        Assert.Equal((4, "Windows-1251", "Volume", "FolderOnly"), (service.Current.SubtitleMatching, service.Current.SubtitleEncoding, service.Current.HorizontalWheelAction, service.Current.FolderOpening));
        Assert.Contains("--sub-autodetect-fuzzy=4", PlayerEngineOptions.Arguments(service.Current, windows: true));

        // The preferred language is Playback's field.
        viewModel.SubtitleLanguage = "fr";
        Assert.Equal("fr", service.Current.SubtitleLanguage);
    }

    [Theory]
    [InlineData(nameof(PlayerSettings.SubtitleTextScale), "Live")]
    [InlineData(nameof(PlayerSettings.SubtitleColor), "Live")]
    [InlineData(nameof(PlayerSettings.SubtitleOpacity), "Next open")]
    [InlineData(nameof(PlayerSettings.SubtitleFont), "Next open")]
    [InlineData(nameof(PlayerSettings.ShowFileNameOnStart), "Next open")]
    [InlineData(nameof(PlayerSettings.ShortcutFeedback), "Live")]
    [InlineData(nameof(PlayerSettings.MediumSkipSeconds), "Live")]
    [InlineData(nameof(PlayerSettings.WheelAction), "Live")]
    [InlineData(nameof(PlayerSettings.KeepOnTop), "Live")]
    [InlineData(nameof(PlayerSettings.ResizeToVideo), "Next open")]
    [InlineData(nameof(PlayerSettings.RememberWindowGeometry), "Restart")]
    [InlineData(nameof(PlayerSettings.EnqueueOnOpen), "Next open")]
    [InlineData(nameof(PlayerSettings.ShowHiddenFiles), "Next open")]
    public void PhaseNineDRowsAreTaggedWithTheirApplyMode(string key, string tag)
    {
        Assert.Equal(tag, Create(out _).LabelFor(key));
    }

    [Fact]
    public void ContinueWhereYouLeftOffMapsToTheResumeSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        Assert.Equal("Ask", viewModel.ResumeMode.Value);
        viewModel.ResumeMode = viewModel.ResumeModes.Single(option => option.Value == "Always");
        Assert.Equal((true, true), (service.Current.ResumePlayback, service.Current.ResumeAutomatically));
        viewModel.ResumeMode = viewModel.ResumeModes.Single(option => option.Value == "Never");
        Assert.Equal((false, false), (service.Current.ResumePlayback, service.Current.ResumeAutomatically));
        Assert.Equal("Never", viewModel.ResumeMode.Value);
    }

    [Fact]
    public void PictureSlidersRoundAndResetTogether()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        viewModel.Hue = 33.333;
        viewModel.Brightness = 1.5;
        viewModel.Gamma = 2;
        Assert.Equal((33.33, 1.5, 2d), (service.Current.Hue, service.Current.Brightness, service.Current.Gamma));
        Assert.False(viewModel.CanAdjustGamma);
        viewModel.HardwareDecoding = viewModel.HardwareDecodingModes.Single(option => option.Value == "Off");
        Assert.True(viewModel.CanAdjustGamma);

        viewModel.ResetPictureCommand.Execute(null);

        Assert.True(PlayerEngineOptions.IsNeutralPicture(service.Current));
        Assert.Equal("Off", service.Current.HardwareDecoding);
    }

    [Fact]
    public void BufferingFieldsShowThePresetAndEditOnlyWithCustom()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        viewModel.Buffering = viewModel.BufferingPresets.Single(option => option.Value == "High");
        Assert.Equal((2000m, 2000m, false), (viewModel.FileCaching, viewModel.NetworkCaching, viewModel.IsCustomBuffering));
        viewModel.FileCaching = 700;
        Assert.Equal(PlayerSettings.Default.FileCaching, service.Current.FileCaching);

        viewModel.Buffering = viewModel.BufferingPresets.Single(option => option.Value == "Custom");
        viewModel.FileCaching = 700;
        viewModel.NetworkCaching = 4500;

        Assert.True(viewModel.IsCustomBuffering);
        Assert.Equal((700, 4500), (service.Current.FileCaching, service.Current.NetworkCaching));
        Assert.Equal(["--file-caching=700", "--network-caching=4500"], PlayerEngineOptions.Arguments(service.Current, windows: true, macOS: false));
    }

    [Fact]
    public void ProxyPasswordIsStoredProtected()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        viewModel.HttpProxy = "user@proxy.example:3128";

        viewModel.HttpProxyPassword = "pa55 word";

        // The password is kept with DPAPI; elsewhere the setting is hidden and stores nothing.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(("", null), (viewModel.HttpProxyPassword, service.Current.HttpProxyPasswordProtected));
            return;
        }
        Assert.Equal("pa55 word", viewModel.HttpProxyPassword);
        Assert.NotNull(service.Current.HttpProxyPasswordProtected);
        Assert.DoesNotContain("pa55", service.Current.HttpProxyPasswordProtected, StringComparison.Ordinal);
        Assert.Contains("--http-proxy-pwd=pa55 word", Conic.MainWindow.EngineArguments(service.Current));
        viewModel.HttpProxyPassword = "";
        Assert.Null(service.Current.HttpProxyPasswordProtected);
    }

    [Fact]
    public void NewPagesResetOnlyTheirOwnSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        PlayerSettings changed = PlayerSettings.Default with
        {
            AutoPlay = false, QueueRepeat = "All", Hue = 90, Deinterlace = "On", HardwareDecoding = "Off", DecodingThreads = 4,
            Buffering = "High", HttpProxy = "proxy:1", VerifyCertificates = false, Theme = "Light",
        };
        void Reset(string page)
        {
            service.Update(_ => changed);
            viewModel.SelectedPage = viewModel.Pages.Single(candidate => candidate.Name == page);
            Assert.True(viewModel.CanResetPage);
            viewModel.ResetPageCommand.Execute(null);
        }

        Reset("Playback");
        Assert.Equal((true, "Off", 90d), (service.Current.AutoPlay, service.Current.QueueRepeat, service.Current.Hue));
        Reset("Video");
        Assert.Equal((0d, "Automatic", "Off"), (service.Current.Hue, service.Current.Deinterlace, service.Current.HardwareDecoding));
        Reset("Performance");
        Assert.Equal(("Automatic", 0, "High"), (service.Current.HardwareDecoding, service.Current.DecodingThreads, service.Current.Buffering));
        Reset("Network");
        Assert.Equal(("Normal", "", true, "Light"), (service.Current.Buffering, service.Current.HttpProxy, service.Current.VerifyCertificates, service.Current.Theme));
    }

    [Fact]
    public void VideoAdjustmentsOpenTheVideoTab()
    {
        PlayerCommandRegistry registry = new();
        object? openedTab = null;
        registry.Register(new(PlayerCommandId.ToggleMediaControls, "Tracks and synchronization", "Playback"), p => openedTab = p);
        SettingsViewModel viewModel = new(new PlayerSettingsService(new MemoryStorage()), registry);

        viewModel.OpenVideoControlsCommand.Execute(null);

        Assert.Equal("Video", openedTab);
    }

    [Fact]
    public void ResetConfirmationCanBeCancelledAndOnlyResetsOnSecondAction()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        service.Update(s => s with { ResumePlayback = false, SkipForwardSeconds = 22 });
        viewModel.ResetAllCommand.Execute(null);
        Assert.True(viewModel.ConfirmReset);
        viewModel.CancelResetCommand.Execute(null);
        Assert.False(viewModel.ConfirmReset);
        viewModel.ResetAllCommand.Execute(null);
        viewModel.ResetAllCommand.Execute(null);
        Assert.Equal(PlayerSettings.Default, service.Current);
    }

    [Fact]
    public void FailedConfirmedResetKeepsConfirmationAndCurrentValue()
    {
        ThrowingStorage storage = new();
        PlayerSettingsService service = new(storage);
        SettingsViewModel viewModel = new(service);
        viewModel.ResetAllCommand.Execute(null);
        viewModel.ResetAllCommand.Execute(null);
        Assert.True(viewModel.ConfirmReset);
        Assert.Contains("could not be reset", viewModel.ErrorMessage);
        Assert.Equal(PlayerSettings.Default, service.Current);
    }

    [Fact]
    public void HotkeyRowsKeepPendingEditsDuringSearchAndRefreshAfterSaveOrReset()
    {
        PlayerSettingsService service = new(new MemoryStorage());
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "General", "Ctrl+,"), _ => { });
        SettingsViewModel viewModel = new(service, registry);
        ShortcutRow row = Assert.Single(viewModel.EditableShortcutRows, x => x.Command == PlayerCommandId.OpenMedia);
        row.PendingGesture = "Ctrl+M";
        viewModel.ShortcutSearch = "open";
        Assert.Same(row, Assert.Single(viewModel.EditableShortcutRows));
        Assert.Equal("Ctrl+M", row.PendingGesture);
        Assert.True(viewModel.SaveShortcut(row));
        Assert.Equal("Ctrl+M", Assert.Single(viewModel.EditableShortcutRows).PendingGesture);
        viewModel.ResetShortcut(PlayerCommandId.OpenMedia);
        Assert.Equal(DisplayedDefault("Ctrl+O"), Assert.Single(viewModel.EditableShortcutRows).PendingGesture);
    }

    [Fact]
    public void ShortcutFocusPolicyProtectsInteractiveEditors()
    {
        Assert.True(ShortcutFocusPolicy.Suppresses(new TextBox()));
        Assert.True(ShortcutFocusPolicy.Suppresses(new Slider()));
        Assert.True(ShortcutFocusPolicy.Suppresses(new ComboBox()));
        Assert.False(ShortcutFocusPolicy.Suppresses(null));
    }

    [Fact]
    public void DefaultSpeedOffersTheDesignRangeAndEachOptionRoundTrips()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        Assert.Equal(["0.25", "0.5", "0.75", "1", "1.25", "1.5", "1.75", "2"], viewModel.SpeedOptions);
        foreach (string option in viewModel.SpeedOptions)
        {
            viewModel.DefaultSpeed = option;
            Assert.Equal(option, viewModel.DefaultSpeed);
            Assert.Equal(double.Parse(option, System.Globalization.CultureInfo.InvariantCulture), service.Current.DefaultSpeed);
        }
    }

    [Fact]
    public void DefaultSpeedReachesVlcAsTheRateOfTheNextOpen()
    {
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A decimal comma must not reach VLC's command line.
            System.Globalization.CultureInfo.CurrentCulture = new("de-DE");
            Assert.Empty(Conic.Interop.VlcCorePlayer.RateArguments(1));
            Assert.Equal(["--rate=1.75"], Conic.Interop.VlcCorePlayer.RateArguments(1.75));
            Assert.Equal(["--rate=0.25"], Conic.Interop.VlcCorePlayer.RateArguments(.25));
            Assert.Empty(Conic.Interop.VlcCorePlayer.RateArguments(double.NaN));
            Assert.Empty(Conic.Interop.VlcCorePlayer.RateArguments(64));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void EqualizerPresetsFillTheBandsAndAChangedBandIsCustom()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        Assert.Equal("flat", viewModel.EqualizerPreset.Value);
        Assert.Equal(10, viewModel.EqualizerBands.Count);
        Assert.Equal(["60 Hz", "16 kHz"], new[] { viewModel.EqualizerBands[0].Frequency, viewModel.EqualizerBands[9].Frequency });

        viewModel.EqualizerPreset = viewModel.EqualizerPresets.Single(preset => preset.Value == "rock");
        Assert.Equal((5d, "8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2"), (service.Current.EqualizerPreamp, service.Current.EqualizerBands));
        Assert.Equal(8, viewModel.EqualizerBands[0].Gain);
        Assert.Equal("8.0 dB", viewModel.EqualizerBands[0].GainText);

        viewModel.EqualizerBands[0].Gain = 3.04;
        Assert.Equal(("custom", "3 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2"), (service.Current.EqualizerPreset, service.Current.EqualizerBands));
        Assert.Equal("Custom", viewModel.EqualizerPreset.Label);
        // Choosing Custom keeps the bands; back to the preset's values, the preset is found again.
        viewModel.EqualizerPreset = viewModel.EqualizerPresets.Single(preset => preset.Value == "custom");
        Assert.Equal("3 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2", service.Current.EqualizerBands);
        viewModel.EqualizerBands[0].Gain = 8;
        Assert.Equal("rock", service.Current.EqualizerPreset);
        viewModel.EqualizerPreamp = 7.26;
        Assert.Equal((7.3, "custom", "7.3 dB"), (service.Current.EqualizerPreamp, service.Current.EqualizerPreset, viewModel.EqualizerPreampText));

        Assert.False(viewModel.IsEqualizerOpen);
        viewModel.ToggleEqualizerCommand.Execute(null);
        Assert.Equal((true, "Close equalizer"), (viewModel.IsEqualizerOpen, viewModel.EqualizerToggleText));
    }

    [Fact]
    public void AudioRowsWriteTheirSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        // Startup volume: Remember last volume, or a fixed volume.
        Assert.True(viewModel.RemembersVolume);
        viewModel.StartsAtFixedVolume = true;
        viewModel.StartupVolume = 40;
        viewModel.RemembersVolume = false;
        Assert.Equal((false, 40), (service.Current.RememberVolume, service.Current.StartupVolume));
        viewModel.RemembersVolume = true;
        Assert.True(service.Current.RememberVolume);
        Assert.Equal("Restart", viewModel.LabelFor(nameof(PlayerSettings.StartupVolume)));

        // The maximum snaps to the slider's 25 % steps.
        viewModel.MaximumVolume = 262;
        Assert.Equal(250, service.Current.MaximumVolume);
        Assert.Equal("Live", viewModel.LabelFor(nameof(PlayerSettings.MaximumVolume)));

        // Normalization is on while it has a level; the target needs it on.
        Assert.False(viewModel.NormalizeLoudness);
        viewModel.NormalizationTarget = 4;
        Assert.Null(service.Current.NormalizationMaximumLevel);
        viewModel.NormalizeLoudness = true;
        Assert.Equal(2, service.Current.NormalizationMaximumLevel);
        viewModel.NormalizationTarget = 4;
        Assert.Equal(4, service.Current.NormalizationMaximumLevel);
        viewModel.NormalizeLoudness = false;
        Assert.Null(service.Current.NormalizationMaximumLevel);

        viewModel.ReplayGain = viewModel.ReplayGainModes.Single(mode => mode.Value == "album");
        Assert.True(viewModel.HasReplayGain);
        viewModel.SpatialAudioMode = viewModel.SpatialAudioModes.Single(mode => mode.Value == "Room");
        viewModel.DolbySurround = viewModel.DolbySurroundModes.Single(mode => mode.Value == "Off");
        viewModel.ResamplerQuality = viewModel.ResamplerQualities.Single(mode => mode.Value == "Best");
        viewModel.PitchShift = 2.5m;
        viewModel.AudioDelay = -300;
        viewModel.AudioPassthrough = true;
        Assert.Equal(("album", "Room", "Off", "Best", 2.5, -300, (bool?)true),
            (service.Current.ReplayGain, service.Current.SpatialAudioMode, service.Current.DolbySurround, service.Current.ResamplerQuality, service.Current.PitchShift, service.Current.AudioDelay, service.Current.AudioPassthrough));
        Assert.Equal("Next open", viewModel.LabelFor(nameof(PlayerSettings.AudioOutputModule)));

        // Preferred audio language is Playback's field, shown on both pages.
        viewModel.AudioLanguage = "DE";
        Assert.Equal("de", service.Current.AudioLanguage);

        // The Audio page resets all of it, and nothing on other pages.
        service.Update(s => s with { DefaultSpeed = 2, Equalizer = true, Karaoke = true, AudioOutputModule = "waveout", AudioDevice = "x" });
        viewModel.SelectedPage = viewModel.Pages.Single(page => page.Name == "Audio");
        viewModel.ResetPageCommand.Execute(null);
        Assert.Equal(PlayerSettings.Default with { DefaultSpeed = 2, ShortcutOverrides = service.Current.ShortcutOverrides }, service.Current);
    }

    private static SettingsViewModel Create(out PlayerSettingsService service)
    {
        service = new PlayerSettingsService(new MemoryStorage());
        return new SettingsViewModel(service);
    }

    private sealed class MemoryStorage : IPlayerSettingsStorage
    {
        public string? Content { get; private set; }
        public string? Read() => Content;
        public void WriteAtomically(string content) => Content = content;
        public void PreserveInvalid(string content) { }
    }

    private sealed class ThrowingStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) => throw new IOException();
        public void PreserveInvalid(string content) { }
    }
}

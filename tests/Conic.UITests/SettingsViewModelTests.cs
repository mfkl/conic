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

        Assert.Equal(["General", "Interface", "Privacy", "Controls", "Playback", "Video", "Audio", "Subtitles", "Advanced"], viewModel.Pages.Select(page => page.Name));
        Assert.Equal(["App", "App", "App", "App", "Media", "Media", "Media", "Media", "System"], viewModel.Pages.Select(page => page.Group));
        Assert.Equal(["General", "Playback", "Advanced"], viewModel.Pages.Where(page => page.ShowsGroupHeader).Select(page => page.Name));
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
        Assert.Equal(["General", "Playback", "Advanced"], viewModel.Pages.Where(page => page.ShowsGroupHeader).Select(page => page.Name));
    }

    [Fact]
    public void ThemeAndAccentChoicesApplyAndMirrorTheSettings()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);

        Assert.Equal(["Theme Dark", "Theme Light", "Theme System"], viewModel.ThemeChoices.Select(choice => choice.AutomationName));
        Assert.Equal(AccentPalette.Colors.Select(color => $"Accent color {color.Name}"), viewModel.AccentChoices.Select(choice => choice.AutomationName));
        Assert.Equal("System", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
        Assert.Equal(AccentPalette.DefaultHex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);

        viewModel.ThemeChoices[1].IsSelected = true;
        viewModel.AccentChoices[5].IsSelected = true;
        // A radio button that is unchecked writes false; that changes nothing.
        viewModel.ThemeChoices[1].IsSelected = false;

        Assert.Equal("Light", service.Current.Theme);
        Assert.Equal(AccentPalette.Colors[5].Hex, service.Current.Accent);
        Assert.Equal("Light", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
        Assert.Equal(AccentPalette.Colors[5].Hex, Assert.Single(viewModel.AccentChoices, choice => choice.IsSelected).Value);

        service.Update(s => s with { Theme = "Dark", Accent = AccentPalette.Colors[2].Hex });
        viewModel.Refresh();
        Assert.Equal("Dark", Assert.Single(viewModel.ThemeChoices, choice => choice.IsSelected).Value);
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
        Assert.Equal(("System", AccentPalette.DefaultHex, "Compact"), (service.Current.Theme, service.Current.Accent, service.Current.Density));
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
        foreach (string page in new[] { "Video", "Subtitles", "Advanced" })
        {
            viewModel.SelectedPage = viewModel.Pages.Single(candidate => candidate.Name == page);
            Assert.False(viewModel.CanResetPage);
        }
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
    public void AudioStartupArgumentsAreAllowlistedExactAndEmptyByDefault()
    {
        Assert.Empty(MainWindow.ToAudioArguments(PlayerSettings.Default));
        PlayerSettings configured = PlayerSettings.Default with { AudioOutputModule = "mmdevice", NormalizationMaximumLevel = 1.25, AudioPassthrough = true };
        Assert.Equal(["--aout=mmdevice", "--audio-filter=normvol", "--norm-max-level=1.25", "--spdif"], MainWindow.ToAudioArguments(configured, windows: true));
        Assert.Equal(["--audio-filter=normvol", "--norm-max-level=1.25", "--spdif"], MainWindow.ToAudioArguments(configured, windows: false));
        Assert.Equal(["--no-spdif"], MainWindow.ToAudioArguments(PlayerSettings.Default with { AudioPassthrough = false }));
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

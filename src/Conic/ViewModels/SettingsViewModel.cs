using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Conic.Commands;

namespace Conic.ViewModels;
internal sealed class ShortcutRow : INotifyPropertyChanged
{
    private EditablePlayerShortcut _value;
    private string _pendingGesture;
    public ShortcutRow(EditablePlayerShortcut value, Action<ShortcutRow> save, Action<ShortcutRow> reset)
    {
        _value = value; _pendingGesture = PlayerShortcutPolicy.Display(value.Gesture);
        SaveCommand = new RelayCommand(_ => save(this)); ResetCommand = new RelayCommand(_ => reset(this));
    }
    public PlayerCommandId Command => _value.Command;
    public string Name => _value.Name;
    public string Category => _value.Category;
    public string DefaultGesture => PlayerShortcutPolicy.Display(_value.DefaultGesture);
    public string PendingGesture { get => _pendingGesture; set { if (_pendingGesture == value) return; _pendingGesture = value; Notify(nameof(PendingGesture)); } }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public void Refresh(EditablePlayerShortcut value) { _value = value; _pendingGesture = PlayerShortcutPolicy.Display(value.Gesture); Notify(nameof(PendingGesture)); Notify(nameof(DefaultGesture)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
/// <summary>A heading and its shortcuts in the shortcut editor; every group but the first has a divider above it.</summary>
internal sealed record ShortcutGroup(string Category, IReadOnlyList<ShortcutRow> Rows, bool HasDivider);
internal sealed partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerSettingsService _service;
    private readonly IPlayerCommandRegistry? _registry;
    private readonly Dictionary<PlayerCommandId, ShortcutRow> _shortcutRows = [];
    private string _search = string.Empty;
    private SettingsPage _selectedPage;
    private bool _confirmReset;
    private string? _errorMessage;
    private string _shortcutSearch = string.Empty;

    public SettingsViewModel(PlayerSettingsService service, IPlayerCommandRegistry? registry = null)
    {
        _service = service;
        _registry = registry;
        _selectedPage = Pages[0];
        ResetPageCommand = new RelayCommand(_ => ResetPage());
        ResetAllCommand = new RelayCommand(_ => ConfirmOrResetAll());
        CancelResetCommand = new RelayCommand(_ => ConfirmReset = false);
        ResetAllShortcutsCommand = new RelayCommand(_ => ResetAllShortcuts());
        ThemeChoices = PlayerSettings.Themes
            .Select(theme => new SettingChoice(theme, theme, $"Theme {theme}", value => Theme = value))
            .ToArray();
        AccentChoices = AccentPalette.Colors
            .Select(color => new SettingChoice(color.Hex, color.Name, $"Accent color {color.Name}", value => Accent = value, color.Hex))
            .ToArray();
        RefreshShortcutRows();
        InitializeNavigationCommands();
        UpdateGroupHeaders();
        RefreshChoices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The design's categories in its App / Media / System groups, limited to
    // the ones with working settings (D2). Search matches a page's name and
    // keywords; the first match is selected.
    public IReadOnlyList<SettingsPage> Pages { get; } =
    [
        new("General", "App", "Preferences", "appearance theme dark light system accent color colour"),
        new("Interface", "App", "Interface", "density compact comfortable controls hide auto-hide timeout reduce motion accessibility animation"),
        new("Privacy", "App", "Privacy", "history recent media resume positions clear remember forget"),
        new("Controls", "App", "Hotkeys", "keyboard shortcuts hotkeys commands skip backward forward seconds"),
        new("Playback", "Media", "Playback", "continue resume left off position near end start playing autoplay paused speed default rate language audio subtitle repeat loop shuffle random order stop after each item finishes quit home seek precisely fast avi repair index"),
        new("Video", "Media", "Video", "enable video fullscreen screensaver sleep display picture hue brightness contrast saturation gamma color colour reset deinterlace deinterlacing method aspect ratio crop zoom framing adjustments"),
        new("Audio", "Media", "Audio", "enable volume maximum startup step output device module normalize normalization loudness target replay gain preamp equalizer eq preset bands bass treble spatial headphones room compressor pitch karaoke language tracks delay dolby surround resampler resampling quality passthrough spdif receiver"),
        new("Subtitles", "Media", "Subtitles", "render tracks delay synchronization"),
        new("Network", "System", "Link", "buffering caching cache latency local files network streams proxy http password certificates trust tls ssl cipher security"),
        new("Performance", "System", "Preferences", "hardware accelerated decoding gpu d3d11 direct3d dxva loop filter deblocking h.264 threads drop frames late smooth"),
        new("Advanced", "System", "Info", "engine diagnostics media information codec streams"),
    ];

    public IReadOnlyList<string> DensityOptions { get; } = ["Comfortable", "Compact"];
    // The design's range: 0.25 to 2 in 0.25 steps, as the playback panel's slider.
    public IReadOnlyList<string> SpeedOptions { get; } = Enumerable.Range(1, (int)(PlayerSettings.MaximumDefaultSpeed * 4))
        .Select(step => (step / 4d).ToString(System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
    public IReadOnlyList<SettingChoice> ThemeChoices { get; }
    public IReadOnlyList<SettingChoice> AccentChoices { get; }

    public IEnumerable<SettingsPage> FilteredPages => Pages.Where(page => page.Matches(Search));

    public SettingsPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is null || !Set(ref _selectedPage, value)) return;
            ConfirmReset = false;
            if (IsAudio) RefreshAudioDevices();
            foreach (string property in new[] { nameof(IsGeneral), nameof(IsInterface), nameof(IsPrivacy), nameof(IsControls), nameof(IsPlayback), nameof(IsVideo), nameof(IsAudio), nameof(IsSubtitles), nameof(IsNetwork), nameof(ShowsProxyOnNetwork), nameof(ShowsSecurityOnNetwork), nameof(IsPerformance), nameof(IsAdvanced), nameof(DialogTitle), nameof(CanResetPage) })
                Notify(property);
        }
    }

    public bool IsGeneral => SelectedPage.Name == "General";
    public bool IsInterface => SelectedPage.Name == "Interface";
    public bool IsPrivacy => SelectedPage.Name == "Privacy";
    public bool IsControls => SelectedPage.Name == "Controls";
    public bool IsPlayback => SelectedPage.Name == "Playback";
    public bool IsVideo => SelectedPage.Name == "Video";
    public bool IsAudio => SelectedPage.Name == "Audio";
    public bool IsSubtitles => SelectedPage.Name == "Subtitles";
    public bool IsNetwork => SelectedPage.Name == "Network";
    public bool IsPerformance => SelectedPage.Name == "Performance";
    public bool IsAdvanced => SelectedPage.Name == "Advanced";
    public string DialogTitle => SelectedPage.Title;

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? string.Empty)) return;
            UpdateGroupHeaders();
            Notify(nameof(FilteredPages));
            Notify(nameof(HasSettingsResults));
            if (FilteredPages.FirstOrDefault() is { } match) SelectedPage = match;
        }
    }

    public bool ConfirmReset { get => _confirmReset; private set => Set(ref _confirmReset, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (Set(ref _errorMessage, value)) Notify(nameof(HasError)); } }
    public ICommand ResetPageCommand { get; }
    public ICommand ResetAllCommand { get; }
    public ICommand CancelResetCommand { get; }
    public ICommand ResetAllShortcutsCommand { get; }

    public string ShortcutSearch { get => _shortcutSearch; set { if (Set(ref _shortcutSearch, value ?? string.Empty)) NotifyShortcutRows(); } }
    public IReadOnlyList<ShortcutRow> EditableShortcutRows => _shortcutRows.Values.Where(row => string.IsNullOrWhiteSpace(ShortcutSearch) || (row.Name + " " + row.Category).Contains(ShortcutSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(row => row.Category).ThenBy(row => row.Name).ToArray();

    // The design's shortcut editor (2437:14832) lists the shortcuts under a
    // heading per group; Playback first, as its "Basic" group leads there.
    private static readonly string[] ShortcutCategoryOrder = ["Playback", "Media", "Audio", "Subtitles", "Video", "Window", "Library"];
    public IReadOnlyList<ShortcutGroup> EditableShortcutGroups => EditableShortcutRows
        .GroupBy(row => row.Category)
        .OrderBy(group => Array.IndexOf(ShortcutCategoryOrder, group.Key) is int index and >= 0 ? index : ShortcutCategoryOrder.Length)
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select((group, index) => new ShortcutGroup(group.Key, group.ToArray(), index > 0))
        .ToArray();

    private void NotifyShortcutRows()
    {
        Notify(nameof(EditableShortcutRows));
        Notify(nameof(EditableShortcutGroups));
        Notify(nameof(HasShortcutResults));
    }
    public bool SaveShortcut(ShortcutRow row) => TrySaveShortcuts([new PlayerShortcut(row.Command, row.PendingGesture)]);
    public bool TrySaveShortcuts(IEnumerable<PlayerShortcut> changes)
    {
        if (_registry is null) { ErrorMessage = "Shortcuts are unavailable."; return false; }
        try
        {
            bool saved = new PlayerShortcutEditor(_service, _registry).TrySave(changes, out string? error);
            ErrorMessage = saved ? null : error;
            if (saved) RefreshShortcutRows();
            return saved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { ErrorMessage = "Shortcuts could not be saved. Your previous shortcuts are still active."; return false; }
    }
    public void ResetShortcut(PlayerCommandId id) => ResetShortcuts(editor => editor.Reset(id));
    public void ResetAllShortcuts() => ResetShortcuts(editor => editor.ResetAll());
    private void ResetShortcuts(Action<PlayerShortcutEditor> reset)
    {
        if (_registry is null) return;
        try { reset(new PlayerShortcutEditor(_service, _registry)); ErrorMessage = null; RefreshShortcutRows(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { ErrorMessage = "Shortcuts could not be reset because they could not be saved."; }
    }

    public string LabelFor(string key) => PlayerSettingsService.ApplyMode(key) switch { SettingApplyMode.Live => "Live", SettingApplyMode.NextOpen => "Next open", _ => "Restart" };

    public bool ResumePlayback { get => _service.Current.ResumePlayback; set => Update(s => s with { ResumePlayback = value }); }
    public bool RememberVolume { get => _service.Current.RememberVolume; set => Update(s => s with { RememberVolume = value }); }
    public bool RememberRecentMedia { get => _service.Current.RememberRecentMedia; set => Update(s => s with { RememberRecentMedia = value }); }
    public bool RememberResumeHistory { get => _service.Current.RememberResumeHistory; set => Update(s => s with { RememberResumeHistory = value }); }
    public decimal ResumeMinimumElapsedSeconds { get => _service.Current.ResumeMinimumElapsedSeconds; set => Update(s => s with { ResumeMinimumElapsedSeconds = (int)value }); }
    public decimal ResumeMaximumRemainingSeconds { get => _service.Current.ResumeMaximumRemainingSeconds; set => Update(s => s with { ResumeMaximumRemainingSeconds = (int)value }); }
    public string Theme { get => _service.Current.Theme; set => Update(s => s with { Theme = value }); }
    public string Accent { get => _service.Current.Accent; set => Update(s => s with { Accent = value }); }
    public string Density { get => _service.Current.Density; set => Update(s => s with { Density = value }); }
    public decimal ControlTimeoutSeconds { get => _service.Current.ControlTimeoutSeconds; set => Update(s => s with { ControlTimeoutSeconds = (int)value }); }
    public bool ReduceMotion { get => _service.Current.ReduceMotion; set => Update(s => s with { ReduceMotion = value }); }
    public bool AutoHideControls { get => _service.Current.AutoHideControls; set => Update(s => s with { AutoHideControls = value }); }
    public string DefaultSpeed { get => _service.Current.DefaultSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture); set => Update(s => s with { DefaultSpeed = double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : s.DefaultSpeed }); }
    public decimal SkipBackwardSeconds { get => _service.Current.SkipBackwardSeconds; set => Update(s => s with { SkipBackwardSeconds = (int)value }); }
    public decimal SkipForwardSeconds { get => _service.Current.SkipForwardSeconds; set => Update(s => s with { SkipForwardSeconds = (int)value }); }
    public IReadOnlyList<string> AudioOutputOptions { get; } = AudioOutputPolicy.Options();
    public string AudioOutputModule { get => AudioOutputPolicy.Effective(_service.Current.AudioOutputModule); set => Update(s => s with { AudioOutputModule = value }); }
    // Off is VLC's default; a document from before phase 9c may hold null.
    public bool AudioPassthrough { get => _service.Current.AudioPassthrough == true; set => Update(s => s with { AudioPassthrough = value }); }

    public void Refresh()
    {
        foreach (string name in new[] { nameof(ResumePlayback), nameof(RememberVolume), nameof(RememberRecentMedia), nameof(RememberResumeHistory), nameof(ResumeMinimumElapsedSeconds), nameof(ResumeMaximumRemainingSeconds), nameof(Theme), nameof(Accent), nameof(Density), nameof(IsCompactDensity), nameof(ControlTimeoutSeconds), nameof(ReduceMotion), nameof(AutoHideControls), nameof(DefaultSpeed), nameof(SkipBackwardSeconds), nameof(SkipForwardSeconds), nameof(AudioOutputModule), nameof(AudioPassthrough) })
            Notify(name);
        foreach (string name in EngineRowProperties)
            Notify(name);
        RefreshAudioRows();
        RefreshChoices();
    }

    private void Update(Func<PlayerSettings, PlayerSettings> update)
    {
        try { _service.Update(update); ErrorMessage = null; Refresh(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "Settings could not be saved. Your previous settings are still active.";
            // A choice that was just checked goes back to the value in effect.
            RefreshChoices();
        }
    }

    private void RefreshChoices()
    {
        foreach (SettingChoice choice in ThemeChoices) choice.Sync(choice.Value == _service.Current.Theme);
        foreach (SettingChoice choice in AccentChoices) choice.Sync(choice.Value == _service.Current.Accent);
    }

    // Each page resets what it shows; Controls also restores the default
    // shortcuts. Subtitles and Advanced have no settings of their own.
    private void ResetPage()
    {
        PlayerSettings defaults = PlayerSettings.Default;
        if (IsControls)
        {
            Update(s => s with { SkipBackwardSeconds = defaults.SkipBackwardSeconds, SkipForwardSeconds = defaults.SkipForwardSeconds });
            ResetAllShortcuts();
            return;
        }
        Update(s =>
            IsGeneral ? s with { Theme = defaults.Theme, Accent = defaults.Accent } :
            IsInterface ? s with { Density = defaults.Density, ControlTimeoutSeconds = defaults.ControlTimeoutSeconds, ReduceMotion = defaults.ReduceMotion, AutoHideControls = defaults.AutoHideControls } :
            IsPrivacy ? s with { RememberRecentMedia = defaults.RememberRecentMedia, RememberResumeHistory = defaults.RememberResumeHistory } :
            IsPlayback ? ResetPlaybackPage(s, defaults) :
            IsVideo ? ResetVideoPage(s, defaults) :
            IsAudio ? ResetAudioPage(s, defaults) :
            IsNetwork ? ResetNetworkPage(s, defaults) :
            IsPerformance ? ResetPerformancePage(s, defaults) :
            s);
    }

    private void ConfirmOrResetAll()
    {
        if (!ConfirmReset) { ConfirmReset = true; return; }
        try { _service.ResetAll(); ErrorMessage = null; ConfirmReset = false; Refresh(); RefreshShortcutRows(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorMessage = "Settings could not be reset because they could not be saved."; }
    }

    private void RefreshShortcutRows()
    {
        if (_registry is null) return;
        foreach (EditablePlayerShortcut value in new PlayerShortcutEditor(_service, _registry).Items())
        {
            if (_shortcutRows.TryGetValue(value.Command, out ShortcutRow? row)) row.Refresh(value);
            else _shortcutRows.Add(value.Command, new ShortcutRow(value, shortcut => SaveShortcut(shortcut), shortcut => ResetShortcut(shortcut.Command)));
        }
        NotifyShortcutRows();
    }

    // The category list shows a group header above the first visible page
    // of each group.
    private void UpdateGroupHeaders()
    {
        HashSet<string> seen = [];
        foreach (SettingsPage page in Pages)
            page.ShowsGroupHeader = page.Matches(Search) && seen.Add(page.Group);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name!); return true; }
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

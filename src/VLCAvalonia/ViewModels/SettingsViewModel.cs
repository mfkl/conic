using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;
internal sealed record SettingsPage(string Name, string Keywords);
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
internal sealed partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerSettingsService _service; private readonly IPlayerCommandRegistry? _registry; private readonly Dictionary<PlayerCommandId, ShortcutRow> _shortcutRows = []; private string _search = string.Empty; private SettingsPage _selectedPage; private bool _confirmReset; private string? _errorMessage; private string _shortcutSearch = string.Empty;
    public SettingsViewModel(PlayerSettingsService service, IPlayerCommandRegistry? registry = null) { _service = service; _registry = registry; _selectedPage = Pages[0]; ResetPageCommand = new RelayCommand(_ => ResetPage()); ResetAllCommand = new RelayCommand(_ => ConfirmOrResetAll()); CancelResetCommand = new RelayCommand(_ => ConfirmReset = false); ResetAllShortcutsCommand = new RelayCommand(_ => ResetAllShortcuts()); RefreshShortcutRows(); InitializeNavigationCommands(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SettingsPage> Pages { get; } = [new("General", "resume volume privacy library favorites"), new("Appearance", "theme density controls timeout reduce motion accessibility"), new("Playback", "speed skip backward forward"), new("Audio", "output normalization passthrough device"), new("Video", "aspect crop zoom deinterlace snapshot"), new("Subtitles", "render tracks delay"), new("Hotkeys", "shortcut keyboard commands"), new("Advanced", "engine diagnostics")];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Dark"]; public IReadOnlyList<string> DensityOptions { get; } = ["Comfortable", "Compact"]; public IReadOnlyList<string> SpeedOptions { get; } = ["0.5", "1", "1.5", "2"];
    public IEnumerable<SettingsPage> FilteredPages => Pages.Where(page => string.IsNullOrWhiteSpace(Search) || (page.Name + " " + page.Keywords).Contains(Search, StringComparison.OrdinalIgnoreCase));
    public SettingsPage SelectedPage { get => _selectedPage; set { if (value is not null && Set(ref _selectedPage, value)) { ConfirmReset = false; foreach (string property in new[] { nameof(IsGeneral), nameof(IsAppearance), nameof(IsPlayback), nameof(IsAudio), nameof(IsVideo), nameof(IsSubtitles), nameof(IsHotkeys), nameof(IsAdvanced), nameof(PageSummary), nameof(CanResetPage) }) Notify(property); } } }
    public bool IsGeneral => SelectedPage.Name == "General"; public bool IsAppearance => SelectedPage.Name == "Appearance"; public bool IsPlayback => SelectedPage.Name == "Playback"; public bool IsAudio => SelectedPage.Name == "Audio"; public bool IsVideo => SelectedPage.Name == "Video"; public bool IsSubtitles => SelectedPage.Name == "Subtitles"; public bool IsHotkeys => SelectedPage.Name == "Hotkeys"; public bool IsAdvanced => SelectedPage.Name == "Advanced";
    public string Search { get => _search; set { if (Set(ref _search, value ?? string.Empty)) { Notify(nameof(FilteredPages)); Notify(nameof(HasSettingsResults)); SettingsPage? match = FilteredPages.FirstOrDefault(); if (match is not null) SelectedPage = match; } } }
    public bool ConfirmReset { get => _confirmReset; private set => Set(ref _confirmReset, value); } public string? ErrorMessage { get => _errorMessage; private set { if (Set(ref _errorMessage, value)) Notify(nameof(HasError)); } }
    public ICommand ResetPageCommand { get; } public ICommand ResetAllCommand { get; } public ICommand CancelResetCommand { get; } public ICommand ResetAllShortcutsCommand { get; }
    public string ShortcutSearch { get => _shortcutSearch; set { if (Set(ref _shortcutSearch, value ?? string.Empty)) { Notify(nameof(EditableShortcutRows)); Notify(nameof(HasShortcutResults)); } } }
    public IReadOnlyList<ShortcutRow> EditableShortcutRows => _shortcutRows.Values.Where(row => string.IsNullOrWhiteSpace(ShortcutSearch) || (row.Name + " " + row.Category).Contains(ShortcutSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(row => row.Category).ThenBy(row => row.Name).ToArray();
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
    public bool ResumePlayback { get => _service.Current.ResumePlayback; set => Update(s => s with { ResumePlayback = value }); } public bool RememberVolume { get => _service.Current.RememberVolume; set => Update(s => s with { RememberVolume = value }); }
    public bool RememberRecentMedia { get => _service.Current.RememberRecentMedia; set => Update(s => s with { RememberRecentMedia = value }); }
    public bool RememberResumeHistory { get => _service.Current.RememberResumeHistory; set => Update(s => s with { RememberResumeHistory = value }); }
    public decimal ResumeMinimumElapsedSeconds { get => _service.Current.ResumeMinimumElapsedSeconds; set => Update(s => s with { ResumeMinimumElapsedSeconds = (int)value }); }
    public decimal ResumeMaximumRemainingSeconds { get => _service.Current.ResumeMaximumRemainingSeconds; set => Update(s => s with { ResumeMaximumRemainingSeconds = (int)value }); }
    public string Theme { get => _service.Current.Theme; set => Update(s => s with { Theme = value }); } public string Density { get => _service.Current.Density; set => Update(s => s with { Density = value }); }
    public decimal ControlTimeoutSeconds { get => _service.Current.ControlTimeoutSeconds; set => Update(s => s with { ControlTimeoutSeconds = (int)value }); }
    public bool ReduceMotion { get => _service.Current.ReduceMotion; set => Update(s => s with { ReduceMotion = value }); }
    public string DefaultSpeed { get => _service.Current.DefaultSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture); set => Update(s => s with { DefaultSpeed = double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : s.DefaultSpeed }); }
    public decimal SkipBackwardSeconds { get => _service.Current.SkipBackwardSeconds; set => Update(s => s with { SkipBackwardSeconds = (int)value }); } public decimal SkipForwardSeconds { get => _service.Current.SkipForwardSeconds; set => Update(s => s with { SkipForwardSeconds = (int)value }); }
    public IReadOnlyList<string> AudioOutputOptions { get; } = AudioOutputPolicy.Options();
    public string AudioOutputModule { get => AudioOutputPolicy.Effective(_service.Current.AudioOutputModule); set => Update(s => s with { AudioOutputModule = value }); }
    public decimal? NormalizationMaximumLevel { get => (decimal?)_service.Current.NormalizationMaximumLevel; set => Update(s => s with { NormalizationMaximumLevel = value is null ? null : (double)value }); }
    public bool? AudioPassthrough { get => _service.Current.AudioPassthrough; set => Update(s => s with { AudioPassthrough = value }); }
    public void Refresh() { foreach (string name in new[] { nameof(ResumePlayback), nameof(RememberVolume), nameof(RememberRecentMedia), nameof(RememberResumeHistory), nameof(ResumeMinimumElapsedSeconds), nameof(ResumeMaximumRemainingSeconds), nameof(Theme), nameof(Density), nameof(IsCompactDensity), nameof(ControlTimeoutSeconds), nameof(ReduceMotion), nameof(DefaultSpeed), nameof(SkipBackwardSeconds), nameof(SkipForwardSeconds), nameof(AudioOutputModule), nameof(NormalizationMaximumLevel), nameof(AudioPassthrough) }) Notify(name); }
    private void Update(Func<PlayerSettings, PlayerSettings> update) { try { _service.Update(update); ErrorMessage = null; Refresh(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorMessage = "Settings could not be saved. Your previous settings are still active."; } }
    private void ResetPage() { if (IsHotkeys) { ResetAllShortcuts(); return; } Update(s => IsGeneral ? s with { ResumePlayback = PlayerSettings.Default.ResumePlayback, RememberVolume = PlayerSettings.Default.RememberVolume, RememberRecentMedia = PlayerSettings.Default.RememberRecentMedia, RememberResumeHistory = PlayerSettings.Default.RememberResumeHistory, ResumeMinimumElapsedSeconds = PlayerSettings.Default.ResumeMinimumElapsedSeconds, ResumeMaximumRemainingSeconds = PlayerSettings.Default.ResumeMaximumRemainingSeconds } : IsAppearance ? s with { Theme = PlayerSettings.Default.Theme, Density = PlayerSettings.Default.Density, ControlTimeoutSeconds = PlayerSettings.Default.ControlTimeoutSeconds, ReduceMotion = PlayerSettings.Default.ReduceMotion } : IsPlayback ? s with { DefaultSpeed = PlayerSettings.Default.DefaultSpeed, SkipBackwardSeconds = PlayerSettings.Default.SkipBackwardSeconds, SkipForwardSeconds = PlayerSettings.Default.SkipForwardSeconds } : IsAudio ? s with { AudioOutputModule = PlayerSettings.Default.AudioOutputModule, NormalizationMaximumLevel = null, AudioPassthrough = null } : s); }
    private void ConfirmOrResetAll() { if (!ConfirmReset) { ConfirmReset = true; return; } try { _service.ResetAll(); ErrorMessage = null; ConfirmReset = false; Refresh(); RefreshShortcutRows(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorMessage = "Settings could not be reset because they could not be saved."; } }
    private void RefreshShortcutRows() { if (_registry is null) return; foreach (EditablePlayerShortcut value in new PlayerShortcutEditor(_service, _registry).Items()) { if (_shortcutRows.TryGetValue(value.Command, out ShortcutRow? row)) row.Refresh(value); else _shortcutRows.Add(value.Command, new ShortcutRow(value, shortcut => SaveShortcut(shortcut), shortcut => ResetShortcut(shortcut.Command))); } Notify(nameof(EditableShortcutRows)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name!); return true; } private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

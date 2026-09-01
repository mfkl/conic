using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;
internal sealed record SettingsPage(string Name, string Keywords);
internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerSettingsService _service; private string _search = string.Empty; private SettingsPage _selectedPage; private bool _confirmReset; private string? _errorMessage;
    public SettingsViewModel(PlayerSettingsService service) { _service = service; _selectedPage = Pages[0]; ResetPageCommand = new RelayCommand(_ => ResetPage()); ResetAllCommand = new RelayCommand(_ => ConfirmOrResetAll()); CancelResetCommand = new RelayCommand(_ => ConfirmReset = false); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SettingsPage> Pages { get; } = [new("General", "resume volume privacy"), new("Appearance", "theme density controls timeout"), new("Playback", "speed skip backward forward")];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Dark"]; public IReadOnlyList<string> DensityOptions { get; } = ["Comfortable", "Compact"]; public IReadOnlyList<string> SpeedOptions { get; } = ["0.5", "1", "1.5", "2"];
    public IEnumerable<SettingsPage> FilteredPages => Pages.Where(page => string.IsNullOrWhiteSpace(Search) || (page.Name + " " + page.Keywords).Contains(Search, StringComparison.OrdinalIgnoreCase));
    public SettingsPage SelectedPage { get => _selectedPage; set { if (Set(ref _selectedPage, value)) { ConfirmReset = false; Notify(nameof(IsGeneral)); Notify(nameof(IsAppearance)); Notify(nameof(IsPlayback)); } } }
    public bool IsGeneral => SelectedPage.Name == "General"; public bool IsAppearance => SelectedPage.Name == "Appearance"; public bool IsPlayback => SelectedPage.Name == "Playback";
    public string Search { get => _search; set { if (Set(ref _search, value)) { Notify(nameof(FilteredPages)); SettingsPage? match = FilteredPages.FirstOrDefault(); if (match is not null) SelectedPage = match; } } }
    public bool ConfirmReset { get => _confirmReset; private set => Set(ref _confirmReset, value); } public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public ICommand ResetPageCommand { get; } public ICommand ResetAllCommand { get; } public ICommand CancelResetCommand { get; }
    public bool ResumePlayback { get => _service.Current.ResumePlayback; set => Update(s => s with { ResumePlayback = value }); } public bool RememberVolume { get => _service.Current.RememberVolume; set => Update(s => s with { RememberVolume = value }); }
    public string Theme { get => _service.Current.Theme; set => Update(s => s with { Theme = value }); } public string Density { get => _service.Current.Density; set => Update(s => s with { Density = value }); }
    public decimal ControlTimeoutSeconds { get => _service.Current.ControlTimeoutSeconds; set => Update(s => s with { ControlTimeoutSeconds = (int)value }); }
    public string DefaultSpeed { get => _service.Current.DefaultSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture); set => Update(s => s with { DefaultSpeed = double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : s.DefaultSpeed }); }
    public decimal SkipBackwardSeconds { get => _service.Current.SkipBackwardSeconds; set => Update(s => s with { SkipBackwardSeconds = (int)value }); } public decimal SkipForwardSeconds { get => _service.Current.SkipForwardSeconds; set => Update(s => s with { SkipForwardSeconds = (int)value }); }
    public void Refresh() { foreach (string name in new[] { nameof(ResumePlayback), nameof(RememberVolume), nameof(Theme), nameof(Density), nameof(ControlTimeoutSeconds), nameof(DefaultSpeed), nameof(SkipBackwardSeconds), nameof(SkipForwardSeconds) }) Notify(name); }
    private void Update(Func<PlayerSettings, PlayerSettings> update) { try { _service.Update(update); ErrorMessage = null; Refresh(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorMessage = "Settings could not be saved. Your previous settings are still active."; } }
    private void ResetPage() => Update(s => IsGeneral ? s with { ResumePlayback = PlayerSettings.Default.ResumePlayback, RememberVolume = PlayerSettings.Default.RememberVolume } : IsAppearance ? s with { Theme = PlayerSettings.Default.Theme, Density = PlayerSettings.Default.Density, ControlTimeoutSeconds = PlayerSettings.Default.ControlTimeoutSeconds } : s with { DefaultSpeed = PlayerSettings.Default.DefaultSpeed, SkipBackwardSeconds = PlayerSettings.Default.SkipBackwardSeconds, SkipForwardSeconds = PlayerSettings.Default.SkipForwardSeconds });
    private void ConfirmOrResetAll() { if (!ConfirmReset) { ConfirmReset = true; return; } try { _service.ResetAll(); ErrorMessage = null; ConfirmReset = false; Refresh(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ErrorMessage = "Settings could not be reset because they could not be saved."; } }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name!); return true; } private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

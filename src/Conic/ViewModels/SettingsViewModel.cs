using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Conic.Commands;

namespace Conic.ViewModels;

internal sealed record SettingsPage(string Name, string Description);

internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PlayerSettingsService _service;
    private string _search = string.Empty;
    private SettingsPage _selectedPage;
    private bool _confirmReset;
    private string? _errorMessage;

    public SettingsViewModel(PlayerSettingsService service)
    {
        _service = service;
        _selectedPage = Pages[0];
        ResetPageCommand = new RelayCommand(_ => ResetPage());
        ResetAllCommand = new RelayCommand(_ => ConfirmOrResetAll());
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SettingsPage> Pages { get; } = [new("General", "Startup and privacy"), new("Appearance", "Theme and player chrome"), new("Playback", "Speed and seeking")];
    public IEnumerable<SettingsPage> FilteredPages => Pages.Where(page => string.IsNullOrWhiteSpace(Search) || (page.Name + " " + page.Description).Contains(Search, StringComparison.OrdinalIgnoreCase));
    public SettingsPage SelectedPage { get => _selectedPage; set => Set(ref _selectedPage, value); }
    public string Search { get => _search; set { if (Set(ref _search, value)) Notify(nameof(FilteredPages)); } }
    public bool ConfirmReset { get => _confirmReset; private set => Set(ref _confirmReset, value); }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public ICommand ResetPageCommand { get; }
    public ICommand ResetAllCommand { get; }
    public PlayerSettings Current => _service.Current;
    public string LabelFor(string key) => PlayerSettingsService.ApplyMode(key) switch { SettingApplyMode.Live => "Live", SettingApplyMode.NextOpen => "Next open", _ => "Restart" };
    public void SetTheme(string theme) => Update(s => s with { Theme = theme });
    public void SetDensity(string density) => Update(s => s with { Density = density });
    public void SetTimeout(double timeout) => Update(s => s with { ControlTimeoutSeconds = (int)timeout });
    public void SetResume(bool value) => Update(s => s with { ResumePlayback = value });
    public void SetRememberVolume(bool value) => Update(s => s with { RememberVolume = value });
    public void SetDefaultSpeed(string value) => Update(s => s with { DefaultSpeed = double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double speed) ? speed : s.DefaultSpeed });
    public void SetBackwardSkip(string value) => Update(s => s with { SkipBackwardSeconds = int.TryParse(value, out int seconds) ? seconds : s.SkipBackwardSeconds });
    public void SetForwardSkip(string value) => Update(s => s with { SkipForwardSeconds = int.TryParse(value, out int seconds) ? seconds : s.SkipForwardSeconds });
    public void Refresh() { Notify(nameof(Current)); Notify(nameof(FilteredPages)); }
    private void Update(Func<PlayerSettings, PlayerSettings> update)
    {
        try { _service.Update(update); ErrorMessage = null; Refresh(); }
        catch (IOException) { ErrorMessage = "Settings could not be saved. Your previous settings are still active."; }
        catch (UnauthorizedAccessException) { ErrorMessage = "Settings could not be saved. Check the application data folder permissions."; }
    }
    private void ResetPage()
    {
        Update(current => SelectedPage.Name switch
        {
            "General" => current with { ResumePlayback = PlayerSettings.Default.ResumePlayback, RememberVolume = PlayerSettings.Default.RememberVolume },
            "Appearance" => current with { Theme = PlayerSettings.Default.Theme, Density = PlayerSettings.Default.Density, ControlTimeoutSeconds = PlayerSettings.Default.ControlTimeoutSeconds },
            _ => current with { DefaultSpeed = PlayerSettings.Default.DefaultSpeed, SkipBackwardSeconds = PlayerSettings.Default.SkipBackwardSeconds, SkipForwardSeconds = PlayerSettings.Default.SkipForwardSeconds },
        });
    }
    private void ConfirmOrResetAll() { if (!ConfirmReset) { ConfirmReset = true; return; } Update(_ => PlayerSettings.Default); ConfirmReset = false; }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

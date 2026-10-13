using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed partial class SettingsViewModel
{
    public bool HasSettingsResults => FilteredPages.Any();
    public bool HasShortcutResults => EditableShortcutRows.Count != 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsCompactDensity => Density == "Compact";
    public bool CanResetPage => IsGeneral || IsInterface || IsPrivacy || IsControls || IsPlayback || IsVideo || IsAudio;

    public ICommand OpenAudioControlsCommand { get; private set; } = null!;
    public ICommand OpenSubtitleControlsCommand { get; private set; } = null!;
    public ICommand OpenVideoControlsCommand { get; private set; } = null!;
    public ICommand OpenMediaInformationCommand { get; private set; } = null!;

    private void InitializeNavigationCommands()
    {
        OpenAudioControlsCommand = Route(PlayerCommandId.ToggleMediaControls, "Audio");
        OpenSubtitleControlsCommand = Route(PlayerCommandId.ToggleMediaControls, "Subtitles");
        OpenVideoControlsCommand = Route(PlayerCommandId.ToggleMediaControls, "Video");
        OpenMediaInformationCommand = Route(PlayerCommandId.MediaInformation);
    }

    private ICommand Route(PlayerCommandId command, object? tab = null) => new RegistryCommand(_registry ?? new PlayerCommandRegistry(), command, tab);
}

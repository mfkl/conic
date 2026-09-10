using System.Windows.Input;
using Player.Application;
using Conic.Commands;

namespace Conic.ViewModels;

internal sealed partial class SettingsViewModel
{
    public bool HasSettingsResults => FilteredPages.Any();
    public bool HasShortcutResults => EditableShortcutRows.Count != 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsCompactDensity => Density == "Compact";
    public bool CanResetPage => IsGeneral || IsAppearance || IsPlayback || IsAudio || IsHotkeys;
    public string PageSummary => SelectedPage.Name switch
    {
        "General" => "Choose what VLC remembers. Changes are saved automatically.",
        "Appearance" => "Adjust the interface and playback controls. Changes apply immediately.",
        "Playback" => "Set the speed for newly opened media and your skip intervals.",
        "Audio" => "Restart VLC after changing audio output, normalization or passthrough.",
        "Video" => "Video playback capabilities in this version of Conic.",
        "Subtitles" => "Choose subtitle tracks and keep them in sync with your media.",
        "Hotkeys" => "Make the keyboard controls your own.",
        _ => "Media information and playback diagnostics."
    };

    public ICommand OpenAudioControlsCommand { get; private set; } = null!;
    public ICommand OpenSubtitleControlsCommand { get; private set; } = null!;
    public ICommand OpenMediaInformationCommand { get; private set; } = null!;

    private void InitializeNavigationCommands()
    {
        OpenAudioControlsCommand = Route(PlayerCommandId.ToggleMediaControls);
        OpenSubtitleControlsCommand = Route(PlayerCommandId.ToggleMediaControls);
        OpenMediaInformationCommand = Route(PlayerCommandId.MediaInformation);
    }

    private ICommand Route(PlayerCommandId command) => new RegistryCommand(_registry ?? new PlayerCommandRegistry(), command);
}

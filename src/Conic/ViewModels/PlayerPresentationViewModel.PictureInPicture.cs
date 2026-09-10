using System.Windows.Input;
using Player.Application;
using Conic.Commands;

namespace Conic.ViewModels;

internal sealed partial class PlayerPresentationViewModel
{
    private bool _isPictureInPicture;
    public event EventHandler? PictureInPictureRequested;
    public event EventHandler? MainLayoutRequested;
    public ICommand TogglePictureInPictureCommand { get; private set; } = null!;

    public bool IsPictureInPicture
    {
        get => _isPictureInPicture;
        internal set
        {
            if (!Set(ref _isPictureInPicture, value)) return;
            OnPropertyChanged(nameof(PictureInPictureLabel));
            (TogglePictureInPictureCommand as RegistryCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string PictureInPictureLabel => IsPictureInPicture ? "Return from picture in picture" : "Picture in picture";

    private void InitializePictureInPictureCommands(PlayerCommandRegistry registry)
    {
        registry.Register(new(PlayerCommandId.TogglePictureInPicture, "Picture in picture", "Window", "Ctrl+Shift+P"),
            _ => PictureInPictureRequested?.Invoke(this, EventArgs.Empty),
            _ => IsPictureInPicture || CanTogglePlayback);
        TogglePictureInPictureCommand = new RegistryCommand(registry, PlayerCommandId.TogglePictureInPicture);
    }

    private void RequestMainLayout() => MainLayoutRequested?.Invoke(this, EventArgs.Empty);
}

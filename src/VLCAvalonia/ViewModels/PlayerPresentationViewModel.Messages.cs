using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed partial class PlayerPresentationViewModel
{
    /// <summary>The Messages window (VLC's Tools › Messages) is asked for; the window opens it.</summary>
    public event EventHandler? MessagesRequested;
    public ICommand ShowMessagesCommand { get; private set; } = null!;

    private void InitializeMessagesCommand(PlayerCommandRegistry registry)
    {
        // VLC uses Ctrl+M on Windows and Cmd+Shift+M on macOS, where Cmd+M minimizes.
        registry.Register(new(PlayerCommandId.ShowMessages, "Messages", "Window", "Ctrl+Shift+M"),
            _ => MessagesRequested?.Invoke(this, EventArgs.Empty));
        ShowMessagesCommand = new RegistryCommand(registry, PlayerCommandId.ShowMessages);
    }
}

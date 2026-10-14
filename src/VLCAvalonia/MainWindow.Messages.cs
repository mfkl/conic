using Avalonia.Controls;
using VLCAvalonia.Interop;
using VLCAvalonia.ViewModels;
using VLCAvalonia.Views;

namespace VLCAvalonia;

// Messages (VLC's Tools › Messages): one window beside the player, closed
// with it so it never keeps the app running.
public partial class MainWindow
{
    private MessagesWindow? _messagesWindow;

    private void InitializeMessages()
    {
        _presentation.MessagesRequested += (_, _) => ShowMessages();
        Closed += (_, _) => _messagesWindow?.Close();
    }

    internal MessagesWindow ShowMessages()
    {
        if (_messagesWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }
        MessagesWindow window = new(new MessagesViewModel(VlcLog.Messages));
        window.Closed += (_, _) => { if (ReferenceEquals(_messagesWindow, window)) _messagesWindow = null; };
        _messagesWindow = window;
        // Not owned: like VLC's, it may sit behind the player or on another display.
        window.Show();
        return window;
    }
}

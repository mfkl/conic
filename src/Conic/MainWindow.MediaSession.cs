using Conic.Interop.Windows;
using Conic.Services;

namespace Conic;

public partial class MainWindow
{
    private MediaSession? _mediaSession;

    private void InitializeMediaSession()
    {
        _mediaSession = new MediaSession(_presentation);
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { Handle: not 0 } handle)
        {
            _mediaSession.Attach(SystemMediaControls.Create(handle.Handle, _mediaSession));
            _mediaSession.Attach(TaskbarButtons.Create(this, _mediaSession));
        }
        Closed += (_, _) => { _mediaSession?.Dispose(); _mediaSession = null; };
    }
}

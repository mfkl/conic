using Avalonia.Controls;
using Conic.Interop.Linux;
using Conic.Interop.MacOS;
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
        if (OperatingSystem.IsMacOS()) _mediaSession.Attach(MacNowPlaying.Create(_mediaSession));
        if (OperatingSystem.IsLinux()) _ = AttachMprisAsync(_mediaSession);
        Closed += (_, _) => { _mediaSession?.Dispose(); _mediaSession = null; };
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task AttachMprisAsync(MediaSession session)
    {
        MprisService? mpris = await MprisService.StartAsync(session, RaiseFromMediaSession);
        if (mpris is null) return;
        if (_mediaSession != session) { mpris.Dispose(); return; }
        session.Attach(mpris);
    }

    private void RaiseFromMediaSession()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}

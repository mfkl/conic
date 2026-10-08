using Avalonia.Controls;
using Conic.Interop;
using Conic.Video.MacOS;
using Conic.Video.Windows;

namespace Conic.Video;

internal static class VideoBackendFactory
{
    public static void InitializeRuntime()
    {
        if (NativePlaybackSupport.IsAvailable) VlcRuntime.Initialize();
    }

    public static IVideoBackend Create(Control host, VideoBackendOptions options)
    {
        NativePlaybackSupport.RequireAvailable();
        return OperatingSystem.IsMacOS()
            ? new IOSurfaceVideoBackend(host, options)
            : new D3D11VideoBackend(host, options);
    }
}

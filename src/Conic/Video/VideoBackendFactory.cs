using Avalonia.Controls;
using Conic.Interop;
using Conic.Video.Linux;
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
        if (OperatingSystem.IsMacOS())
            return new IOSurfaceVideoBackend(host, options);
        if (OperatingSystem.IsLinux())
            return new DmaBufVideoBackend(host, options);
        return new D3D11VideoBackend(host, options);
    }
}

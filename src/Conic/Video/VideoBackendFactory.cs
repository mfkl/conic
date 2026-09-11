using Avalonia.Controls;
using Conic.Interop;
using Conic.Interop.Windows;
using Conic.Video.Windows;

namespace Conic.Video;

internal static class VideoBackendFactory
{
    public static void InitializeRuntime()
    {
        if (NativePlaybackSupport.IsAvailable) WindowsVlcRuntime.Initialize();
    }

    public static IVideoBackend Create(Control host, VideoBackendOptions options)
    {
        NativePlaybackSupport.RequireAvailable();
        return new D3D11VideoBackend(host, options);
    }
}

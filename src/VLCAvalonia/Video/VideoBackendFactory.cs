using Avalonia.Controls;
using VLCAvalonia.Interop;
using VLCAvalonia.Video.Windows;

namespace VLCAvalonia.Video;

internal static class VideoBackendFactory
{
    public static void InitializeRuntime()
    {
        if (NativePlaybackSupport.IsAvailable) VlcRuntime.Initialize();
    }

    public static IVideoBackend Create(Control host, VideoBackendOptions options)
    {
        NativePlaybackSupport.RequireAvailable();
        return new D3D11VideoBackend(host, options);
    }
}

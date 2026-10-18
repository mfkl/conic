using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Player.Contracts;
using Conic.Interop;

namespace Conic.Video;

/// <summary>
/// One attachment's native video transport and compositor resources. Calls and
/// notifications run on the UI thread. Native handles stay inside the backend.
/// InitializeAsync must finish device selection before CreatePlayer is called.
/// </summary>
internal interface IVideoBackend : IDisposable
{
    event Action<string>? StatusChanged;
    event Action<ulong, ulong, string>? GpuFramePresented;
    /// <summary>Raised when the subtitle or the video placement changes.</summary>
    event Action? PresentationChanged;
    VideoGeometry Geometry { get; }
    VideoSubtitle? Subtitle { get; }
    Size VideoSize { get; }
    bool HasCpuFrame { get; }
    bool HasGpuFrame { get; }
    /// <summary>The drawing surface GPU frames are presented into, for showing the
    /// video a second time; null while no GPU frame is on it.</summary>
    SharedVideoSurface? SharedSurface { get; }
    Task InitializeAsync();
    IMediaEngine CreatePlayer();
    Task PresentLatestFrameAsync();
    void UpdateLayout();
    void Render(DrawingContext context);
}

/// <summary>Subtitle overlay at Bounds on a canvas covering the visible video.</summary>
internal sealed record VideoSubtitle(Bitmap Image, SubtitleBounds Bounds, int CanvasWidth, int CanvasHeight);

/// <summary>A backend's drawing surface, its size in pixels and where the picture sits on it.</summary>
internal sealed record SharedVideoSurface(CompositionDrawingSurface Surface, Size SurfaceSize, Rect Picture);

internal readonly record struct VideoBackendOptions(
    bool ForceUnavailableAdapter = false,
    bool ProbeSharedTexture = false);

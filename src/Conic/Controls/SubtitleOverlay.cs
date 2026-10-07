using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Conic.Interop;

namespace Conic.Controls;

/// <summary>
/// Draws VLC's rendered subtitles over a <see cref="CompositionVideoSurface"/>.
/// GPU video is a composition child visual, so subtitles need a sibling layered
/// above it, sharing the surface's bounds.
/// </summary>
public sealed class SubtitleOverlay : Control
{
    private CompositionVideoSurface? _surface;

    public SubtitleOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    internal CompositionVideoSurface? Surface
    {
        get => _surface;
        set
        {
            if (_surface is not null) _surface.PresentationChanged -= InvalidateVisual;
            _surface = value;
            if (_surface is not null) _surface.PresentationChanged += InvalidateVisual;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_surface?.Subtitle is not { } subtitle) return;
        Rect video = _surface.CurrentVideoLayout.Destination;
        if (video.Width <= 0 || video.Height <= 0) return;
        double scaleX = video.Width / subtitle.CanvasWidth, scaleY = video.Height / subtitle.CanvasHeight;
        SubtitleBounds bounds = subtitle.Bounds;
        context.DrawImage(subtitle.Image, new Rect(video.X + bounds.X * scaleX, video.Y + bounds.Y * scaleY, bounds.Width * scaleX, bounds.Height * scaleY));
    }
}

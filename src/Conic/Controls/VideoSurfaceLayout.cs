using Avalonia;
using Conic.Interop;

namespace Conic.Controls;

internal readonly record struct VideoSurfaceLayout(Rect Source, Rect Destination, Rect FullImage)
{
    public static VideoSurfaceLayout Calculate(Size viewport, Size pixels, VideoGeometry geometry, double displayScale = 1)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return default;
        if (geometry.Active == 0) geometry = new() { Width = 1, Height = 1, SampleAspect = 1, AutoScale = 1, Zoom = 1 };
        double sar = geometry.SampleAspect > 0 && double.IsFinite(geometry.SampleAspect) ? geometry.SampleAspect : 1;
        Rect source = new Rect(geometry.X * pixels.Width, geometry.Y * pixels.Height, geometry.Width * pixels.Width, geometry.Height * pixels.Height).Intersect(new Rect(pixels));
        if (source.Width <= 0 || source.Height <= 0) return default;
        double scale = geometry.AutoScale != 0
            ? Math.Min(viewport.Width / (source.Width * sar), viewport.Height / source.Height)
            : geometry.Zoom / displayScale;
        if (!double.IsFinite(scale) || scale <= 0) return default;
        Size size = new(source.Width * sar * scale, source.Height * scale);
        return new(source, new Rect((viewport.Width - size.Width) / 2, (viewport.Height - size.Height) / 2, size.Width, size.Height),
            new Rect(-source.X * sar * scale, -source.Y * scale, pixels.Width * sar * scale, pixels.Height * scale));
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Conic.Interop;
using Conic.Video;

namespace Conic.Controls;

/// <summary>
/// Shows a <see cref="CompositionVideoSurface"/>'s GPU video a second time,
/// fitted to this control, through another surface visual on the same drawing
/// surface: no second decode, no copy. Empty on the CPU path and until the
/// first GPU frame; <see cref="HasVideo"/> says when it shows a picture.
/// </summary>
public sealed class VideoMirror : Control
{
    public static readonly DirectProperty<VideoMirror, bool> HasVideoProperty =
        AvaloniaProperty.RegisterDirect<VideoMirror, bool>(nameof(HasVideo), mirror => mirror.HasVideo);

    private CompositionVideoSurface? _source;
    private CompositionContainerVisual? _clip;
    private CompositionSurfaceVisual? _visual;
    private bool _attached;
    private bool _hasVideo;

    public VideoMirror()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public bool HasVideo
    {
        get => _hasVideo;
        private set => SetAndRaise(HasVideoProperty, ref _hasVideo, value);
    }

    internal CompositionVideoSurface? Source
    {
        get => _source;
        set
        {
            if (_source is not null) _source.SharedSurfaceChanged -= Update;
            _source = value;
            if (_source is not null) _source.SharedSurfaceChanged += Update;
            Update();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ElementComposition.SetElementChildVisual(this, null);
        _clip = null;
        _visual = null;
        HasVideo = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Update();
    }

    private void Update()
    {
        SharedVideoSurface? shared = _attached ? _source?.SharedSurface : null;
        VideoGeometry geometry = _source?.GeometryForSmoke ?? default;
        VideoSurfaceLayout layout = default;
        if (shared is not null && geometry.Active != 0)
        {
            // Always fit the card, whatever zoom the player uses.
            geometry.AutoScale = 1;
            layout = VideoSurfaceLayout.Calculate(Bounds.Size, shared.Picture.Size, geometry, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        }
        if (shared is null || layout.Destination.Width <= 0 || layout.Destination.Height <= 0 || !EnsureVisuals())
        {
            if (_clip is not null) _clip.Visible = false;
            if (_visual is not null) _visual.Surface = null;
            HasVideo = false;
            return;
        }
        // Place the whole surface so the picture lands on the full-image
        // rectangle; the clip keeps only the cropped, fitted part.
        double scaleX = layout.FullImage.Width / shared.Picture.Width;
        double scaleY = layout.FullImage.Height / shared.Picture.Height;
        _visual!.Surface = shared.Surface;
        _visual.Size = new Vector(shared.SurfaceSize.Width * scaleX, shared.SurfaceSize.Height * scaleY);
        _visual.Offset = new Vector3D(layout.FullImage.X - shared.Picture.X * scaleX, layout.FullImage.Y - shared.Picture.Y * scaleY, 0);
        _clip!.Size = new Vector(layout.Destination.Width, layout.Destination.Height);
        _clip.Offset = new Vector3D(layout.Destination.X, layout.Destination.Y, 0);
        _clip.Visible = true;
        HasVideo = true;
    }

    private bool EnsureVisuals()
    {
        if (_clip is not null) return true;
        if (ElementComposition.GetElementVisual(this) is not { } element) return false;
        _visual = element.Compositor.CreateSurfaceVisual();
        _clip = element.Compositor.CreateContainerVisual();
        _clip.ClipToBounds = true;
        _clip.Children.Add(_visual);
        ElementComposition.SetElementChildVisual(this, _clip);
        return true;
    }
}

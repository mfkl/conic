using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Conic.Controls;

// One drawing layer of a 24x24 design icon: a geometry that is filled,
// stroked or both. Colors are not part of the data; VlcIcon supplies them.
public sealed class IconLayer
{
    public Geometry? Data { get; set; }
    public bool Fill { get; set; }
    // In 24x24 artwork units; 0 means no stroke.
    public double StrokeThickness { get; set; }
    public PenLineCap LineCap { get; set; } = PenLineCap.Round;
    public PenLineJoin LineJoin { get; set; } = PenLineJoin.Round;
}

// A design icon (Styles/Icons.axaml, keys "Icon.*"), drawn bottom layer first.
public sealed class IconData
{
    [Content]
    public List<IconLayer> Layers { get; } = [];
}

// Draws an IconData in its Foreground, scaled uniformly from the 24x24
// artwork to the control's size (strokes scale with it) and centered.
// Foreground inherits like text, so a Button's styles color the icon.
public sealed class VlcIcon : Control
{
    public const double ArtworkSize = 24;

    public static readonly StyledProperty<IconData?> IconProperty =
        AvaloniaProperty.Register<VlcIcon, IconData?>(nameof(Icon));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<VlcIcon>();

    static VlcIcon()
    {
        AffectsRender<VlcIcon>(IconProperty, ForegroundProperty);
    }

    public IconData? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(ArtworkSize, availableSize.Width), Math.Min(ArtworkSize, availableSize.Height));

    public override void Render(DrawingContext context)
    {
        if (Icon is not { } icon || Foreground is not { } brush) return;
        Size size = Bounds.Size;
        double scale = Math.Min(size.Width, size.Height) / ArtworkSize;
        if (scale <= 0) return;
        Matrix transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(
            (size.Width - ArtworkSize * scale) / 2, (size.Height - ArtworkSize * scale) / 2);
        using (context.PushTransform(transform))
        {
            foreach (IconLayer layer in icon.Layers)
            {
                if (layer.Data is null) continue;
                Pen? pen = layer.StrokeThickness > 0
                    ? new Pen(brush, layer.StrokeThickness, lineCap: layer.LineCap, lineJoin: layer.LineJoin, miterLimit: 4)
                    : null;
                context.DrawGeometry(layer.Fill ? brush : null, pen, layer.Data);
            }
        }
    }
}

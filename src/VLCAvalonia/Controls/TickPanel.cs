using Avalonia;
using Avalonia.Controls;

namespace VLCAvalonia.Controls;

/// <summary>
/// Spreads its children evenly along a slider: child i of n is centered at
/// <see cref="Inset"/> + i/(n-1) of the remaining width, where the inset is
/// where the slider's thumb center stops at each end. Children stay inside
/// the panel (the first and last labels align to the edges when they would
/// overflow). Used for the speed slider's tick marks and labels.
/// </summary>
public sealed class TickPanel : Panel
{
    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<TickPanel, double>(nameof(Inset));

    static TickPanel() => AffectsArrange<TickPanel>(InsetProperty);

    public double Inset
    {
        get => GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        foreach (Control child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += child.DesiredSize.Width;
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = Children.Count;
        double span = Math.Max(0, finalSize.Width - 2 * Inset);
        for (int index = 0; index < count; index++)
        {
            Control child = Children[index];
            double center = Inset + (count == 1 ? span / 2 : span * index / (count - 1));
            double width = Math.Min(child.DesiredSize.Width, finalSize.Width);
            double left = Math.Clamp(center - width / 2, 0, Math.Max(0, finalSize.Width - width));
            child.Arrange(new Rect(left, 0, width, finalSize.Height));
        }
        return finalSize;
    }
}

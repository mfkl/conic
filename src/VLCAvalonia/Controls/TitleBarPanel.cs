using Avalonia;
using Avalonia.Controls;

namespace VLCAvalonia.Controls;

/// <summary>
/// The design's title bar row: the first child sits at the left, the third at
/// the right, and the second (the window title) is centered on the whole bar
/// when it fits, otherwise centered in the space left between the two groups.
/// The title gets only that space, so a long title trims instead of
/// overlapping the buttons.
/// </summary>
public sealed class TitleBarPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TitleBarPanel, double>(nameof(Spacing), 10);

    static TitleBarPanel() => AffectsMeasure<TitleBarPanel>(SpacingProperty);

    /// <summary>Minimum gap between the title and either group.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private Control? Left => Children.Count > 0 ? Children[0] : null;
    private Control? Center => Children.Count > 1 ? Children[1] : null;
    private Control? Right => Children.Count > 2 ? Children[2] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(double.PositiveInfinity, availableSize.Height);
        Left?.Measure(unbounded);
        Right?.Measure(unbounded);
        double left = Left?.DesiredSize.Width ?? 0;
        double right = Right?.DesiredSize.Width ?? 0;
        double room = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - left - right - 2 * Spacing);
        Center?.Measure(new Size(room, availableSize.Height));
        double height = Math.Max(Math.Max(Left?.DesiredSize.Height ?? 0, Right?.DesiredSize.Height ?? 0), Center?.DesiredSize.Height ?? 0);
        double width = left + right + (Center?.DesiredSize.Width ?? 0) + 2 * Spacing;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double left = Left?.DesiredSize.Width ?? 0;
        double right = Right?.DesiredSize.Width ?? 0;
        Left?.Arrange(new Rect(0, 0, left, finalSize.Height));
        Right?.Arrange(new Rect(Math.Max(0, finalSize.Width - right), 0, right, finalSize.Height));
        if (Center is { } center)
        {
            double start = left + Spacing;
            double end = finalSize.Width - right - Spacing;
            double width = Math.Max(0, Math.Min(center.DesiredSize.Width, end - start));
            double x = Math.Clamp((finalSize.Width - width) / 2, start, Math.Max(start, end - width));
            center.Arrange(new Rect(x, 0, width, finalSize.Height));
        }
        return finalSize;
    }
}

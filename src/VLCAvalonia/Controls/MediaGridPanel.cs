using Avalonia;
using Avalonia.Controls;

namespace VLCAvalonia.Controls;

// A wrapping grid whose items share one width between ItemMinWidth and
// ItemMaxWidth: it uses the fewest columns that keep items at or below the
// maximum, and drops columns when items would get narrower than the minimum.
// The design's video grid is 5 columns of 316 with a 20 gap at 1920 wide.
public sealed class MediaGridPanel : Panel
{
    public const double CardMinWidth = 180;
    public const double CardMaxWidth = 316;
    public const double RowMinWidth = 260;
    public const double RowMaxWidth = 420;
    public const double DefaultSpacing = 20;

    public static readonly StyledProperty<double> ItemMinWidthProperty =
        AvaloniaProperty.Register<MediaGridPanel, double>(nameof(ItemMinWidth), CardMinWidth);
    public static readonly StyledProperty<double> ItemMaxWidthProperty =
        AvaloniaProperty.Register<MediaGridPanel, double>(nameof(ItemMaxWidth), CardMaxWidth);
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MediaGridPanel, double>(nameof(ColumnSpacing), DefaultSpacing);
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<MediaGridPanel, double>(nameof(RowSpacing), DefaultSpacing);

    static MediaGridPanel() =>
        AffectsMeasure<MediaGridPanel>(ItemMinWidthProperty, ItemMaxWidthProperty, ColumnSpacingProperty, RowSpacingProperty);

    public double ItemMinWidth { get => GetValue(ItemMinWidthProperty); set => SetValue(ItemMinWidthProperty, value); }
    public double ItemMaxWidth { get => GetValue(ItemMaxWidthProperty); set => SetValue(ItemMaxWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }

    /// <summary>The number of columns for <paramref name="width"/>; at least one.</summary>
    public static int ColumnCount(double width, double min, double max, double spacing)
    {
        if (!double.IsFinite(width) || width <= 0) return 1;
        // A small tolerance keeps an exact fit (5 x 316 + 4 x 20 in 1663) at 5 columns.
        int columns = Math.Max(1, (int)Math.Ceiling((width + spacing) / (max + spacing) - 0.02));
        if (ItemWidth(width, columns, spacing) < min)
            columns = Math.Max(1, (int)Math.Floor((width + spacing) / (min + spacing)));
        return columns;
    }

    private static double ItemWidth(double width, int columns, double spacing) =>
        (width - spacing * (columns - 1)) / columns;

    private (int Columns, double Width) Layout(double available)
    {
        double min = ItemMinWidth, max = Math.Max(ItemMinWidth, ItemMaxWidth), spacing = ColumnSpacing;
        if (!double.IsFinite(available)) return (1, max);
        int columns = ColumnCount(available, min, max, spacing);
        return (columns, Math.Max(0, Math.Min(max, ItemWidth(available, columns, spacing))));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        (int columns, double itemWidth) = Layout(availableSize.Width);
        double height = 0, rowHeight = 0, widest = 0;
        int column = 0, rows = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            widest = Math.Max(widest, (column + 1) * itemWidth + column * ColumnSpacing);
            if (++column == columns) { height += rowHeight; rows++; rowHeight = 0; column = 0; }
        }
        if (column > 0) { height += rowHeight; rows++; }
        if (rows > 1) height += RowSpacing * (rows - 1);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : widest, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (int columns, double itemWidth) = Layout(finalSize.Width);
        List<Control> visible = Children.Where(c => c.IsVisible).ToList();
        double y = 0;
        for (int start = 0; start < visible.Count; start += columns)
        {
            IEnumerable<Control> row = visible.Skip(start).Take(columns);
            double rowHeight = row.Max(c => c.DesiredSize.Height);
            int column = 0;
            foreach (Control child in row)
                child.Arrange(new Rect(column++ * (itemWidth + ColumnSpacing), y, itemWidth, rowHeight));
            y += rowHeight + RowSpacing;
        }
        return finalSize;
    }
}

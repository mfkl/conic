using Avalonia;
using Avalonia.Controls;

namespace Conic.Controls;

// Takes the width it is given and sizes its child to Ratio (width / height),
// e.g. the 16:9 media card thumbnail.
public sealed class AspectRatioDecorator : Decorator
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioDecorator, double>(nameof(Ratio), 16d / 9);

    static AspectRatioDecorator() => AffectsMeasure<AspectRatioDecorator>(RatioProperty);

    public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }

    private Size Fit(Size available)
    {
        double ratio = Ratio > 0 ? Ratio : 16d / 9;
        double width = double.IsFinite(available.Width) ? available.Width : 0;
        return new Size(width, width / ratio);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size size = Fit(availableSize);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = Fit(finalSize);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

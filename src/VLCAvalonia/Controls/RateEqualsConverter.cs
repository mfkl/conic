using System.Globalization;
using Avalonia.Data.Converters;

namespace VLCAvalonia.Controls;

/// <summary>True when a playback rate equals the rate in the parameter ("1.5"); checks the current speed in menus.</summary>
public sealed class RateEqualsConverter : IValueConverter
{
    public static RateEqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double rate && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double expected)
            && Math.Abs(rate - expected) < .001;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

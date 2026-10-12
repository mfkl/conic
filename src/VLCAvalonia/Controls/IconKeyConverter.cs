using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace VLCAvalonia.Controls;

/// <summary>Looks up the design icon <c>Icon.{value}</c> from
/// Styles/Icons.axaml. With the parameter "Active" it prefers the
/// <c>Icon.{value}Active</c> variant and falls back to the plain icon.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static IconKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || Application.Current is not { } application) return null;
        if (parameter is "Active" &&
            application.TryGetResource($"Icon.{name}Active", application.ActualThemeVariant, out object? active))
            return active;
        return application.TryGetResource($"Icon.{name}", application.ActualThemeVariant, out object? icon) ? icon : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

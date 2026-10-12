using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Conic.ViewModels;

namespace Conic.Controls;

/// <summary>Picks the design icon for a <see cref="VolumeLevel"/>:
/// <c>Icon.VolumeMute</c>, <c>Icon.VolumeLow</c>, <c>Icon.VolumeMedium</c> or
/// <c>Icon.VolumeHigh</c> from Styles/Icons.axaml.</summary>
public sealed class VolumeIconConverter : IValueConverter
{
    public static VolumeIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is VolumeLevel level && Application.Current is { } application &&
        application.TryGetResource($"Icon.Volume{level}", application.ActualThemeVariant, out object? icon)
            ? icon
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using Avalonia;
using Avalonia.Controls;

namespace VLCAvalonia.Controls;

/// <summary>
/// Where the window caption buttons come from. The client area extends over
/// the decorations everywhere; on macOS the system draws its traffic lights
/// at the top left, elsewhere <see cref="WindowCaptionButtons"/> draws
/// Windows-style buttons at the top right.
/// </summary>
internal static class PlatformChrome
{
    public static bool UsesSystemCaptionButtons => OperatingSystem.IsMacOS();

    /// <summary>
    /// The caption row's content starts at least this far from the left edge,
    /// clear of the traffic lights.
    /// </summary>
    public const double SystemCaptionButtonsWidth = 78;

    /// <summary>Keeps the system caption buttons on macOS.</summary>
    public static void Apply(Window window)
    {
        if (UsesSystemCaptionButtons) window.WindowDecorations = WindowDecorations.Full;
    }

    /// <summary>Moves caption-row content clear of the system caption buttons.</summary>
    public static Thickness ClearOfCaptionButtons(Thickness thickness) =>
        UsesSystemCaptionButtons
            ? new Thickness(Math.Max(thickness.Left, SystemCaptionButtonsWidth), thickness.Top, thickness.Right, thickness.Bottom)
            : thickness;
}

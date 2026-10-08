using Avalonia;
using Avalonia.Controls;
using VLCAvalonia.Interop.MacOS;

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

    /// <summary>Where the first traffic light starts, as in AppKit's own tall title bars.</summary>
    public const double SystemCaptionButtonsLeft = 20;

    /// <summary>
    /// The caption row's content starts at least this far from the left edge,
    /// clear of the traffic lights.
    /// </summary>
    public const double SystemCaptionButtonsWidth = 92;

    /// <summary>Keeps the system caption buttons on macOS.</summary>
    public static void Apply(Window window)
    {
        if (UsesSystemCaptionButtons) window.WindowDecorations = WindowDecorations.Full;
    }

    /// <summary>
    /// Centers the traffic lights <paramref name="captionCenter"/> points below
    /// the top of the window. AppKit lays them out again on resize, full
    /// screen and activation, so this is applied after each of them.
    /// </summary>
    public static void PlaceCaptionButtons(Window window, double captionCenter)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        bool isView = handle.HandleDescriptor == "NSView";
        if (!isView && handle.HandleDescriptor != "NSWindow") return;
        MacWindowButtons.Apply(handle.Handle, isView, SystemCaptionButtonsLeft, captionCenter);
    }

    /// <summary>Moves caption-row content clear of the system caption buttons.</summary>
    public static Thickness ClearOfCaptionButtons(Thickness thickness) =>
        UsesSystemCaptionButtons
            ? new Thickness(Math.Max(thickness.Left, SystemCaptionButtonsWidth), thickness.Top, thickness.Right, thickness.Bottom)
            : thickness;
}

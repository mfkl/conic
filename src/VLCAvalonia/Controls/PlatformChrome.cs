using Avalonia;
using Avalonia.Controls;
using System.Runtime.Versioning;
using Avalonia.Platform;
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
    [SupportedOSPlatformGuard("macos")]
    public static bool UsesSystemCaptionButtons => OperatingSystem.IsMacOS();

    /// <summary>Where the first traffic light starts, as in AppKit's own tall title bars.</summary>
    public const double SystemCaptionButtonsLeft = 20;

    /// <summary>
    /// The caption row's content starts at least this far from the left edge,
    /// clear of the traffic lights.
    /// </summary>
    public const double SystemCaptionButtonsWidth = 92;

    /// <summary>Keeps the system caption buttons and uses the macOS app icon on macOS.</summary>
    public static void Apply(Window window)
    {
        if (!UsesSystemCaptionButtons) return;
        window.WindowDecorations = WindowDecorations.Full;
        // The bundle's Dock icon comes from AppIcon.icns; run from source,
        // the Dock needs it set explicitly. Both are the rounded-square icon.
        if (!AppContext.BaseDirectory.Contains(".app/Contents/", StringComparison.Ordinal))
        {
            using Stream icon = AssetLoader.Open(new Uri("avares://VLCAvalonia/Assets/vlc-avalonia-macos.png"));
            using MemoryStream bytes = new();
            icon.CopyTo(bytes);
            MacApplicationIcon.Set(bytes.ToArray());
        }
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

    /// <summary>Shows or hides the traffic lights along with the app's own caption row.</summary>
    public static void SetCaptionButtonsVisible(Window window, bool visible)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        bool isView = handle.HandleDescriptor == "NSView";
        if (!isView && handle.HandleDescriptor != "NSWindow") return;
        MacWindowButtons.SetVisible(handle.Handle, isView, visible);
    }

    /// <summary>Moves caption-row content clear of the system caption buttons.</summary>
    public static Thickness ClearOfCaptionButtons(Thickness thickness) =>
        UsesSystemCaptionButtons
            ? new Thickness(Math.Max(thickness.Left, SystemCaptionButtonsWidth), thickness.Top, thickness.Right, thickness.Bottom)
            : thickness;
}

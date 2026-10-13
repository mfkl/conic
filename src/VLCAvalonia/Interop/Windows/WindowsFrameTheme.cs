using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Styling;

namespace VLCAvalonia.Interop.Windows;

/// <summary>
/// Keeps the DWM frame in the app's theme on Windows 10. With the client area
/// extended over a border-only frame, DWM still paints the window's top pixel
/// row in the frame color, which is white unless the frame is in dark mode.
/// Avalonia 12 only sets the dark frame attribute on Windows 11, so a dark
/// window on Windows 10 shows a white line along its top edge.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsFrameTheme
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE; builds before 18985 used 19.
    private const int ImmersiveDarkMode = 20;
    private const int ImmersiveDarkModeBefore18985 = 19;

    /// <summary>Follows the window's theme on Windows 10 1809 to 22H2; Avalonia handles Windows 11.</summary>
    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        window.Opened += (_, _) => Update(window);
        window.ActualThemeVariantChanged += (_, _) => Update(window);
    }

    private static void Update(Window window)
    {
        if (window.TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        int dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
        int attribute = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985) ? ImmersiveDarkMode : ImmersiveDarkModeBefore18985;
        DwmSetWindowAttribute(handle.Handle, attribute, ref dark, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.Windows;

/// <summary>
/// Settings › Video › Stop the screensaver during playback. VLC's own
/// screensaver handling lives in its Windows video outputs, which the app
/// does not use, so the app asks Windows itself to keep the display on
/// (SetThreadExecutionState from the UI thread) while video plays.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class DisplayRequest
{
    private const uint Continuous = 0x80000000, SystemRequired = 0x1, DisplayRequired = 0x2;
    private static bool s_active;

    public static bool IsActive => s_active;

    /// <summary>Keeps the display on while <paramref name="active"/>; call on the UI thread.</summary>
    public static void Set(bool active)
    {
        if (s_active == active) return;
        s_active = active;
        SetThreadExecutionState(active ? Continuous | SystemRequired | DisplayRequired : Continuous);
    }

#if SMOKE
    /// <summary>The UI thread's execution state as Windows holds it (it re-applies the same state).</summary>
    internal static bool DisplayRequiredForSmoke()
    {
        uint state = s_active ? Continuous | SystemRequired | DisplayRequired : Continuous;
        uint previous = SetThreadExecutionState(state);
        return (previous & DisplayRequired) != 0;
    }
#endif

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}

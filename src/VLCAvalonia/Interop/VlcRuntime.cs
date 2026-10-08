using VLCAvalonia.Interop.Unix;
using VLCAvalonia.Interop.Windows;

namespace VLCAvalonia.Interop;

/// <summary>Loads the located VLC 3 runtime and makes it visible to VLCLR and the plugin.</summary>
internal static class VlcRuntime
{
    public static void Initialize()
    {
        NativeEngineSupport.RequireAvailable();
        if (OperatingSystem.IsWindows()) WindowsVlcRuntime.Initialize();
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) UnixVlcRuntime.Initialize();
    }
}

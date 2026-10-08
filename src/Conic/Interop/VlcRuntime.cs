using Conic.Interop.Linux;
using Conic.Interop.Windows;

namespace Conic.Interop;

/// <summary>Loads the located VLC 3 runtime and makes it visible to VLCLR and the plugin.</summary>
internal static class VlcRuntime
{
    public static void Initialize()
    {
        NativeEngineSupport.RequireAvailable();
        if (OperatingSystem.IsWindows()) WindowsVlcRuntime.Initialize();
        else if (OperatingSystem.IsLinux()) LinuxVlcRuntime.Initialize();
    }
}

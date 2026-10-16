using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop;

/// <summary>Where a GPU video backend exists: D3D11 on Windows x64, IOSurface on macOS, DMA-BUF on Linux x64.</summary>
internal static class NativePlaybackSupport
{
    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("linux")]
    public static bool IsAvailable => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux(),
        Architecture.Arm64 => OperatingSystem.IsMacOS(),
        _ => false,
    };

    public static string? UnavailableReason(VlcRuntimePlatform platform, Architecture architecture) =>
        (platform, architecture) switch
        {
            (VlcRuntimePlatform.Windows, Architecture.X64) => null,
            (VlcRuntimePlatform.MacOS, Architecture.X64 or Architecture.Arm64) => null,
            (VlcRuntimePlatform.Linux, Architecture.X64) => null,
            (VlcRuntimePlatform.Linux, _) => "Linux playback requires an x64 process.",
            (VlcRuntimePlatform.MacOS, _) => "macOS playback requires an arm64 or x64 process.",
            _ => "The Windows playback backend currently requires an x64 process.",
        };

    public static string? CurrentUnavailableReason => UnavailableReason(
        VlcRuntimeLocator.CurrentPlatform, RuntimeInformation.ProcessArchitecture);

    public static void RequireAvailable()
    {
        if (CurrentUnavailableReason is { } reason) throw new PlatformNotSupportedException(reason);
    }
}

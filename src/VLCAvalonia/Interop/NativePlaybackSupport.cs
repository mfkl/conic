using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop;

internal static class NativePlaybackSupport
{
    [SupportedOSPlatformGuard("windows")]
    public static bool IsAvailable => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static string? UnavailableReason(VlcRuntimePlatform platform, Architecture architecture) =>
        (platform, architecture) switch
        {
            (VlcRuntimePlatform.Windows, Architecture.X64) => null,
            (VlcRuntimePlatform.Linux, _) => "Linux hardware playback is not implemented yet.",
            (VlcRuntimePlatform.MacOS, _) => "macOS hardware playback is not implemented yet.",
            _ => "The Windows playback backend currently requires an x64 process.",
        };

    public static string? CurrentUnavailableReason => UnavailableReason(
        VlcRuntimeLocator.CurrentPlatform, RuntimeInformation.ProcessArchitecture);

    public static void RequireAvailable()
    {
        if (CurrentUnavailableReason is { } reason) throw new PlatformNotSupportedException(reason);
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace Conic.Interop;

/// <summary>
/// Where the VLC 3 engine (queue, transport, tracks and media information) runs.
/// This is broader than <see cref="NativePlaybackSupport"/>, which also requires
/// an implemented GPU video backend.
/// </summary>
internal static class NativeEngineSupport
{
    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    public static bool IsAvailable => VLCPlatform.IsSupported;

    public static string? UnavailableReason(VlcRuntimePlatform platform, Architecture architecture) =>
        (platform, architecture) switch
        {
            (VlcRuntimePlatform.Windows or VlcRuntimePlatform.Linux, Architecture.X64) => null,
            (VlcRuntimePlatform.MacOS, Architecture.X64 or Architecture.Arm64) => null,
            _ => "The VLC engine currently requires an x64 process, or macOS on Apple silicon.",
        };

    public static string? CurrentUnavailableReason => UnavailableReason(
        VlcRuntimeLocator.CurrentPlatform, RuntimeInformation.ProcessArchitecture);

    public static void RequireAvailable()
    {
        if (CurrentUnavailableReason is { } reason) throw new PlatformNotSupportedException(reason);
    }
}

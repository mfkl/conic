namespace Conic.Interop;

internal sealed record PlaybackStartupFailure(string Message, string Detail);

internal static class PlaybackStartup
{
    public static PlaybackStartupFailure? TryInitialize(Action initialize) => TryInitialize(initialize, VlcRuntimeLocator.CurrentPlatform);

    internal static PlaybackStartupFailure? TryInitialize(Action initialize, VlcRuntimePlatform platform)
    {
        try { initialize(); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return new("VLC playback files are missing, inaccessible or incompatible. " + Reinstall(platform) + " " +
                "If you configured VLC_DIRECTORY for development, check or remove that override.", error.Message);
        }
    }

    // Each package keeps VLC where the platform's download puts it.
    private static string Reinstall(VlcRuntimePlatform platform) => platform switch
    {
        VlcRuntimePlatform.MacOS => "Download Conic again and replace Conic.app with the new copy; VLC is inside the app.",
        VlcRuntimePlatform.Linux => "Extract the complete Linux x64 package again and keep its vlc folder beside Conic.",
        _ => "Extract the complete Windows x64 package again and keep its vlc folder beside the application.",
    };
}

namespace VLCAvalonia.Interop;

internal sealed record PlaybackStartupFailure(string Message, string Detail);

internal static class PlaybackStartup
{
    public static PlaybackStartupFailure? TryInitialize(Action initialize)
    {
        try { initialize(); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return new("VLC playback files are missing, inaccessible or incompatible. " +
                "Extract the complete Windows x64 package again and keep its vlc folder beside the application. " +
                "If you configured VLC_DIRECTORY for development, check or remove that override.", error.Message);
        }
    }
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Module;
using VLCLR.Native;

namespace Conic.Vout;

/// <summary>
/// VLC interface module that lends the main playlist to the Avalonia host for
/// the interface's lifetime. It has no platform-specific code.
/// </summary>
public static unsafe class ControlInterface
{
    private const int Success = 0;
    private const int GenericError = -1;

    // Registration and invocation share this gate. A disconnect therefore
    // waits for an in-flight callback to return before the host frees its
    // callback userdata.
    private static readonly object CallbackGate = new();
    private static nint s_playlistCallback;
    private static nint s_playlistCallbackOpaque;

    internal static int Register(nint vlcSet, nint opaque) =>
        ModuleBuilder.Create(vlcSet, opaque)
            .WithShortcut("vlc_avalonia_control")
            .WithShortName("Conic control host")
            .WithDescription("Exposes the VLC 3 main playlist to the Avalonia host")
            .WithCapability("interface")
            .WithScore(0)
            .WithOpenCallback(&Open, "ControlInterfaceOpen")
            .WithCloseCallback(&Close, "ControlInterfaceClose")
            .WithNoUnload()
            .Register();

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_playlist_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetPlaylistCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_playlistCallback = callback;
            s_playlistCallbackOpaque = opaque;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(nint interfacePtr)
    {
        try
        {
            if (interfacePtr == 0)
                return GenericError;
            VLCObjectHeader* header = (VLCObjectHeader*)interfacePtr;
            PublishPlaylist(header->Parent);
            return header->Parent == 0 ? GenericError : Success;
        }
        catch
        {
            return GenericError;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Close(nint interfacePtr)
    {
        try
        {
            PublishPlaylist(0);
        }
        catch
        {
        }
    }

    private static void PublishPlaylist(nint playlist)
    {
        lock (CallbackGate)
        {
            if (s_playlistCallback == 0)
                return;
            var callback =
                (delegate* unmanaged[Cdecl]<nint, nint, void>)s_playlistCallback;
            callback(s_playlistCallbackOpaque, playlist);
        }
    }
}

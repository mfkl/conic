using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.Linux;

/// <summary>The libvlc 3 calls for decoding through video callbacks, bound to the runtime's own libvlc.</summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LibVlcApi(nint library)
{
    private static readonly Lazy<LibVlcApi> s_runtime = new(() => new LibVlcApi(NativeLibrary.Load(RepositoryPaths.PublicLibraryPath)));

    public static LibVlcApi Runtime => s_runtime.Value;

    private nint Export(string name) => NativeLibrary.GetExport(library, name);
    public delegate* unmanaged[Cdecl]<int, nint*, nint> New => (delegate* unmanaged[Cdecl]<int, nint*, nint>)Export("libvlc_new");
    public delegate* unmanaged[Cdecl]<nint, void> Release => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_release");
    public delegate* unmanaged[Cdecl]<nint, byte*, nint> MediaNewPath => (delegate* unmanaged[Cdecl]<nint, byte*, nint>)Export("libvlc_media_new_path");
    public delegate* unmanaged[Cdecl]<nint, byte*, void> MediaAddOption => (delegate* unmanaged[Cdecl]<nint, byte*, void>)Export("libvlc_media_add_option");
    public delegate* unmanaged[Cdecl]<nint, int, int, int> MediaParseWithOptions => (delegate* unmanaged[Cdecl]<nint, int, int, int>)Export("libvlc_media_parse_with_options");
    public delegate* unmanaged[Cdecl]<nint, int> MediaGetParsedStatus => (delegate* unmanaged[Cdecl]<nint, int>)Export("libvlc_media_get_parsed_status");
    public delegate* unmanaged[Cdecl]<nint, long> MediaGetDuration => (delegate* unmanaged[Cdecl]<nint, long>)Export("libvlc_media_get_duration");
    public delegate* unmanaged[Cdecl]<nint, nint**, uint> MediaTracksGet => (delegate* unmanaged[Cdecl]<nint, nint**, uint>)Export("libvlc_media_tracks_get");
    public delegate* unmanaged[Cdecl]<nint*, uint, void> MediaTracksRelease => (delegate* unmanaged[Cdecl]<nint*, uint, void>)Export("libvlc_media_tracks_release");
    public delegate* unmanaged[Cdecl]<nint, void> MediaRelease => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_release");
    public delegate* unmanaged[Cdecl]<nint, nint> PlayerNewFromMedia => (delegate* unmanaged[Cdecl]<nint, nint>)Export("libvlc_media_player_new_from_media");
    public delegate* unmanaged[Cdecl]<nint, int> Play => (delegate* unmanaged[Cdecl]<nint, int>)Export("libvlc_media_player_play");
    public delegate* unmanaged[Cdecl]<nint, void> Stop => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_player_stop");
    public delegate* unmanaged[Cdecl]<nint, void> PlayerRelease => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_player_release");
    public delegate* unmanaged[Cdecl]<nint, uint, uint*, uint*, int> GetSize => (delegate* unmanaged[Cdecl]<nint, uint, uint*, uint*, int>)Export("libvlc_video_get_size");
    public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint*, byte*, uint*, uint*, uint*, uint*, uint>, delegate* unmanaged[Cdecl]<nint, void>, void> SetFormatCallbacks =>
        (delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint*, byte*, uint*, uint*, uint*, uint*, uint>, delegate* unmanaged[Cdecl]<nint, void>, void>)Export("libvlc_video_set_format_callbacks");
    public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, nint*, nint>, delegate* unmanaged[Cdecl]<nint, nint, nint*, void>, delegate* unmanaged[Cdecl]<nint, nint, void>, nint, void> SetCallbacks =>
        (delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, nint*, nint>, delegate* unmanaged[Cdecl]<nint, nint, nint*, void>, delegate* unmanaged[Cdecl]<nint, nint, void>, nint, void>)Export("libvlc_video_set_callbacks");
}

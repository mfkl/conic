using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;

namespace VLCAvalonia.Vout;

/// <summary>
/// VLC 3 module entry points. The playlist control interface is registered on
/// every target; the D3D11 video output in the Windows build and the IOSurface
/// video output in the macOS builds.
/// </summary>
/// <remarks>
/// Debian and Ubuntu builds of VLC 3 look for the <c>3_0_0ft64</c> suffix
/// introduced by their 64-bit <c>time_t</c> transition. On 64-bit targets
/// <c>time_t</c> was already 64 bits wide, so the ABI behind both suffixes is
/// identical and the same entry points are exported under both names.
/// </remarks>
public static unsafe class PluginEntry
{
    private static readonly nint Copyright =
        Marshal.StringToCoTaskMemUTF8("Copyright (c) VideoLabs");
    private static readonly nint License =
        Marshal.StringToCoTaskMemUTF8("LGPL-2.1-or-later");

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright__3_0_0f", CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCCopyright() => Copyright;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_license__3_0_0f", CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCLicense() => License;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry__3_0_0f", CallConvs = [typeof(CallConvCdecl)])]
    public static int VLCEntry(nint vlcSet, nint opaque) => Register(vlcSet, opaque);

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright__3_0_0ft64", CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCCopyrightTime64() => Copyright;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_license__3_0_0ft64", CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCLicenseTime64() => License;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry__3_0_0ft64", CallConvs = [typeof(CallConvCdecl)])]
    public static int VLCEntryTime64(nint vlcSet, nint opaque) => Register(vlcSet, opaque);

    private static int Register(nint vlcSet, nint opaque)
    {
#if VLC_AVALONIA_D3D11
        int result = CompositionVoutPlugin.Register(vlcSet, opaque);
        if (result != 0)
            return result;
#elif VLC_AVALONIA_IOSURFACE
        // The display calls libvlccore, which VLC.app keeps outside every
        // library search path: use the core that is loading this plugin.
        if (!OperatingSystem.IsMacOS())
            return -1;
        try
        {
            VLCCore.UseLibraryContaining(vlcSet);
        }
        catch
        {
            return -1;
        }
        int result = IOSurfaceVoutPlugin.Register(vlcSet, opaque);
        if (result != 0)
            return result;
#endif
        return ControlInterface.Register(vlcSet, opaque);
    }
}

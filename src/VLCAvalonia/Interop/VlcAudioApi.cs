using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace VLCAvalonia.Interop;

/// <summary>
/// The VLC 3 audio output of a playlist core: <c>playlist_GetAout</c> (the
/// playlist creates its output with the core, so it exists before any media)
/// and the output's device (<c>aout_DeviceSet</c>/<c>aout_DeviceGet</c>).
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe class VlcAudioApi
{
    /// <summary>The playlist's audio output, held: release it with <see cref="VLCCore.ObjectRelease"/>. Zero when it has none.</summary>
    public static nint HoldOutput(nint playlist) => Exports.GetAout(playlist);

    /// <summary>Asks the output to switch to the device; empty is the system default.</summary>
    public static bool SetDevice(nint output, string device)
    {
        if (device.Length == 0) return Exports.DeviceSet(output, null) == 0;
        nint id = Marshal.StringToCoTaskMemUTF8(device);
        try { return Exports.DeviceSet(output, (byte*)id) == 0; }
        finally { Marshal.FreeCoTaskMem(id); }
    }

    /// <summary>The device the output uses, empty for the default.</summary>
    public static string Device(nint output)
    {
        byte* id = Exports.DeviceGet(output);
        if (id is null) return string.Empty;
        try { return Marshal.PtrToStringUTF8((nint)id) ?? string.Empty; }
        finally { VLCCore.Free((nint)id); }
    }

    private static class Exports
    {
        static Exports() { }
        private static readonly nint Library = NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        internal static readonly delegate* unmanaged[Cdecl]<nint, nint> GetAout =
            (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(Library, "playlist_GetAout");
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*, int> DeviceSet =
            (delegate* unmanaged[Cdecl]<nint, byte*, int>)NativeLibrary.GetExport(Library, "aout_DeviceSet");
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*> DeviceGet =
            (delegate* unmanaged[Cdecl]<nint, byte*>)NativeLibrary.GetExport(Library, "aout_DeviceGet");
    }
}

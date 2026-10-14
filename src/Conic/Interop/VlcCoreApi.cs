using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop;

/// <summary>The product's fixed-signature VLC 3 interface startup export.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe class VlcCoreApi
{
    public static int AddInterface(nint core, string name)
    {
        NativeEngineSupport.RequireAvailable();
        nint pointer = Marshal.StringToCoTaskMemUTF8(name);
        try { return Exports.AddInterface(core, (byte*)pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    /// <summary>vlc_LogSet: VLC 3 calls <paramref name="callback"/> for every message of <paramref name="core"/>.</summary>
    public static void SetLogger(nint core, delegate* unmanaged[Cdecl]<nint, int, VLCLR.Types.VLCLog*, byte*, nint, void> callback)
    {
        NativeEngineSupport.RequireAvailable();
        Exports.LogSet(core, callback, 0);
    }

    /// <summary>libvlc_get_version: the engine's version, such as "3.0.24 Vetinari"; null when VLC can't be loaded.</summary>
    public static string? TryGetVersion()
    {
        try
        {
            NativeEngineSupport.RequireAvailable();
            return Marshal.PtrToStringUTF8((nint)PublicExports.Version());
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or PlatformNotSupportedException or TypeInitializationException)
        {
            return null;
        }
    }

    // Load only after the platform/architecture guard, and retain the module
    // for the process lifetime just like the other VLC core bindings.
    private static class Exports
    {
        static Exports() { }
        private static readonly nint Library = NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*, int> AddInterface =
            (delegate* unmanaged[Cdecl]<nint, byte*, int>)NativeLibrary.GetExport(Library, "libvlc_InternalAddIntf");
        internal static readonly delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, int, VLCLR.Types.VLCLog*, byte*, nint, void>, nint, void> LogSet =
            (delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, int, VLCLR.Types.VLCLog*, byte*, nint, void>, nint, void>)NativeLibrary.GetExport(Library, "vlc_LogSet");
    }

    private static class PublicExports
    {
        static PublicExports() { }
        private static readonly nint Library = NativeLibrary.Load(RepositoryPaths.PublicLibraryPath);
        internal static readonly delegate* unmanaged[Cdecl]<byte*> Version =
            (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(Library, "libvlc_get_version");
    }
}

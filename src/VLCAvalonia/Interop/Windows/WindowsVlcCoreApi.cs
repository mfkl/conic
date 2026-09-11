using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.Windows;

/// <summary>The product's fixed-signature VLC 3 interface startup export.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsVlcCoreApi
{
    public static int AddInterface(nint core, string name)
    {
        NativePlaybackSupport.RequireAvailable();
        nint pointer = Marshal.StringToCoTaskMemUTF8(name);
        try { return Exports.AddInterface(core, (byte*)pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    // Load only after the platform/architecture guard, and retain the module
    // for the process lifetime just like the other VLC core bindings.
    private static class Exports
    {
        static Exports() { }
        private static readonly nint Library = NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*, int> AddInterface =
            (delegate* unmanaged[Cdecl]<nint, byte*, int>)NativeLibrary.GetExport(Library, "libvlc_InternalAddIntf");
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Player.Application;
using VLCLR.Native;

namespace Conic.Interop;

// The input commands behind VLC 3's hotkeys for Next Frame (E) and the
// previous and next chapter (Shift+P, Shift+N): VLC's hotkeys module
// triggers these variables on the playing input, and so does Conic. Next
// Frame pauses a playing input first, as in VLC; Previous Chapter restarts
// the chapter when it has played for more than 1.5 s.
internal sealed partial class VlcCorePlayer : IPlaybackNavigationTransport
{
    public void NextFrame() => TriggerInput("frame-next");

    public void NextChapter() => TriggerInput("next-chapter");

    public void PreviousChapter() => TriggerInput("prev-chapter");

    private void TriggerInput(string variable) => QueueMediaRequest(() =>
    {
        nint playlist;
        lock (_gate) playlist = _tearingDown ? 0 : _playlist;
        if (playlist == 0) return;
        nint input = VLCCore.PlaylistCurrentInput(playlist);
        if (input == 0) return;
        try { VlcInputApi.Trigger(input, variable); }
        finally { VLCCore.ObjectRelease(input); }
    });
}

/// <summary>
/// <c>var_TriggerCallback</c>: runs the callbacks of a VLC 3 variable that
/// carries no value, such as an input's <c>frame-next</c>. A variable the
/// object lacks (a media without chapters has no <c>next-chapter</c>) is
/// left alone by VLC.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe class VlcInputApi
{
    public static void Trigger(nint vlcObject, string variable)
    {
        nint name = Marshal.StringToCoTaskMemUTF8(variable);
        try { Exports.TriggerCallback(vlcObject, (byte*)name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    private static class Exports
    {
        static Exports() { }
        private static readonly nint Library = NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        internal static readonly delegate* unmanaged[Cdecl]<nint, byte*, int> TriggerCallback =
            (delegate* unmanaged[Cdecl]<nint, byte*, int>)NativeLibrary.GetExport(Library, "var_TriggerCallback");
    }
}

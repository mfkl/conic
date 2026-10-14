using Player.Application;
using VLCLR;

namespace Conic.Interop;

// VLC 3's input commands behind its hotkeys for Next Frame (E) and the
// previous and next chapter (Shift+P, Shift+N), through VLCLR's transport
// controls; and Take Snapshot (Shift+S), on the playing video output.
internal sealed partial class VlcCorePlayer : IPlaybackNavigationTransport, IVideoSnapshotTransport
{
    public void NextFrame() => OnPlaylist(transport => transport.NextFrame());

    public void NextChapter() => OnPlaylist(transport => transport.NextChapter());

    public void PreviousChapter() => OnPlaylist(transport => transport.PreviousChapter());

    /// <summary>VLC writes the file where Settings › Video › Snapshots says (the core's options).</summary>
    public bool RequestSnapshot() => QueueMediaRequest(() =>
    {
        VLCPlayerCapabilityResult result = _videoControls.RequestSnapshot();
        Console.WriteLine($"[snapshot] result={result.Code}");
    });

    private void OnPlaylist(Func<VLCTransportControl, bool> command) => QueueMediaRequest(() =>
    {
        nint playlist;
        lock (_gate) playlist = _tearingDown ? 0 : _playlist;
        if (playlist != 0) command(new VLCTransportControl(playlist));
    });
}

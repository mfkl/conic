using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop;

/// <summary>
/// Receives the VLC main playlist from the plugin's <c>vlc_avalonia_control</c>
/// interface. The playlist is lent for the interface/core lifetime and is never
/// released independently.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed unsafe class VlcControlHost : IDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setPlaylistCallback;
    private readonly ManualResetEventSlim _playlistReady = new(false);
    private GCHandle _selfHandle;
    private nint _playlist;
    private bool _connected;
    private int _disposed;

    public VlcControlHost()
    {
        NativeEngineSupport.RequireAvailable();
        nint library = NativeLibrary.Load(RepositoryPaths.PluginPath);
        _setPlaylistCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)
            NativeLibrary.GetExport(library, "vlc_avalonia_set_playlist_callback");
        _selfHandle = GCHandle.Alloc(this);
    }

    public void Connect()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_connected)
                return;

            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
            _setPlaylistCallback(
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnPlaylist,
                GCHandle.ToIntPtr(_selfHandle));
            _connected = true;
        }
    }

    public void Reset()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
        }
    }

    public nint WaitForPlaylist(TimeSpan timeout, ManualResetEventSlim stop)
    {
        ThrowIfDisposed();
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (stop.IsSet)
                throw new OperationCanceledException(
                    "Playback stopped while waiting for the VLC playlist.");

            nint playlist = Volatile.Read(ref _playlist);
            if (playlist != 0)
                return playlist;

            TimeSpan remaining = timeout - elapsed.Elapsed;
            _playlistReady.Wait(
                remaining < TimeSpan.FromMilliseconds(50)
                    ? remaining
                    : TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            "VLC interface vlc_avalonia_control did not publish the main " +
            $"playlist within {timeout.TotalSeconds:0.#} seconds.");
    }

    public void Disconnect()
    {
        lock (_lifecycleGate)
        {
            if (!_connected)
                return;
            // The native setter returns only after an in-flight callback has completed.
            _setPlaylistCallback(0, 0);
            _connected = false;
            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnPlaylist(nint opaque, nint playlist)
    {
        try
        {
            if (opaque == 0 || GCHandle.FromIntPtr(opaque).Target is not VlcControlHost host || host.IsDisposed)
                return;

            Volatile.Write(ref host._playlist, playlist);
            if (playlist == 0)
                host._playlistReady.Reset();
            else
                host._playlistReady.Set();
        }
        catch
        {
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (IsDisposed)
                return;
            Disconnect();
            Volatile.Write(ref _disposed, 1);
            _playlistReady.Dispose();
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
        }

        // Do not unload: VLC may hold the same module after discovering it.
    }
}

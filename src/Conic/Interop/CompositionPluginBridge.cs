using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Conic.Interop;

internal sealed unsafe class CompositionPluginBridge : IDisposable
{
    internal sealed record GPUFrame(
        nint SharedHandle,
        int Width,
        int Height,
        ulong Generation,
        long PresentationTimestamp);

    internal sealed record Frame(
        int Width,
        int Height,
        int RowBytes,
        byte[] Pixels,
        long PresentationTimestamp);

    private readonly object _lifecycleGate = new();
    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setGPUCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void>
        _setPlaylistCallback;
    private readonly ManualResetEventSlim _playlistReady = new(false);
    private GCHandle _selfHandle;
    private nint _opaque;
    private Frame? _latestFrame;
    private GPUFrame? _latestGPUFrame;
    private nint _playlist;
    private bool _frameConnected;
    private bool _GPUConnected;
    private bool _playlistConnected;
    private int _disposed;

    public string PluginDirectory { get; }

    public CompositionPluginBridge(bool registerFrameCallback = true)
    {
        string pluginPath = FindPluginPath();
        PluginDirectory = Path.GetDirectoryName(pluginPath)!;
        _library = NativeLibrary.Load(pluginPath);
        nint setter = NativeLibrary.GetExport(
            _library,
            "vlc_avalonia_set_frame_callback");
        _setCallback =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)setter;
        nint GPUSetter = NativeLibrary.GetExport(
            _library,
            "vlc_avalonia_set_gpu_frame_callback");
        _setGPUCallback =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)GPUSetter;
        nint playlistSetter = NativeLibrary.GetExport(
            _library,
            "vlc_avalonia_set_playlist_callback");
        _setPlaylistCallback =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)playlistSetter;

        _selfHandle = GCHandle.Alloc(this);
        _opaque = GCHandle.ToIntPtr(_selfHandle);
        try
        {
            if (registerFrameCallback)
            {
                _setCallback(
                    (nint)(delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)&OnFrame,
                    _opaque);
                _frameConnected = true;
            }
        }
        catch
        {
            _opaque = 0;
            _selfHandle.Free();
            throw;
        }
    }

    public void ConnectPlaylist()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_playlistConnected)
                return;

            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
            _setPlaylistCallback(
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnPlaylist,
                _opaque);
            _playlistConnected = true;
        }
    }

    public void ResetPlaylist()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
        }
    }

    public nint WaitForPlaylist(
        TimeSpan timeout,
        ManualResetEventSlim stop)
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

    public void DisconnectPlaylist()
    {
        lock (_lifecycleGate)
        {
            if (!_playlistConnected)
                return;
            _setPlaylistCallback(0, 0);
            _playlistConnected = false;
            Volatile.Write(ref _playlist, 0);
            _playlistReady.Reset();
        }
    }

    public void EnableGPUFrames()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_GPUConnected)
                return;
            _setGPUCallback(
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, ulong, long, void>)&OnGPUFrame,
                _opaque);
            _GPUConnected = true;
        }
    }

    public void DisableGPUFrames()
    {
        lock (_lifecycleGate)
        {
            if (_GPUConnected)
            {
                _setGPUCallback(0, 0);
                _GPUConnected = false;
            }
            Interlocked.Exchange(ref _latestGPUFrame, null);
        }
    }

    public bool TryTakeLatestGPU(out GPUFrame? frame)
    {
        frame = Interlocked.Exchange(ref _latestGPUFrame, null);
        return frame is not null;
    }

    public bool TryTakeLatest(out Frame? frame)
    {
        frame = Interlocked.Exchange(ref _latestFrame, null);
        return frame is not null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnPlaylist(nint opaque, nint playlist)
    {
        try
        {
            CompositionPluginBridge? bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed)
                return;

            Volatile.Write(ref bridge._playlist, playlist);
            if (playlist == 0)
                bridge._playlistReady.Reset();
            else
                bridge._playlistReady.Set();
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(
        nint opaque,
        uint width,
        uint height,
        int stride,
        nint pixels,
        long presentationTimestamp)
    {
        try
        {
            CompositionPluginBridge? bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed || pixels == 0)
                return;
            if (width is 0 or > 16384 || height is 0 or > 16384)
                return;

            int rowBytes = checked((int)width * 4);
            if (Math.Abs((long)stride) < rowBytes)
                return;

            byte[] destination = GC.AllocateUninitializedArray<byte>(
                checked(rowBytes * (int)height));
            for (int row = 0; row < height; row++)
            {
                nint sourceRow = pixels + checked(row * stride);
                new ReadOnlySpan<byte>((void*)sourceRow, rowBytes).CopyTo(
                    destination.AsSpan(row * rowBytes, rowBytes));
            }

            Interlocked.Exchange(
                ref bridge._latestFrame,
                new Frame(
                    (int)width,
                    (int)height,
                    rowBytes,
                    destination,
                    presentationTimestamp));
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGPUFrame(
        nint opaque,
        nint sharedHandle,
        uint width,
        uint height,
        ulong generation,
        long presentationTimestamp)
    {
        try
        {
            CompositionPluginBridge? bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed || sharedHandle == 0)
                return;
            if (width is 0 or > 16384 || height is 0 or > 16384)
                return;

            Interlocked.Exchange(
                ref bridge._latestGPUFrame,
                new GPUFrame(
                    sharedHandle,
                    (int)width,
                    (int)height,
                    generation,
                    presentationTimestamp));
        }
        catch
        {
        }
    }

    private static CompositionPluginBridge? GetBridge(nint opaque)
    {
        if (opaque == 0)
            return null;
        return GCHandle.FromIntPtr(opaque).Target as CompositionPluginBridge;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(IsDisposed, this);

    private static string FindPluginPath()
    {
        string root = RepositoryPaths.Root;
        string fileName = "libvlc_avalonia_plugin.dll";
        string[] candidates =
        [
            Path.Combine(
                RepositoryPaths.VLCDirectory,
                "plugins",
                "video_output",
                fileName),
            Path.Combine(
                root,
                "src",
                "Conic.Vout",
                "bin",
                "Release",
                "net10.0",
                "win-x64",
                "native",
                fileName),
            Path.Combine(
                root,
                "src",
                "Conic.Vout",
                "bin",
                "Debug",
                "net10.0",
                "win-x64",
                "native",
                fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Publish src/Conic.Vout for win-x64 before starting " +
            "Conic.",
            candidates[1]);
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (IsDisposed)
                return;
            Volatile.Write(ref _disposed, 1);

            if (_playlistConnected)
            {
                _setPlaylistCallback(0, 0);
                _playlistConnected = false;
            }
            if (_GPUConnected)
            {
                _setGPUCallback(0, 0);
                _GPUConnected = false;
            }
            if (_frameConnected)
            {
                _setCallback(0, 0);
                _frameConnected = false;
            }

            Volatile.Write(ref _playlist, 0);
            Interlocked.Exchange(ref _latestFrame, null);
            Interlocked.Exchange(ref _latestGPUFrame, null);
            _playlistReady.Dispose();

            // The native setters return only after in-flight callbacks have
            // completed, so callback userdata can now be released safely.
            _opaque = 0;
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
        }

        // Do not unload: VLC may hold the same module after discovering it.
        GC.SuppressFinalize(this);
    }
}

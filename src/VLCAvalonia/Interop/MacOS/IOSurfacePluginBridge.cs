using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>
/// The app's side of the macOS video output: IOSurface frames, CPU fallback
/// frames, the subtitle overlay, geometry and picture counters.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class IOSurfacePluginBridge : IDisposable
{
    /// <summary>
    /// A frame lent by the video output: one reference on its pixel buffer,
    /// which <see cref="Release"/> returns. The surface is valid until then.
    /// </summary>
    internal sealed class SurfaceFrame(
        nint pixelBuffer, nint surface, uint surfaceId, int surfaceWidth, int surfaceHeight,
        int x, int y, int width, int height, long presentationTimestamp, ulong poolGeneration)
    {
        private nint _pixelBuffer = pixelBuffer;
        public nint Surface { get; } = surface;
        public uint SurfaceId { get; } = surfaceId;
        public int SurfaceWidth { get; } = surfaceWidth;
        public int SurfaceHeight { get; } = surfaceHeight;
        /// <summary>The visible picture inside the surface.</summary>
        public int X { get; } = x;
        public int Y { get; } = y;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public long PresentationTimestamp { get; } = presentationTimestamp;
        /// <summary>Changes when VLC opens a new display, as after reopening media.</summary>
        public ulong PoolGeneration { get; } = poolGeneration;
        internal nint TakePixelBuffer() => Interlocked.Exchange(ref _pixelBuffer, 0);
    }

    /// <summary>Straight-alpha RGBA overlay at Bounds on a canvas covering the visible video. Empty Pixels clears it.</summary>
    internal sealed record Subtitle(int CanvasWidth, int CanvasHeight, SubtitleBounds Bounds, byte[] Pixels)
    {
        public static Subtitle None { get; } = new(0, 0, default, []);
    }

    internal sealed record Frame(int Width, int Height, int RowBytes, byte[] Pixels, long PresentationTimestamp);

    /// <summary>The plugin's picture counters; on macOS "opaque" pictures are pixel buffers.</summary>
    internal readonly record struct PoolTelemetry(
        long PixelBufferPicturesReceived,
        long CpuPicturesReceived,
        long PoolGenerations,
        long PixelBufferPoolGenerations,
        long CpuPoolGenerations,
        long PixelBuffersLent,
        long PixelBuffersReturned,
        long CpuCopies)
    {
        public long LentBalance => PixelBuffersLent - PixelBuffersReturned;

        public string ToMachineReadableString() =>
            "[iosurface-pool] " +
            $"pixel-buffer-pictures={PixelBufferPicturesReceived} " +
            $"cpu-pictures={CpuPicturesReceived} " +
            $"pool-generations={PoolGenerations} " +
            $"pixel-buffer-pool-generations={PixelBufferPoolGenerations} " +
            $"cpu-pool-generations={CpuPoolGenerations} " +
            $"lent={PixelBuffersLent} returned={PixelBuffersReturned} " +
            $"lent-balance={LentBalance} cpu-copies={CpuCopies}";
    }

    private readonly object _lifecycleGate = new();
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setFrameCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setSurfaceCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setSubtitleCallback;
    private readonly delegate* unmanaged[Cdecl]<VideoGeometry*, void> _getVideoGeometry;
    private readonly delegate* unmanaged[Cdecl]<long*, long*, long*, long*, long*, long*, long*, long*, void> _getPoolTelemetry;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _releasePixelBuffer;
    private GCHandle _selfHandle;
    private nint _opaque;
    private SurfaceFrame? _latestSurface;
    private Frame? _latestFrame;
    private Subtitle? _latestSubtitle;
    private bool _surfaceConnected;
    private bool _framesConnected;
    private int _disposed;

    public IOSurfacePluginBridge(bool registerFrameCallbacks = true)
    {
        nint library = NativeLibrary.Load(RepositoryPaths.PluginPath);
        _setFrameCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_frame_callback");
        _setSurfaceCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_iosurface_frame_callback");
        _setSubtitleCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_subtitle_callback");
        _getVideoGeometry = (delegate* unmanaged[Cdecl]<VideoGeometry*, void>)NativeLibrary.GetExport(library, "vlc_avalonia_get_video_geometry_v1");
        _getPoolTelemetry = (delegate* unmanaged[Cdecl]<long*, long*, long*, long*, long*, long*, long*, long*, void>)
            NativeLibrary.GetExport(library, "vlc_avalonia_get_pool_telemetry");
        _releasePixelBuffer = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_release_pixel_buffer");

        _selfHandle = GCHandle.Alloc(this);
        _opaque = GCHandle.ToIntPtr(_selfHandle);
        if (registerFrameCallbacks)
        {
            _setFrameCallback((nint)(delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)&OnFrame, _opaque);
            _setSubtitleCallback((nint)(delegate* unmanaged[Cdecl]<nint, int, int, int, int, int, int, nint, void>)&OnSubtitle, _opaque);
            _framesConnected = true;
        }
    }

    /// <summary>
    /// Asks displays opened from now on for IOSurface-backed pixel buffers.
    /// Without it they publish CPU frames.
    /// </summary>
    public void EnableSurfaceFrames()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_surfaceConnected)
                return;
            _setSurfaceCallback(
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, uint, uint, uint, uint, uint, uint, long, ulong, void>)&OnSurfaceFrame,
                _opaque);
            _surfaceConnected = true;
        }
    }

    public void DisableSurfaceFrames()
    {
        lock (_lifecycleGate)
        {
            if (_surfaceConnected)
            {
                _setSurfaceCallback(0, 0);
                _surfaceConnected = false;
            }
            Release(Interlocked.Exchange(ref _latestSurface, null));
        }
    }

    public VideoGeometry GetVideoGeometry()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            VideoGeometry geometry = default;
            _getVideoGeometry(&geometry);
            return geometry;
        }
    }

    public bool TryTakeLatestSurface(out SurfaceFrame? frame)
    {
        frame = Interlocked.Exchange(ref _latestSurface, null);
        return frame is not null;
    }

    public bool TryTakeLatest(out Frame? frame)
    {
        frame = Interlocked.Exchange(ref _latestFrame, null);
        return frame is not null;
    }

    public bool TryTakeLatestSubtitle(out Subtitle? subtitle)
    {
        subtitle = Interlocked.Exchange(ref _latestSubtitle, null);
        return subtitle is not null;
    }

    /// <summary>Returns a frame's pixel buffer to VLC. Safe to call more than once and after disposal.</summary>
    public void Release(SurfaceFrame? frame)
    {
        if (frame?.TakePixelBuffer() is { } pixelBuffer and not 0)
            _releasePixelBuffer(pixelBuffer);
    }

    public PoolTelemetry ReadPoolTelemetry()
    {
        long received = 0, cpuPictures = 0, pools = 0, pixelBufferPools = 0, cpuPools = 0, lent = 0, returned = 0, copies = 0;
        _getPoolTelemetry(&received, &cpuPictures, &pools, &pixelBufferPools, &cpuPools, &lent, &returned, &copies);
        return new PoolTelemetry(received, cpuPictures, pools, pixelBufferPools, cpuPools, lent, returned, copies);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSurfaceFrame(
        nint opaque, nint pixelBuffer, nint surface, uint surfaceWidth, uint surfaceHeight,
        uint x, uint y, uint width, uint height, long presentationTimestamp, ulong poolGeneration)
    {
        IOSurfacePluginBridge? bridge = null;
        SurfaceFrame? frame = null;
        try
        {
            bridge = GetBridge(opaque);
            frame = new SurfaceFrame(pixelBuffer, surface, CoreVideo.IOSurfaceGetID(surface),
                (int)surfaceWidth, (int)surfaceHeight, (int)x, (int)y, (int)width, (int)height, presentationTimestamp, poolGeneration);
            if (bridge is null || bridge.IsDisposed || surface == 0 ||
                width is 0 or > 16384 || height is 0 or > 16384)
            {
                bridge?.Release(frame);
                if (bridge is null)
                    ReleaseOrphan(pixelBuffer);
                return;
            }
            // Keep only the newest frame; an unpresented one goes straight back.
            bridge.Release(Interlocked.Exchange(ref bridge._latestSurface, frame));
        }
        catch
        {
            if (bridge is not null && frame is not null)
                bridge.Release(frame);
        }
    }

    // Without a bridge there is no release export at hand; the reference was
    // counted as lent, so the plugin's telemetry will show it as outstanding.
    private static void ReleaseOrphan(nint pixelBuffer) => CoreVideo.CFRelease(pixelBuffer);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(nint opaque, uint width, uint height, int stride, nint pixels, long presentationTimestamp)
    {
        try
        {
            IOSurfacePluginBridge? bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed || pixels == 0 ||
                width is 0 or > 16384 || height is 0 or > 16384)
                return;
            int rowBytes = checked((int)width * 4);
            if (Math.Abs((long)stride) < rowBytes)
                return;
            byte[] destination = GC.AllocateUninitializedArray<byte>(checked(rowBytes * (int)height));
            for (int row = 0; row < height; row++)
                new ReadOnlySpan<byte>((void*)(pixels + checked(row * stride)), rowBytes)
                    .CopyTo(destination.AsSpan(row * rowBytes, rowBytes));
            Interlocked.Exchange(ref bridge._latestFrame,
                new Frame((int)width, (int)height, rowBytes, destination, presentationTimestamp));
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSubtitle(nint opaque, int canvasWidth, int canvasHeight, int x, int y, int width, int height, nint pixels)
    {
        try
        {
            IOSurfacePluginBridge? bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed)
                return;
            SubtitleBounds bounds = new(x, y, width, height);
            Subtitle subtitle = Subtitle.None;
            if (!bounds.IsEmpty && pixels != 0 &&
                canvasWidth is > 0 and <= SubtitleComposer.MaximumCanvas && canvasHeight is > 0 and <= SubtitleComposer.MaximumCanvas &&
                x >= 0 && y >= 0 && (long)x + width <= canvasWidth && (long)y + height <= canvasHeight)
            {
                subtitle = new Subtitle(canvasWidth, canvasHeight, bounds,
                    new ReadOnlySpan<byte>((void*)pixels, bounds.ByteCount).ToArray());
            }
            Interlocked.Exchange(ref bridge._latestSubtitle, subtitle);
        }
        catch
        {
        }
    }

    private static IOSurfacePluginBridge? GetBridge(nint opaque) =>
        opaque == 0 ? null : GCHandle.FromIntPtr(opaque).Target as IOSurfacePluginBridge;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (IsDisposed)
                return;
            Volatile.Write(ref _disposed, 1);
            if (_surfaceConnected)
            {
                _setSurfaceCallback(0, 0);
                _surfaceConnected = false;
            }
            if (_framesConnected)
            {
                _setSubtitleCallback(0, 0);
                _setFrameCallback(0, 0);
                _framesConnected = false;
            }
            Release(Interlocked.Exchange(ref _latestSurface, null));
            Interlocked.Exchange(ref _latestFrame, null);
            Interlocked.Exchange(ref _latestSubtitle, null);
            // The setters return only after in-flight callbacks have completed,
            // so the callback userdata can now be released.
            _opaque = 0;
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
        }
        // Do not unload: VLC may hold the same module after discovering it.
    }
}

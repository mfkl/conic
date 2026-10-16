using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.Linux;

/// <summary>
/// The app's side of the Linux video output: DMA-BUF frames, CPU fallback
/// frames, the subtitle overlay, geometry and picture counters.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class DmaBufPluginBridge : IDisposable
{
    /// <summary>
    /// A converted frame lent by the video output: a duplicate of its DMA-BUF
    /// descriptor, owned here, and the target it names, which stays unchanged
    /// until <see cref="Release"/> returns it.
    /// </summary>
    internal sealed class DmaBufFrame(
        int fd, uint drmFormat, ulong modifier, uint pitch, uint offset, ulong size,
        int width, int height, ulong generation, int slot, long presentationTimestamp)
    {
        private int _fd = fd;
        private int _returned;
        public uint DrmFormat { get; } = drmFormat;
        public ulong Modifier { get; } = modifier;
        public uint Pitch { get; } = pitch;
        public uint Offset { get; } = offset;
        public ulong Size { get; } = size;
        public int Width { get; } = width;
        public int Height { get; } = height;
        /// <summary>Changes when VLC opens a new display, as after reopening media.</summary>
        public ulong Generation { get; } = generation;
        /// <summary>The target within the display's ring.</summary>
        public int Slot { get; } = slot;
        public long PresentationTimestamp { get; } = presentationTimestamp;
        /// <summary>The descriptor, or -1 once closed.</summary>
        public int Fd => Volatile.Read(ref _fd);

        /// <summary>Closes the descriptor; an import keeps its own reference to the memory.</summary>
        public void CloseDescriptor()
        {
            int descriptor = Interlocked.Exchange(ref _fd, -1);
            if (descriptor >= 0) VaapiConverter.CloseFile(descriptor);
        }

        internal bool TakeReturn() => Interlocked.Exchange(ref _returned, 1) == 0;
    }

    /// <summary>Straight-alpha RGBA overlay at Bounds on a canvas covering the visible video. Empty Pixels clears it.</summary>
    internal sealed record Subtitle(int CanvasWidth, int CanvasHeight, SubtitleBounds Bounds, byte[] Pixels)
    {
        public static Subtitle None { get; } = new(0, 0, default, []);
    }

    internal sealed record Frame(int Width, int Height, int RowBytes, byte[] Pixels, long PresentationTimestamp);

    /// <summary>The plugin's picture counters; on Linux "opaque" pictures are exported targets.</summary>
    internal readonly record struct PoolTelemetry(
        long TargetsPublished,
        long CpuPicturesReceived,
        long PoolGenerations,
        long DmaBufPoolGenerations,
        long CpuPoolGenerations,
        long TargetsLent,
        long TargetsReturned,
        long Uploads)
    {
        public long LentBalance => TargetsLent - TargetsReturned;

        public string ToMachineReadableString() =>
            "[dmabuf-pool] " +
            $"targets-published={TargetsPublished} " +
            $"cpu-pictures={CpuPicturesReceived} " +
            $"pool-generations={PoolGenerations} " +
            $"dmabuf-pool-generations={DmaBufPoolGenerations} " +
            $"cpu-pool-generations={CpuPoolGenerations} " +
            $"lent={TargetsLent} returned={TargetsReturned} " +
            $"lent-balance={LentBalance} uploads={Uploads}";
    }

    private readonly object _lifecycleGate = new();
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setFrameCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setDmaBufCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setSubtitleCallback;
    private readonly delegate* unmanaged[Cdecl]<VideoGeometry*, void> _getVideoGeometry;
    private readonly delegate* unmanaged[Cdecl]<long*, long*, long*, long*, long*, long*, long*, long*, void> _getPoolTelemetry;
    private readonly delegate* unmanaged[Cdecl]<ulong, int, void> _releaseFrame;
    private GCHandle _selfHandle;
    private nint _opaque;
    private DmaBufFrame? _latestDmaBuf;
    private Frame? _latestFrame;
    private Subtitle? _latestSubtitle;
    private bool _dmaBufConnected;
    private bool _framesConnected;
    private int _disposed;

    public DmaBufPluginBridge(bool registerFrameCallbacks = true)
    {
        nint library = NativeLibrary.Load(RepositoryPaths.PluginPath);
        _setFrameCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_frame_callback");
        _setDmaBufCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_dmabuf_frame_callback");
        _setSubtitleCallback = (delegate* unmanaged[Cdecl]<nint, nint, void>)NativeLibrary.GetExport(library, "vlc_avalonia_set_subtitle_callback");
        _getVideoGeometry = (delegate* unmanaged[Cdecl]<VideoGeometry*, void>)NativeLibrary.GetExport(library, "vlc_avalonia_get_video_geometry_v1");
        _getPoolTelemetry = (delegate* unmanaged[Cdecl]<long*, long*, long*, long*, long*, long*, long*, long*, void>)
            NativeLibrary.GetExport(library, "vlc_avalonia_get_pool_telemetry");
        _releaseFrame = (delegate* unmanaged[Cdecl]<ulong, int, void>)NativeLibrary.GetExport(library, "vlc_avalonia_release_dmabuf_frame");

        _selfHandle = GCHandle.Alloc(this);
        _opaque = GCHandle.ToIntPtr(_selfHandle);
        if (registerFrameCallbacks)
        {
            _setFrameCallback((nint)(delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)&OnFrame, _opaque);
            _setSubtitleCallback((nint)(delegate* unmanaged[Cdecl]<nint, int, int, int, int, int, int, nint, void>)&OnSubtitle, _opaque);
            _framesConnected = true;
        }
    }

    /// <summary>Asks displays opened from now on for DMA-BUF frames. Without it they publish CPU frames.</summary>
    public void EnableDmaBufFrames()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_dmaBufConnected)
                return;
            _setDmaBufCallback(
                (nint)(delegate* unmanaged[Cdecl]<nint, int, uint, ulong, uint, uint, ulong, int, int, ulong, int, long, void>)&OnDmaBufFrame,
                _opaque);
            _dmaBufConnected = true;
        }
    }

    public void DisableDmaBufFrames()
    {
        lock (_lifecycleGate)
        {
            if (_dmaBufConnected)
            {
                _setDmaBufCallback(0, 0);
                _dmaBufConnected = false;
            }
            Release(Interlocked.Exchange(ref _latestDmaBuf, null));
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

    public bool TryTakeLatestDmaBuf(out DmaBufFrame? frame)
    {
        frame = Interlocked.Exchange(ref _latestDmaBuf, null);
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

    /// <summary>
    /// Closes the frame's descriptor and returns its target to the video
    /// output. Safe to call more than once and after disposal.
    /// </summary>
    public void Release(DmaBufFrame? frame)
    {
        if (frame is null)
            return;
        frame.CloseDescriptor();
        if (frame.TakeReturn())
            _releaseFrame(frame.Generation, frame.Slot);
    }

    public PoolTelemetry ReadPoolTelemetry()
    {
        long published = 0, cpuPictures = 0, pools = 0, dmaBufPools = 0, cpuPools = 0, lent = 0, returned = 0, uploads = 0;
        _getPoolTelemetry(&published, &cpuPictures, &pools, &dmaBufPools, &cpuPools, &lent, &returned, &uploads);
        return new PoolTelemetry(published, cpuPictures, pools, dmaBufPools, cpuPools, lent, returned, uploads);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDmaBufFrame(
        nint opaque, int fd, uint drmFormat, ulong modifier, uint pitch, uint offset, ulong size,
        int width, int height, ulong generation, int slot, long presentationTimestamp)
    {
        DmaBufPluginBridge? bridge = null;
        DmaBufFrame? frame = null;
        try
        {
            bridge = GetBridge(opaque);
            if (bridge is null || bridge.IsDisposed || fd < 0 || width is <= 0 or > 16384 || height is <= 0 or > 16384)
            {
                bridge?._releaseFrame(generation, slot);
                return;
            }
            int duplicate = VaapiConverter.DuplicateFile(fd);
            frame = new DmaBufFrame(duplicate, drmFormat, modifier, pitch, offset, size, width, height, generation, slot, presentationTimestamp);
            if (duplicate < 0)
            {
                bridge.Release(frame);
                return;
            }
            bridge.Release(Interlocked.Exchange(ref bridge._latestDmaBuf, frame));
        }
        catch
        {
            if (bridge is not null && frame is not null)
                bridge.Release(frame);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(nint opaque, uint width, uint height, int stride, nint pixels, long presentationTimestamp)
    {
        try
        {
            DmaBufPluginBridge? bridge = GetBridge(opaque);
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
            DmaBufPluginBridge? bridge = GetBridge(opaque);
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

    private static DmaBufPluginBridge? GetBridge(nint opaque) =>
        opaque == 0 ? null : GCHandle.FromIntPtr(opaque).Target as DmaBufPluginBridge;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (IsDisposed)
                return;
            Volatile.Write(ref _disposed, 1);
            if (_dmaBufConnected)
            {
                _setDmaBufCallback(0, 0);
                _dmaBufConnected = false;
            }
            if (_framesConnected)
            {
                _setSubtitleCallback(0, 0);
                _setFrameCallback(0, 0);
                _framesConnected = false;
            }
            Release(Interlocked.Exchange(ref _latestDmaBuf, null));
            Interlocked.Exchange(ref _latestFrame, null);
            Interlocked.Exchange(ref _latestSubtitle, null);
            _opaque = 0;
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
        }
    }
}

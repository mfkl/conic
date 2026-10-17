using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Conic.Interop.Linux;

namespace Conic.Video.Linux;

/// <summary>
/// Decoded frames from the runtime's libvlc through its public video
/// callbacks, for the DMA-BUF probe: VLC decodes and converts to NV12 or P010
/// on the CPU, into buffers owned here. The latest displayed frame wins;
/// frames not yet taken when a newer one is displayed are dropped.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LibVlcFrameSource : IDisposable
{
    private const int BufferCount = 8;

    private enum BufferState { Free, Decoding, Ready, Taken }

    private readonly object _gate = new();
    private readonly BufferState[] _states = new BufferState[BufferCount];
    private readonly nint[] _buffers = new nint[BufferCount];
    private nint _spare;
    private readonly long[] _lockSequence = new long[BufferCount];
    private long _nextSequence;
    private readonly TaskCompletionSource<(int Width, int Height)> _format = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _frameReady = new(0);
    private readonly string _chroma;
    private readonly LibVlcApi _api = LibVlcApi.Runtime;
    private GCHandle _self;
    private nint _instance;
    private nint _player;
    private int _ready = -1;
    private int _lumaPitch;
    private int _lumaLines;

    public int FramesDisplayed;
    public int FramesDropped;
    public int Overruns;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LumaPitch => _lumaPitch;
    public int LumaLines => _lumaLines;
    public int ChromaPitch => _lumaPitch;

    /// <param name="chroma">"NV12" or "P010".</param>
    public unsafe LibVlcFrameSource(string path, string chroma)
    {
        _chroma = chroma;
        _self = GCHandle.Alloc(this);
        string[] arguments = ["--no-audio", "--avcodec-hw=none", "--no-video-title-show", "--input-repeat=65535",
            Environment.GetEnvironmentVariable("CONIC_PROBE_VLC_VERBOSE") == "1" ? "-vv" : "--quiet"];
        nint[] native = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        try
        {
            fixed (nint* argv = native)
                _instance = _api.New(native.Length, argv);
        }
        finally { foreach (nint argument in native) Marshal.FreeCoTaskMem(argument); }
        if (_instance == 0) throw new InvalidOperationException("libvlc_new failed.");
        nint pathUtf8 = Marshal.StringToCoTaskMemUTF8(path);
        nint media;
        try { media = _api.MediaNewPath(_instance, (byte*)pathUtf8); }
        finally { Marshal.FreeCoTaskMem(pathUtf8); }
        if (media == 0) throw new InvalidOperationException($"libvlc could not open {path}.");
        _player = _api.PlayerNewFromMedia(media);
        _api.MediaRelease(media);
        nint opaque = GCHandle.ToIntPtr(_self);
        _api.SetFormatCallbacks(_player, &Setup, &Cleanup);
        _api.SetCallbacks(_player, &Lock, &Unlock, &Display, opaque);
    }

    public Task<(int Width, int Height)> Format => _format.Task;

    /// <summary>The visible size once VLC knows it, which may be smaller than the buffers' (coded) size.</summary>
    public unsafe (int Width, int Height)? VisibleSize
    {
        get
        {
            uint width, height;
            return _api.GetSize(_player, 0, &width, &height) == 0 && width > 0 && height > 0 ? ((int)width & ~1, (int)height & ~1) : null;
        }
    }

    public unsafe void Play()
    {
        if (_api.Play(_player) != 0) throw new InvalidOperationException("libvlc_media_player_play failed.");
    }

    /// <summary>Waits for a displayed frame and takes it; release it with <see cref="Release"/>.</summary>
    public async Task<int> TakeAsync(CancellationToken cancellation)
    {
        while (true)
        {
            await _frameReady.WaitAsync(cancellation);
            lock (_gate)
            {
                if (_ready < 0) continue;
                int index = _ready;
                _ready = -1;
                _states[index] = BufferState.Taken;
                return index;
            }
        }
    }

    public unsafe byte* Luma(int index) => (byte*)_buffers[index];
    public unsafe byte* Chroma(int index) => (byte*)_buffers[index] + (long)_lumaPitch * _lumaLines;

    public void Release(int index)
    {
        lock (_gate) _states[index] = BufferState.Free;
    }

    public unsafe void Dispose()
    {
        if (_player != 0)
        {
            _api.Stop(_player);
            _api.PlayerRelease(_player);
            _player = 0;
        }
        if (_instance != 0)
        {
            _api.Release(_instance);
            _instance = 0;
        }
        for (int i = 0; i < BufferCount; i++)
        {
            if (_buffers[i] != 0) NativeMemory.AlignedFree((void*)_buffers[i]);
            _buffers[i] = 0;
        }
        if (_spare != 0) NativeMemory.AlignedFree((void*)_spare);
        _spare = 0;
        if (_self.IsAllocated) _self.Free();
    }

    private static LibVlcFrameSource From(nint opaque) => (LibVlcFrameSource)GCHandle.FromIntPtr(opaque).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe uint Setup(nint* opaque, byte* chroma, uint* width, uint* height, uint* pitches, uint* lines)
    {
        LibVlcFrameSource source = From(*opaque);
        int bytesPerSample = source._chroma == "P010" ? 2 : 1;
        for (int i = 0; i < 4; i++) chroma[i] = (byte)source._chroma[i];
        int w = (int)(*width & ~1u), h = (int)(*height & ~1u);
        *width = (uint)w;
        *height = (uint)h;
        int pitch = (w * bytesPerSample + 63) & ~63;
        int lumaLines = (h + 15) & ~15;
        pitches[0] = pitches[1] = (uint)pitch;
        lines[0] = (uint)lumaLines;
        lines[1] = (uint)(lumaLines / 2);
        lock (source._gate)
        {
            source.Width = w;
            source.Height = h;
            source._lumaPitch = pitch;
            source._lumaLines = lumaLines;
            for (int i = 0; i < BufferCount; i++)
            {
                if (source._buffers[i] != 0) NativeMemory.AlignedFree((void*)source._buffers[i]);
                source._buffers[i] = (nint)NativeMemory.AlignedAlloc((nuint)(pitch * lumaLines * 3 / 2), 64);
                source._states[i] = BufferState.Free;
            }
            if (source._spare != 0) NativeMemory.AlignedFree((void*)source._spare);
            source._spare = (nint)NativeMemory.AlignedAlloc((nuint)(pitch * lumaLines * 3 / 2), 64);
            source._ready = -1;
        }
        source._format.TrySetResult((w, h));
        return BufferCount;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Cleanup(nint opaque) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe nint Lock(nint opaque, nint* planes)
    {
        LibVlcFrameSource source = From(opaque);
        lock (source._gate)
        {
            int index = Array.IndexOf(source._states, BufferState.Free);
            if (index < 0 && source._ready >= 0)
            {
                index = source._ready;
                source._ready = -1;
                source.FramesDropped++;
            }
            if (index < 0)
            {
                index = -1;
                for (int i = 0; i < BufferCount; i++)
                    if (source._states[i] == BufferState.Decoding && (index < 0 || source._lockSequence[i] < source._lockSequence[index]))
                        index = i;
                source.Overruns++;
            }
            if (index < 0)
            {
                planes[0] = source._spare;
                planes[1] = source._spare + source._lumaPitch * source._lumaLines;
                return 0;
            }
            source._lockSequence[index] = ++source._nextSequence;
            source._states[index] = BufferState.Decoding;
            planes[0] = source._buffers[index];
            planes[1] = (nint)source.Chroma(index);
            return index + 1;
        }
    }

    /// <summary>VLC 3's vmem unlocks in its prepare step, before display, so nothing is released here.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void Unlock(nint opaque, nint picture, nint* planes) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Display(nint opaque, nint picture)
    {
        LibVlcFrameSource source = From(opaque);
        int index = (int)picture - 1;
        lock (source._gate)
        {
            if (index < 0)
            {
                source.FramesDropped++;
                return;
            }
            if (source._states[index] != BufferState.Decoding) return;
            if (source._ready >= 0)
            {
                source._states[source._ready] = BufferState.Free;
                source.FramesDropped++;
            }
            source._states[index] = BufferState.Ready;
            source._ready = index;
            source.FramesDisplayed++;
        }
        source._frameReady.Release();
    }
}

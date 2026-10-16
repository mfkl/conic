using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Module;
using VLCLR.Native;
using Conic.Interop.Linux;

namespace Conic.Vout;

/// <summary>
/// Linux VLC vout that hands each frame to the app as a DMA-BUF: VLC's
/// pictures (NV12, or P010 for 10-bit sources) are copied into a VA-API
/// surface, converted to BGRA by VA-API video processing on the GPU, and the
/// app imports the exported target into Avalonia's compositor.
/// </summary>
/// <remarks>
/// Distribution builds of VLC 3 before 3.0.22 have no VA-API decoder, so
/// pictures arrive in system memory: the copy into the VA surface is the one
/// CPU step. A ring of exported targets is lent to the app one frame at a
/// time and returned with <see cref="ReleaseDmaBufFrame"/> once the compositor
/// has taken its copy. Without a registered DMA-BUF consumer, or when VA-API
/// cannot be opened, the display asks for BGRA and publishes CPU frames.
/// </remarks>
[SupportedOSPlatform("linux")]
public static unsafe class VaapiVoutPlugin
{
    private const int Success = 0;
    private const int GenericError = -1;
    private const int RingSize = 3;

    private const uint ChromaNv12 = VaApi.FourCCNv12;
    private const uint ChromaP010 = VaApi.FourCCP010;
    private const uint ChromaI0AL = 0x4C413049;
    private const uint ChromaI0AB = 0x42413049;
    private const uint ChromaP016 = 0x36313050;

    private static readonly object CallbackGate = VoutHost.CallbackGate;
    private static nint s_dmaBufCallback;
    private static nint s_dmaBufCallbackOpaque;
    private static readonly object DisplaysGate = new();
    private static readonly Dictionary<ulong, DisplayState> s_displays = [];
    private static ulong s_lastGeneration;

    private sealed class DisplayState
    {
        public VLCVideoFormat OriginalSource;
        public nint Pool;
        public object PoolGate { get; } = new();
        public VoutHost.SubtitleState Subtitle { get; } = new();
        public VaapiConverter? Converter;
        public ExportedSurface[] Exports = [];
        /// <summary>Targets the app holds; guarded by the state itself.</summary>
        public bool[] Lent = new bool[RingSize];
        public int Prepared = -1;
        public int Next;
        public ulong Generation;
        public int ColorStandard;
        public bool FullRange;
    }

    internal static int Register(nint vlcSet, nint opaque) =>
        ModuleBuilder.Create(vlcSet, opaque)
            .WithName("vlc_avalonia")
            .WithShortcut("vlc_avalonia")
            .WithShortName("Conic")
            .WithDescription("Avalonia DMA-BUF composition video output")
            .WithCapability("vout display")
            .WithScore(0)
            .WithOpenCallback(&Open, "VaapiVoutOpen")
            .WithCloseCallback(&Close, "VaapiVoutClose")
            .WithNoUnload()
            .Register();

    /// <summary>
    /// Registers the consumer of GPU frames, invoked synchronously from VLC's
    /// display thread with <c>(opaque, fd, drmFormat, drmModifier, pitch,
    /// offset, size, width, height, generation, slot, pts)</c>. The descriptor
    /// belongs to the video output and is valid only during the call; the
    /// consumer duplicates it to keep it. The target stays unchanged until
    /// the consumer returns it with <see cref="ReleaseDmaBufFrame"/>. The
    /// generation changes when a new display opens. Displays opened while a
    /// consumer is registered use DMA-BUFs. Passing zero disconnects.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_avalonia_set_dmabuf_frame_callback", CallConvs = [typeof(CallConvCdecl)])]
    public static void SetDmaBufFrameCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_dmaBufCallback = callback;
            s_dmaBufCallbackOpaque = opaque;
        }
    }

    /// <summary>Returns a target lent by the frame callback. Unknown or stale frames are ignored.</summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_avalonia_release_dmabuf_frame", CallConvs = [typeof(CallConvCdecl)])]
    public static void ReleaseDmaBufFrame(ulong generation, int slot)
    {
        DisplayState? state;
        lock (DisplaysGate)
            s_displays.TryGetValue(generation, out state);
        if (state is null || slot is < 0 or >= RingSize)
            return;
        lock (state)
        {
            if (!state.Lent[slot])
                return;
            state.Lent[slot] = false;
        }
        PicturePoolTelemetry.OpaquePictureReleased();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(nint displayPtr)
    {
        if (displayPtr == 0)
            return GenericError;

        VLCVoutDisplay* display = (VLCVoutDisplay*)displayPtr;
        if (IsOpaqueHardwareChroma(display->Source.Chroma))
            return GenericError;
        GCHandle stateHandle = default;
        DisplayState state = new() { OriginalSource = display->Source };
        try
        {
            bool useDmaBuf;
            lock (CallbackGate)
                useDmaBuf = s_dmaBufCallback != 0;

            display->Format = display->Source;
            if (useDmaBuf && TryOpenConverter(state, in display->Source, out uint chroma))
            {
                display->Format.Chroma = chroma;
            }
            else
            {
                display->Format.Chroma = VLCFourCC.BGRA;
                display->Format.BitsPerPixel = 32;
                display->Format.RedMask = 0x00ff0000;
                display->Format.GreenMask = 0x0000ff00;
                display->Format.BlueMask = 0x000000ff;
            }

            display->Info = new VLCVoutDisplayInfo
            {
                IsSlow = 0,
                HasDoubleClick = 0,
                NeedsHideMouse = 0,
                HasPicturesInvalid = 0,
                SubpictureChromas = VoutHost.SubpictureChromas,
            };

            stateHandle = GCHandle.Alloc(state);
            display->Pool = (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint>)&GetPool;
            display->Prepare = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Prepare;
            display->Display = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Display;
            display->Control = (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, int>)&Control;
            display->Manage = 0;
            display->Sys = GCHandle.ToIntPtr(stateHandle);
            if (state.Converter is not null)
                lock (DisplaysGate)
                    s_displays[state.Generation] = state;
            VoutHost.PublishGeometry(display, state.OriginalSource, display->Configuration);
            return Success;
        }
        catch
        {
            state.Converter?.Dispose();
            if (stateHandle.IsAllocated)
                stateHandle.Free();
            display->Sys = 0;
            return GenericError;
        }
    }

    private static bool IsOpaqueHardwareChroma(uint chroma) =>
        VLCFourCC.IsVaapiOpaque(chroma) ||
        (chroma & 0x00FFFFFF) == 0x00564456;

    /// <summary>Opens VA-API for the source's decoded size and exports the ring; false leaves the CPU path.</summary>
    private static bool TryOpenConverter(DisplayState state, in VLCVideoFormat source, out uint chroma)
    {
        bool tenBit = source.Chroma is ChromaI0AL or ChromaI0AB or ChromaP010 or ChromaP016 or VLCFourCC.VaapiOpaque420TenBit;
        chroma = tenBit ? ChromaP010 : ChromaNv12;
        uint visibleWidth = source.VisibleWidth != 0 ? source.VisibleWidth : source.Width;
        uint visibleHeight = source.VisibleHeight != 0 ? source.VisibleHeight : source.Height;
        if (source.Width == 0 || source.Height == 0 ||
            (ulong)source.XOffset + visibleWidth > source.Width || (ulong)source.YOffset + visibleHeight > source.Height)
            return false;
        VaapiConverter? converter = null;
        try
        {
            converter = VaapiConverter.Open((int)source.Width, (int)source.Height,
                (int)source.XOffset, (int)source.YOffset, (int)visibleWidth, (int)visibleHeight, chroma, RingSize);
            ExportedSurface[] exports = new ExportedSurface[RingSize];
            for (int i = 0; i < RingSize; i++)
                exports[i] = converter.Export(i);
            state.Converter = converter;
            state.Exports = exports;
            state.ColorStandard = VaapiConverter.ColorStandardFor(source.Space, (int)visibleHeight);
            state.FullRange = source.ColorRangeFull != 0;
            state.Generation = Interlocked.Increment(ref s_lastGeneration);
            return true;
        }
        catch
        {
            converter?.Dispose();
            return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Close(nint displayPtr)
    {
        VoutHost.ClearGeometry(displayPtr);
        if (displayPtr == 0)
            return;

        VLCVoutDisplay* display = (VLCVoutDisplay*)displayPtr;
        nint statePointer = display->Sys;
        display->Sys = 0;
        if (statePointer == 0)
            return;

        GCHandle stateHandle = GCHandle.FromIntPtr(statePointer);
        try
        {
            if (stateHandle.Target is DisplayState state)
            {
                lock (DisplaysGate)
                    s_displays.Remove(state.Generation);
                VoutHost.PublishSubtitle(state.Subtitle, 0);
                if (state.Pool != 0)
                {
                    VLCCore.PicturePoolRelease(state.Pool);
                    state.Pool = 0;
                }
                state.Converter?.Dispose();
                state.Converter = null;
            }
        }
        catch
        {
        }
        finally
        {
            if (stateHandle.IsAllocated)
                stateHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint GetPool(nint displayPtr, uint requestedPictures)
    {
        try
        {
            VLCVoutDisplay* display = (VLCVoutDisplay*)displayPtr;
            DisplayState? state = GetState(display);
            if (state is null)
                return 0;
            lock (state.PoolGate)
            {
                if (state.Pool != 0)
                    return state.Pool;
                state.Pool = VLCCore.PicturePoolNewFromFormat(in display->Format, Math.Clamp(requestedPictures, 3u, 8u));
                if (state.Pool != 0)
                {
                    if (state.Converter is not null)
                        PicturePoolTelemetry.OpaquePoolCreated();
                    else
                        PicturePoolTelemetry.CpuPoolCreated();
                }
                return state.Pool;
            }
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Copies and converts ahead of the display date, into a target the app does not hold.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Prepare(nint displayPtr, nint picturePtr, nint subpicturePtr)
    {
        try
        {
            DisplayState? state = displayPtr != 0 ? GetState((VLCVoutDisplay*)displayPtr) : null;
            if (state?.Converter is not { } converter || picturePtr == 0)
                return;
            state.Prepared = -1;
            int slot = -1;
            lock (state)
            {
                for (int i = 0; i < RingSize && slot < 0; i++)
                {
                    int candidate = (state.Next + i) % RingSize;
                    if (!state.Lent[candidate]) slot = candidate;
                }
            }
            if (slot < 0)
                return;

            VLCPicture* picture = (VLCPicture*)picturePtr;
            VLCPlane luma = picture->Plane0, chroma = picture->Plane1;
            if (luma.Pixels == 0 || chroma.Pixels == 0)
                return;
            converter.Upload((byte*)luma.Pixels, luma.Pitch, luma.Lines, (byte*)chroma.Pixels, chroma.Pitch, chroma.Lines);
            PicturePoolTelemetry.CpuUpload();
            converter.Convert(slot, state.ColorStandard, state.FullRange);
            state.Prepared = slot;
            state.Next = (slot + 1) % RingSize;
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Display(nint displayPtr, nint picturePtr, nint subpicturePtr)
    {
        DisplayState? state = null;
        try
        {
            state = displayPtr != 0 ? GetState((VLCVoutDisplay*)displayPtr) : null;
            if (picturePtr != 0 && state is not null)
            {
                VLCPicture* picture = (VLCPicture*)picturePtr;
                PicturePoolTelemetry.CpuPictureReceived();
                if (state.Converter is not null)
                {
                    int slot = state.Prepared;
                    state.Prepared = -1;
                    if (slot >= 0)
                        PublishDmaBuf(state, slot, picture->Date);
                }
                else
                {
                    PublishCpuPicture(picture);
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (picturePtr != 0)
                VLCCore.PictureRelease(picturePtr);
            VoutHost.PublishSubtitle(state?.Subtitle, subpicturePtr);
            if (subpicturePtr != 0)
                VLCCore.SubpictureDelete(subpicturePtr);
        }
    }

    private static void PublishDmaBuf(DisplayState state, int slot, long presentationTimestamp)
    {
        ExportedSurface export = state.Exports[slot];
        VaapiConverter converter = state.Converter!;
        lock (CallbackGate)
        {
            if (s_dmaBufCallback == 0)
                return;
            lock (state)
                state.Lent[slot] = true;
            PicturePoolTelemetry.OpaquePictureReceived();
            PicturePoolTelemetry.OpaquePictureAllocated();
            var callback = (delegate* unmanaged[Cdecl]<nint, int, uint, ulong, uint, uint, ulong, int, int, ulong, int, long, void>)s_dmaBufCallback;
            callback(s_dmaBufCallbackOpaque, export.Fd, export.DrmFormat, export.Modifier, export.Pitch, export.Offset, export.Size,
                converter.TargetWidth, converter.TargetHeight, state.Generation, slot, presentationTimestamp);
        }
    }

    private static void PublishCpuPicture(VLCPicture* picture)
    {
        VLCPlane plane = picture->Plane0;
        uint width = picture->Format.VisibleWidth != 0 ? picture->Format.VisibleWidth : picture->Format.Width;
        uint height = picture->Format.VisibleHeight != 0 ? picture->Format.VisibleHeight : picture->Format.Height;
        if (plane.Pixels == 0 || width == 0 || height == 0 || plane.Pitch == 0)
            return;
        nint pixels = plane.Pixels
            + checked((nint)picture->Format.YOffset * plane.Pitch)
            + checked((nint)picture->Format.XOffset * 4);
        VoutHost.PublishFrame(width, height, plane.Pitch, pixels, picture->Date);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Control(nint displayPtr, int query, nint arguments)
    {
        var display = (VLCVoutDisplay*)displayPtr;
        return GetState(display) is { } state
            ? VoutHost.Control(display, state.OriginalSource, query, arguments)
            : Success;
    }

    private static DisplayState? GetState(VLCVoutDisplay* display)
    {
        if (display->Sys == 0)
            return null;
        return GCHandle.FromIntPtr(display->Sys).Target as DisplayState;
    }
}

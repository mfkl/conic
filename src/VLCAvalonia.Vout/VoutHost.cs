using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCAvalonia.Interop;

namespace VLCAvalonia.Vout;

/// <summary>
/// The host-facing parts every platform's video output shares: CPU frames,
/// the subtitle overlay and the video geometry, with the exports the app uses
/// to subscribe to them.
/// </summary>
internal static unsafe class VoutHost
{
    // Registration and invocation share this gate. A disconnect therefore
    // waits for an in-flight callback to return before the host frees its
    // callback userdata.
    internal static readonly object CallbackGate = new();
    private static VideoGeometry s_geometry;
    private static nint s_geometryOwner;
    private static nint s_frameCallback;
    private static nint s_frameCallbackOpaque;
    private static nint s_subtitleCallback;
    private static nint s_subtitleCallbackOpaque;

    // Zero-terminated chroma list VLC reads for the display's lifetime; the
    // module is never unloaded. Accepting RGBA subpictures makes VLC hand us
    // rendered subtitles instead of blending them into the (often GPU) frame.
    internal static readonly nint SubpictureChromas = CreateSubpictureChromas();

    private static nint CreateSubpictureChromas()
    {
        uint* chromas = (uint*)NativeMemory.Alloc(2, sizeof(uint));
        chromas[0] = VLCFourCC.RGBA;
        chromas[1] = 0;
        return (nint)chromas;
    }

    /// <summary>
    /// Registers the consumer invoked synchronously while a VLC picture is
    /// valid. Passing zero disconnects the consumer.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_frame_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetFrameCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_frameCallback = callback;
            s_frameCallbackOpaque = opaque;
        }
    }

    /// <summary>
    /// Registers the consumer of the subtitle overlay: straight-alpha RGBA rows
    /// (width * 4 bytes) placed at x/y on a canvas covering the visible video.
    /// Invoked synchronously while the pixels are valid, only when the overlay
    /// changes; width or height 0 clears it. Passing zero disconnects.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_subtitle_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetSubtitleCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_subtitleCallback = callback;
            s_subtitleCallbackOpaque = opaque;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vlc_avalonia_get_video_geometry_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static void GetVideoGeometry(void* destination)
    {
        if (destination == null) return;
        lock (CallbackGate) *(VideoGeometry*)destination = s_geometry;
    }

    /// <summary>
    /// Copies process-wide picture-pool counters into caller-owned storage.
    /// No raw pointer or handle is retained by the plugin.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_get_pool_telemetry",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void GetPoolTelemetry(
        long* opaquePicturesReceived,
        long* cpuPicturesReceived,
        long* poolGenerations,
        long* opaquePoolGenerations,
        long* cpuPoolGenerations,
        long* opaquePicturesAllocated,
        long* opaquePicturesReleased,
        long* cpuUploads)
    {
        try
        {
            PicturePoolTelemetrySnapshot snapshot =
                PicturePoolTelemetry.Snapshot();
            if (opaquePicturesReceived is not null)
                *opaquePicturesReceived = snapshot.OpaquePicturesReceived;
            if (cpuPicturesReceived is not null)
                *cpuPicturesReceived = snapshot.CpuPicturesReceived;
            if (poolGenerations is not null)
                *poolGenerations = snapshot.PoolGenerations;
            if (opaquePoolGenerations is not null)
                *opaquePoolGenerations = snapshot.OpaquePoolGenerations;
            if (cpuPoolGenerations is not null)
                *cpuPoolGenerations = snapshot.CpuPoolGenerations;
            if (opaquePicturesAllocated is not null)
                *opaquePicturesAllocated = snapshot.OpaquePicturesAllocated;
            if (opaquePicturesReleased is not null)
                *opaquePicturesReleased = snapshot.OpaquePicturesReleased;
            if (cpuUploads is not null)
                *cpuUploads = snapshot.CpuUploads;
        }
        catch
        {
        }
    }

    /// <summary>The subtitle overlay last published by one display.</summary>
    internal sealed class SubtitleState
    {
        public SubtitleRegion[] Regions = new SubtitleRegion[16];
        public byte[] Scratch = [];
        public byte[] Published = [];
        public SubtitleBounds Bounds;
        public int CanvasWidth;
        public int CanvasHeight;
    }

    /// <summary>
    /// Flattens the RGBA regions VLC rendered for this frame and publishes the
    /// overlay when it differs from the last one. A null subpicture clears it.
    /// </summary>
    internal static void PublishSubtitle(SubtitleState? state, nint subpicturePtr)
    {
        if (state is null)
            return;
        try
        {
            int count = 0, canvasWidth = 0, canvasHeight = 0;
            if (subpicturePtr != 0)
            {
                VLCSubpicture* subpicture = (VLCSubpicture*)subpicturePtr;
                canvasWidth = subpicture->OriginalPictureWidth;
                canvasHeight = subpicture->OriginalPictureHeight;
                for (nint next = subpicture->Region; next != 0 && count < state.Regions.Length; next = ((VLCSubpictureRegion*)next)->Next)
                {
                    VLCSubpictureRegion* region = (VLCSubpictureRegion*)next;
                    if (region->Picture == 0 || region->Format.Chroma != VLCFourCC.RGBA)
                        continue;
                    VLCPlane plane = ((VLCPicture*)region->Picture)->Plane0;
                    if (plane.Pixels == 0)
                        continue;
                    state.Regions[count++] = new SubtitleRegion(
                        region->X, region->Y,
                        (int)region->Format.VisibleWidth, (int)region->Format.VisibleHeight,
                        plane.Pitch,
                        plane.Pixels + (nint)region->Format.YOffset * plane.Pitch + (nint)region->Format.XOffset * 4,
                        region->Alpha);
                }
            }

            Span<SubtitleRegion> regions = state.Regions.AsSpan(0, SubtitleComposer.Clip(state.Regions.AsSpan(0, count), canvasWidth, canvasHeight));
            SubtitleBounds bounds = SubtitleComposer.Bounds(regions);
            if (state.Scratch.Length != bounds.ByteCount)
                state.Scratch = new byte[bounds.ByteCount];
            SubtitleComposer.Compose(regions, bounds, state.Scratch);
            if (bounds.IsEmpty)
                canvasWidth = canvasHeight = 0;
            if (bounds == state.Bounds && canvasWidth == state.CanvasWidth && canvasHeight == state.CanvasHeight &&
                state.Scratch.AsSpan().SequenceEqual(state.Published))
                return;

            (state.Published, state.Scratch) = (state.Scratch, state.Published);
            state.Bounds = bounds;
            state.CanvasWidth = canvasWidth;
            state.CanvasHeight = canvasHeight;
            InvokeSubtitleCallback(canvasWidth, canvasHeight, bounds, state.Published);
        }
        catch
        {
        }
    }

    private static void InvokeSubtitleCallback(int canvasWidth, int canvasHeight, SubtitleBounds bounds, byte[] pixels)
    {
        lock (CallbackGate)
        {
            if (s_subtitleCallback == 0)
                return;
            var callback = (delegate* unmanaged[Cdecl]<nint, int, int, int, int, int, int, nint, void>)s_subtitleCallback;
            fixed (byte* data = pixels)
                callback(s_subtitleCallbackOpaque, canvasWidth, canvasHeight, bounds.X, bounds.Y, bounds.Width, bounds.Height, (nint)data);
        }
    }

    internal static void PublishFrame(
        uint width,
        uint height,
        int stride,
        nint pixels,
        long presentationTimestamp)
    {
        lock (CallbackGate)
        {
            if (s_frameCallback == 0)
                return;
            var callback =
                (delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)s_frameCallback;
            callback(
                s_frameCallbackOpaque,
                width,
                height,
                stride,
                pixels,
                presentationTimestamp);
        }
    }

    /// <summary>
    /// Handles the display control queries that change the video geometry.
    /// <paramref name="original"/> is the source format the display opened with.
    /// </summary>
    internal static int Control(VLCVoutDisplay* display, in VLCVideoFormat original, int query, nint arguments)
    {
        switch (VLCVoutDisplayControls.FromNative(query))
        {
            case VLCVoutDisplayControl.ChangeSourceAspect:
            case VLCVoutDisplayControl.ChangeSourceCrop:
                PublishGeometry(display, original, 0);
                break;
            case VLCVoutDisplayControl.ChangeZoom:
            case VLCVoutDisplayControl.ChangeDisplayFilled:
            case VLCVoutDisplayControl.ChangeDisplaySize:
                // The first variadic argument is the display configuration.
                if (arguments != 0) PublishGeometry(display, original, VLCVaList.PeekPointer(arguments));
                break;
        }
        return 0;
    }

    /// <param name="configuration">A <c>const vout_display_cfg_t *</c>, or zero to keep the scaling.</param>
    internal static void PublishGeometry(VLCVoutDisplay* display, in VLCVideoFormat original, nint configuration)
    {
        var source = display->Source;
        if (original.VisibleWidth == 0 || original.VisibleHeight == 0) return;
        lock (CallbackGate)
        {
            s_geometryOwner = (nint)display;
            s_geometry.X = ((double)source.XOffset - original.XOffset) / original.VisibleWidth;
            s_geometry.Y = ((double)source.YOffset - original.YOffset) / original.VisibleHeight;
            s_geometry.Width = (double)source.VisibleWidth / original.VisibleWidth;
            s_geometry.Height = (double)source.VisibleHeight / original.VisibleHeight;
            s_geometry.SampleAspect = source.SarNum > 0 && source.SarDen > 0 ? (double)source.SarNum / source.SarDen : 1;
            s_geometry.Active = 1;
            if (configuration != 0)
            {
                s_geometry.AutoScale = VLCVoutDisplayControls.IsDisplayFilled(configuration) ? 1 : 0;
                (int numerator, int denominator) = VLCVoutDisplayControls.Zoom(configuration);
                s_geometry.Zoom = denominator > 0 ? (double)numerator / denominator : 1;
            }
        }
    }

    /// <summary>Clears the geometry if <paramref name="displayPtr"/> published it.</summary>
    internal static void ClearGeometry(nint displayPtr)
    {
        lock (CallbackGate)
        {
            if (s_geometryOwner == displayPtr) { s_geometry = default; s_geometryOwner = 0; }
        }
    }
}

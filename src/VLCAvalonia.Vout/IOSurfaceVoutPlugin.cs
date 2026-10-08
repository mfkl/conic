using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Module;
using VLCLR.Native;
using VLCAvalonia.Interop.MacOS;

namespace VLCAvalonia.Vout;

/// <summary>
/// macOS VLC vout that lends each frame's IOSurface-backed CoreVideo pixel
/// buffer to the app, which imports the IOSurface into Avalonia's compositor.
/// </summary>
/// <remarks>
/// The display asks VLC for <c>VLC_CODEC_CVPX_BGRA</c>. VideoToolbox decodes
/// to NV12 pixel buffers that VLC's cvpx converter turns into BGRA with
/// <c>VTPixelTransferSession</c>; software decoding is converted to BGRA and
/// uploaded to a pixel buffer by the same module. VLC allocates those buffers
/// from IOSurface-backed pools, so no frame is copied on the CPU here. Without
/// a registered surface consumer the display asks for plain BGRA and publishes
/// CPU frames instead.
/// </remarks>
[SupportedOSPlatform("macos")]
public static unsafe class IOSurfaceVoutPlugin
{
    private const int Success = 0;
    private const int GenericError = -1;
    // VLC's OpenGL output keeps at most this many CoreVideo pictures.
    private const uint MaximumPixelBufferPictures = 128;

    private static readonly object CallbackGate = VoutHost.CallbackGate;
    private static nint s_surfaceCallback;
    private static nint s_surfaceCallbackOpaque;

    private sealed class DisplayState
    {
        public VLCVideoFormat OriginalSource;
        public nint Pool;
        public bool UsesPixelBuffers;
        public ulong PoolGeneration;
        public object PoolGate { get; } = new();
        public VoutHost.SubtitleState Subtitle { get; } = new();
    }

    internal static int Register(nint vlcSet, nint opaque) =>
        ModuleBuilder.Create(vlcSet, opaque)
            .WithName("vlc_avalonia")
            .WithShortcut("vlc_avalonia")
            .WithShortName("VLC Avalonia")
            .WithDescription("Avalonia IOSurface composition video output")
            .WithCapability("vout display")
            .WithScore(0)
            .WithOpenCallback(&Open, "IOSurfaceVoutOpen")
            .WithCloseCallback(&Close, "IOSurfaceVoutClose")
            .WithNoUnload()
            .Register();

    /// <summary>
    /// Registers the consumer of GPU frames, invoked synchronously from VLC's
    /// display thread with <c>(opaque, CVPixelBufferRef, IOSurfaceRef,
    /// surfaceWidth, surfaceHeight, visibleX, visibleY, visibleWidth,
    /// visibleHeight, pts, poolGeneration)</c>; the pool generation changes
    /// when a new display opens. The consumer owns one reference on the pixel
    /// buffer and returns it with <see cref="ReleasePixelBuffer"/> once the
    /// compositor no longer reads the surface; the IOSurface stays valid while
    /// the buffer is held. Displays opened while a consumer is registered use
    /// pixel buffers. Passing zero disconnects.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_iosurface_frame_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetSurfaceFrameCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_surfaceCallback = callback;
            s_surfaceCallbackOpaque = opaque;
        }
    }

    /// <summary>Returns a pixel buffer lent by the frame callback.</summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_release_pixel_buffer",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void ReleasePixelBuffer(nint pixelBuffer)
    {
        if (pixelBuffer == 0)
            return;
        CoreVideo.CFRelease(pixelBuffer);
        PicturePoolTelemetry.OpaquePictureReleased();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(nint displayPtr)
    {
        if (displayPtr == 0)
            return GenericError;

        VLCVoutDisplay* display = (VLCVoutDisplay*)displayPtr;
        GCHandle stateHandle = default;
        try
        {
            bool usePixelBuffers;
            lock (CallbackGate)
                usePixelBuffers = s_surfaceCallback != 0;

            DisplayState state = new()
            {
                OriginalSource = display->Source,
                UsesPixelBuffers = usePixelBuffers,
            };
            stateHandle = GCHandle.Alloc(state);

            display->Format = display->Source;
            if (usePixelBuffers)
            {
                display->Format.Chroma = VLCFourCC.CvpxBGRA;
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
                IsSlow = usePixelBuffers ? (byte)0 : (byte)1,
                HasDoubleClick = 0,
                NeedsHideMouse = 0,
                HasPicturesInvalid = 0,
                SubpictureChromas = VoutHost.SubpictureChromas,
            };

            display->Pool = (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint>)&GetPool;
            display->Prepare = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Prepare;
            display->Display = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Display;
            display->Control = (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, int>)&Control;
            display->Manage = 0;
            display->Sys = GCHandle.ToIntPtr(stateHandle);
            VoutHost.PublishGeometry(display, state.OriginalSource, display->Configuration);
            return Success;
        }
        catch
        {
            if (stateHandle.IsAllocated)
                stateHandle.Free();
            display->Sys = 0;
            return GenericError;
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
                VoutHost.PublishSubtitle(state.Subtitle, 0);
                if (state.Pool != 0)
                {
                    VLCCore.PicturePoolRelease(state.Pool);
                    state.Pool = 0;
                }
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
                if (state.UsesPixelBuffers)
                {
                    // CoreVideo chromas have no planes: these pictures carry no
                    // memory, and the decoder or converter attaches a pixel
                    // buffer to each as its picture context.
                    state.Pool = VLCCore.PicturePoolNewFromFormat(
                        in display->Format,
                        Math.Clamp(requestedPictures, 3u, MaximumPixelBufferPictures));
                    if (state.Pool != 0)
                        state.PoolGeneration = PicturePoolTelemetry.OpaquePoolCreated();
                }
                else
                {
                    state.Pool = VLCCore.PicturePoolNewFromFormat(
                        in display->Format,
                        Math.Clamp(requestedPictures, 3u, 8u));
                    if (state.Pool != 0)
                        state.PoolGeneration = PicturePoolTelemetry.CpuPoolCreated();
                }
                return state.Pool;
            }
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Prepare(nint displayPtr, nint picturePtr, nint subpicturePtr)
    {
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Display(nint displayPtr, nint picturePtr, nint subpicturePtr)
    {
        try
        {
            if (picturePtr != 0)
            {
                VLCPicture* picture = (VLCPicture*)picturePtr;
                if (VLCFourCC.IsCvpx(picture->Format.Chroma))
                {
                    PicturePoolTelemetry.OpaquePictureReceived();
                    nint context = picture->Context;
                    nint pixelBuffer = context != 0 ? ((VLCCvpxPictureContext*)context)->PixelBuffer : 0;
                    if (pixelBuffer != 0)
                        PublishPixelBuffer(pixelBuffer, picture, GetState((VLCVoutDisplay*)displayPtr)?.PoolGeneration ?? 0);
                }
                else
                {
                    PicturePoolTelemetry.CpuPictureReceived();
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
            if (displayPtr != 0)
                VoutHost.PublishSubtitle(GetState((VLCVoutDisplay*)displayPtr)?.Subtitle, subpicturePtr);
            if (subpicturePtr != 0)
                VLCCore.SubpictureDelete(subpicturePtr);
        }
    }

    private static void PublishPixelBuffer(nint pixelBuffer, VLCPicture* picture, ulong poolGeneration)
    {
        nint surface = CoreVideo.CVPixelBufferGetIOSurface(pixelBuffer);
        if (surface == 0)
        {
            // Not shareable with the GPU: read it on the CPU instead.
            PublishMappedPixelBuffer(pixelBuffer, picture->Date);
            return;
        }

        uint surfaceWidth = (uint)CoreVideo.IOSurfaceGetWidth(surface);
        uint surfaceHeight = (uint)CoreVideo.IOSurfaceGetHeight(surface);
        VisibleRectangle(in picture->Format, surfaceWidth, surfaceHeight,
            out uint x, out uint y, out uint width, out uint height);
        lock (CallbackGate)
        {
            if (s_surfaceCallback == 0)
                return;
            CoreVideo.CFRetain(pixelBuffer);
            PicturePoolTelemetry.OpaquePictureAllocated();
            var callback = (delegate* unmanaged[Cdecl]<nint, nint, nint, uint, uint, uint, uint, uint, uint, long, ulong, void>)s_surfaceCallback;
            callback(s_surfaceCallbackOpaque, pixelBuffer, surface, surfaceWidth, surfaceHeight, x, y, width, height, picture->Date, poolGeneration);
        }
    }

    /// <summary>
    /// The picture's visible area inside the surface. VLC sizes converted
    /// buffers to the visible area, so offsets that do not fit are ignored.
    /// </summary>
    private static void VisibleRectangle(in VLCVideoFormat format, uint surfaceWidth, uint surfaceHeight,
        out uint x, out uint y, out uint width, out uint height)
    {
        width = format.VisibleWidth != 0 ? format.VisibleWidth : format.Width;
        height = format.VisibleHeight != 0 ? format.VisibleHeight : format.Height;
        x = (ulong)format.XOffset + width <= surfaceWidth ? format.XOffset : 0;
        y = (ulong)format.YOffset + height <= surfaceHeight ? format.YOffset : 0;
        width = Math.Min(width, surfaceWidth - x);
        height = Math.Min(height, surfaceHeight - y);
    }

    private static void PublishMappedPixelBuffer(nint pixelBuffer, long presentationTimestamp)
    {
        if (CoreVideo.CVPixelBufferGetPixelFormatType(pixelBuffer) != CoreVideo.PixelFormat32BGRA ||
            CoreVideo.CVPixelBufferLockBaseAddress(pixelBuffer, CoreVideo.ReadOnlyLock) != CoreVideo.Success)
            return;
        try
        {
            PicturePoolTelemetry.CpuUpload();
            VoutHost.PublishFrame(
                (uint)CoreVideo.CVPixelBufferGetWidth(pixelBuffer),
                (uint)CoreVideo.CVPixelBufferGetHeight(pixelBuffer),
                checked((int)CoreVideo.CVPixelBufferGetBytesPerRow(pixelBuffer)),
                CoreVideo.CVPixelBufferGetBaseAddress(pixelBuffer),
                presentationTimestamp);
        }
        finally
        {
            CoreVideo.CVPixelBufferUnlockBaseAddress(pixelBuffer, CoreVideo.ReadOnlyLock);
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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using VLCLR.Module;
using VLCLR.Native;
using Conic.Interop;

namespace Conic.Vout;

/// <summary>
/// VLC vout that publishes video through a keyed-mutex D3D11 shared texture.
/// The CPU callback remains available as a fallback and diagnostic path.
/// </summary>
public static unsafe class CompositionVoutPlugin
{
    private const int Success = 0;
    private const int GenericError = -1;
    private const uint I42010LittleEndian = 0x4C413049; // "I0AL"
    private const uint P010 = 0x30313050; // "P010"

    private static readonly nint Copyright =
        Marshal.StringToCoTaskMemUTF8("Copyright (c) VideoLabs");
    private static readonly nint License =
        Marshal.StringToCoTaskMemUTF8("LGPL-2.1-or-later");

    // Registration and invocation share this gate. A disconnect therefore
    // waits for an in-flight callback to return before the host frees its
    // callback userdata.
    private static readonly object CallbackGate = new();
    private static VideoGeometry s_geometry;
    private static nint s_geometryOwner;
    private static nint s_frameCallback;
    private static nint s_frameCallbackOpaque;
    private static nint s_GPUFrameCallback;
    private static nint s_GPUFrameCallbackOpaque;
    private static nint s_playlistCallback;
    private static nint s_playlistCallbackOpaque;
    private static nint s_telemetryCallback;
    private static nint s_telemetryCallbackOpaque;
    private static bool s_adapterConfigured;
    private static bool s_forceCPUOutput;
    private static uint s_adapterLowPart;
    private static int s_adapterHighPart;

    private static readonly nint LibVLCCore =
        NativeLibrary.Load("libvlccore.dll");
    private static readonly delegate* unmanaged[Cdecl]<nint, void>
        PictureRelease =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(
                LibVLCCore,
                "picture_Release");

    private sealed class DisplayState
    {
        public VLCVideoFormat OriginalSource;
        public nint Pool;
        public D3D11SharedTexture? SharedTexture;
        public D3D11VideoProcessorConverter? Converter;
        public uint RequestedAdapterLowPart;
        public int RequestedAdapterHighPart;
        public uint SourceChroma;
        public int SourceDXGIFormat;
        public bool IsOpaque;
        public ulong PoolGeneration;
        public D3D11AdapterFailure FallbackReason;
        public int NativeResult;
        public object PoolGate { get; } = new();
    }

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_entry_copyright__3_0_0f",
        CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCCopyright() => Copyright;

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_entry_license__3_0_0f",
        CallConvs = [typeof(CallConvCdecl)])]
    public static nint VLCLicense() => License;

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_entry__3_0_0f",
        CallConvs = [typeof(CallConvCdecl)])]
    public static int VLCEntry(nint VLCSetPtr, nint opaque)
    {
        int result = ModuleBuilder.Create(VLCSetPtr, opaque)
            .WithName("vlc_avalonia")
            .WithShortcut("vlc_avalonia")
            .WithShortName("Conic")
            .WithDescription("Pure-C# Avalonia D3D11 composition video output")
            .WithCapability("vout display")
            .WithScore(0)
            .WithOpenCallback(&Open, "CompositionVoutOpen")
            .WithCloseCallback(&Close, "CompositionVoutClose")
            .WithNoUnload()
            .Register();
        if (result != Success)
            return result;

        return ModuleBuilder.Create(VLCSetPtr, opaque)
            .WithShortcut("vlc_avalonia_control")
            .WithShortName("Conic control host")
            .WithDescription("Exposes the VLC 3 main playlist to the Avalonia host")
            .WithCapability("interface")
            .WithScore(0)
            .WithOpenCallback(&OpenControlInterface, "ControlInterfaceOpen")
            .WithCloseCallback(&CloseControlInterface, "ControlInterfaceClose")
            .WithNoUnload()
            .Register();
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
    /// Registers a callback receiving a non-owning D3D11 global shared handle,
    /// dimensions, resource generation, and VLC presentation timestamp.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_gpu_frame_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetGPUFrameCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_GPUFrameCallback = callback;
            s_GPUFrameCallbackOpaque = opaque;
        }
    }

    /// <summary>
    /// Selects the exact DXGI adapter that owns Avalonia's compositor device.
    /// The two integer parts are copied by value and contain no native handle.
    /// Passing configured=0 clears the selection and forces the CPU fallback.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_adapter_luid",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetAdapterLuid(
        uint lowPart,
        int highPart,
        int configured)
    {
        lock (CallbackGate)
        {
            s_adapterLowPart = lowPart;
            s_adapterHighPart = highPart;
            s_adapterConfigured = configured != 0;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_force_cpu_output",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetForceCPUOutput(int enabled)
    {
        lock (CallbackGate)
            s_forceCPUOutput = enabled != 0;
    }

    /// <summary>
    /// Registers machine-readable adapter/device telemetry. Values contain no
    /// raw pointers or handles.
    /// </summary>
    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_device_telemetry_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetDeviceTelemetryCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_telemetryCallback = callback;
            s_telemetryCallbackOpaque = opaque;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "vlc_avalonia_set_playlist_callback",
        CallConvs = [typeof(CallConvCdecl)])]
    public static void SetPlaylistCallback(nint callback, nint opaque)
    {
        lock (CallbackGate)
        {
            s_playlistCallback = callback;
            s_playlistCallbackOpaque = opaque;
        }
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenControlInterface(nint interfacePtr)
    {
        try
        {
            if (interfacePtr == 0)
                return GenericError;
            VLCObjectHeader* header = (VLCObjectHeader*)interfacePtr;
            PublishPlaylist(header->Parent);
            return header->Parent == 0 ? GenericError : Success;
        }
        catch
        {
            return GenericError;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseControlInterface(nint interfacePtr)
    {
        try
        {
            PublishPlaylist(0);
        }
        catch
        {
        }
    }

    private static void PublishPlaylist(nint playlist)
    {
        lock (CallbackGate)
        {
            if (s_playlistCallback == 0)
                return;
            var callback =
                (delegate* unmanaged[Cdecl]<nint, nint, void>)s_playlistCallback;
            callback(s_playlistCallbackOpaque, playlist);
        }
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
            bool adapterConfigured;
            bool forceCPUOutput;
            uint adapterLowPart;
            int adapterHighPart;
            lock (CallbackGate)
            {
                adapterConfigured = s_adapterConfigured;
                forceCPUOutput = s_forceCPUOutput;
                adapterLowPart = s_adapterLowPart;
                adapterHighPart = s_adapterHighPart;
            }

            DisplayState state = new()
            {
                OriginalSource = display->Source,
                RequestedAdapterLowPart = adapterLowPart,
                RequestedAdapterHighPart = adapterHighPart,
                SourceChroma = display->Source.Chroma,
            };
            if (adapterConfigured)
            {
                try
                {
                    state.SharedTexture = new D3D11SharedTexture(
                        adapterLowPart,
                        adapterHighPart);
                }
                catch (D3D11AdapterException exception)
                {
                    state.FallbackReason = exception.Failure;
                    state.NativeResult = exception.NativeResult;
                }
                catch
                {
                    state.FallbackReason =
                        D3D11AdapterFailure.ProducerDeviceCreationFailed;
                    state.NativeResult = unchecked((int)0x80004005);
                }
            }
            else
            {
                state.FallbackReason =
                    D3D11AdapterFailure.AdapterNotConfigured;
            }
            stateHandle = GCHandle.Alloc(state);

            display->Format = display->Source;
            uint outputChroma = SelectOutputChroma(
                display->Source.Chroma,
                state.SharedTexture is not null && !forceCPUOutput);
            state.IsOpaque = VLCFourCC.IsD3D11Opaque(outputChroma);
            if (state.IsOpaque)
            {
                display->Format.Chroma = outputChroma;
                state.SourceChroma = outputChroma;
                state.SourceDXGIFormat = outputChroma ==
                    VLCFourCC.D3D11Opaque10Bit
                        ? VLCD3D11PictureConstants.DxgiFormatP010
                        : VLCD3D11PictureConstants.DxgiFormatNV12;
            }
            else
            {
                display->Format.Chroma = VLCFourCC.BGRA;
                display->Format.BitsPerPixel = 32;
                display->Format.RedMask = 0x00ff0000;
                display->Format.GreenMask = 0x0000ff00;
                display->Format.BlueMask = 0x000000ff;
                state.SourceDXGIFormat =
                    VLCD3D11PictureConstants.DxgiFormatB8G8R8A8UNorm;
            }

            display->Info = new VLCVoutDisplayInfo
            {
                IsSlow = state.IsOpaque ? (byte)0 : (byte)1,
                HasDoubleClick = 0,
                NeedsHideMouse = 0,
                HasPicturesInvalid = 0,
                SubpictureChromas = 0,
            };

            display->Pool = (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint>)&GetPool;
            display->Prepare = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Prepare;
            display->Display = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&Display;
            display->Control = (nint)(delegate* unmanaged[Cdecl]<nint, int, nint, int>)&Control;
            display->Manage = 0;
            display->Sys = GCHandle.ToIntPtr(stateHandle);
            PublishGeometry(display, (DisplayConfiguration*)display->Configuration);
            PublishTelemetry(state);
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
        lock (CallbackGate)
        {
            if (s_geometryOwner == displayPtr) { s_geometry = default; s_geometryOwner = 0; }
        }
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
                if (state.Pool != 0)
                {
                    VLCCore.PicturePoolRelease(state.Pool);
                    state.Pool = 0;
                }
                state.Converter?.Dispose();
                state.Converter = null;
                state.SharedTexture?.Dispose();
                state.SharedTexture = null;
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

                if (state.IsOpaque)
                {
                    D3D11SharedTexture producer = state.SharedTexture ??
                        throw new InvalidOperationException(
                            "Opaque pool lost its producer device.");
                    state.Pool = D3D11OpaquePicturePool.Create(
                        producer.Device,
                        producer.Context,
                        in display->Format,
                        requestedPictures,
                        (TerraFX.Interop.DXGI_FORMAT)state.SourceDXGIFormat,
                        out ulong generation);
                    state.PoolGeneration = generation;
                }
                else
                {
                    uint count = Math.Clamp(requestedPictures, 3u, 8u);
                    state.Pool = VLCCore.PicturePoolNewFromFormat(
                        in display->Format,
                        count);
                    if (state.Pool != 0)
                        state.PoolGeneration =
                            PicturePoolTelemetry.CpuPoolCreated();
                }
                PublishTelemetry(state);
                return state.Pool;
            }
        }
        catch (D3D11AdapterException exception)
        {
            DisplayState? state = GetState((VLCVoutDisplay*)displayPtr);
            if (state is not null)
            {
                state.FallbackReason = exception.Failure;
                state.NativeResult = exception.NativeResult;
                PublishTelemetry(state);
            }
            return 0;
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
                DisplayState? state = GetState(
                    (VLCVoutDisplay*)displayPtr);
                if (VLCFourCC.IsD3D11Opaque(picture->Format.Chroma))
                {
                    PicturePoolTelemetry.OpaquePictureReceived();
                    if (HasGPUFrameCallback() &&
                        state?.SharedTexture is not null &&
                        picture->Sys != 0)
                    {
                        try
                        {
                            state.Converter ??=
                                new D3D11VideoProcessorConverter(
                                    state.SharedTexture.Device,
                                    state.SharedTexture.Context);
                            if (state.Converter.Convert(
                                    (VLCD3D11PictureSys*)picture->Sys,
                                    in picture->Format,
                                    state.SharedTexture,
                                    out uint outputWidth,
                                    out uint outputHeight))
                            {
                                D3D11SharedTexture texture =
                                    state.SharedTexture;
                                PublishGPUFrame(
                                    texture.SharedHandle,
                                    outputWidth,
                                    outputHeight,
                                    texture.ResourceGeneration,
                                    texture.DeviceGeneration,
                                    texture.AdapterLowPart,
                                    texture.AdapterHighPart,
                                    picture->Date);
                            }
                        }
                        catch (D3D11AdapterException exception)
                        {
                            state.Converter?.Dispose();
                            state.Converter = null;
                            state.FallbackReason = exception.Failure;
                            state.NativeResult = exception.NativeResult;
                            PublishTelemetry(state);
                        }
                    }
                    return;
                }

                PicturePoolTelemetry.CpuPictureReceived();
                VLCPlane plane = picture->Plane0;
                uint width = picture->Format.VisibleWidth;
                uint height = picture->Format.VisibleHeight;
                if (width == 0)
                    width = picture->Format.Width;
                if (height == 0)
                    height = picture->Format.Height;

                if (plane.Pixels != 0 && width > 0 && height > 0 && plane.Pitch != 0)
                {
                    nint pixels = plane.Pixels
                        + checked((nint)picture->Format.YOffset * plane.Pitch)
                        + checked((nint)picture->Format.XOffset * 4);
                    if (HasGPUFrameCallback() && state?.SharedTexture is not null)
                    {
                        try
                        {
                            PicturePoolTelemetry.CpuUpload();
                            if (state.SharedTexture.Upload(
                                    pixels,
                                    plane.Pitch,
                                    width,
                                    height))
                            {
                                D3D11SharedTexture texture =
                                    state.SharedTexture;
                                PublishGPUFrame(
                                    texture.SharedHandle,
                                    width,
                                    height,
                                    texture.ResourceGeneration,
                                    texture.DeviceGeneration,
                                    texture.AdapterLowPart,
                                    texture.AdapterHighPart,
                                    picture->Date);
                            }
                        }
                        catch (D3D11AdapterException exception)
                        {
                            state.SharedTexture.Dispose();
                            state.SharedTexture = null;
                            state.FallbackReason = exception.Failure;
                            state.NativeResult = exception.NativeResult;
                            PublishTelemetry(state);
                            PublishFrame(
                                width,
                                height,
                                plane.Pitch,
                                pixels,
                                picture->Date);
                        }
                    }
                    else
                    {
                        PublishFrame(
                            width,
                            height,
                            plane.Pitch,
                            pixels,
                            picture->Date);
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (picturePtr != 0)
                PictureRelease(picturePtr);
            if (subpicturePtr != 0)
                VLCCore.SubpictureDelete(subpicturePtr);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Control(nint displayPtr, int query, nint arguments)
    {
        var display = (VLCVoutDisplay*)displayPtr;
        switch ((VLCVoutDisplayControl)query)
        {
            case VLCVoutDisplayControl.ChangeSourceAspect:
            case VLCVoutDisplayControl.ChangeSourceCrop:
                PublishGeometry(display, null);
                break;
            case VLCVoutDisplayControl.ChangeZoom:
            case VLCVoutDisplayControl.ChangeDisplayFilled:
            case VLCVoutDisplayControl.ChangeDisplaySize:
                // Windows x64 va_list points to the first eight-byte argument slot.
                if (arguments != 0) PublishGeometry(display, *(DisplayConfiguration**)arguments);
                break;
        }
        return Success;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct DisplayConfiguration
    {
        // VLC 3 Windows x64 vout_display_cfg_t, pinned SDK vlc_vout_display.h.
        [FieldOffset(40)] public byte Filled;
        [FieldOffset(44)] public int ZoomNumerator;
        [FieldOffset(48)] public int ZoomDenominator;
    }

    private static void PublishGeometry(VLCVoutDisplay* display, DisplayConfiguration* config)
    {
        var state = GetState(display);
        if (state is null) return;
        var original = state.OriginalSource;
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
            if (config != null)
            {
                s_geometry.AutoScale = config->Filled;
                s_geometry.Zoom = config->ZoomDenominator > 0 ? (double)config->ZoomNumerator / config->ZoomDenominator : 1;
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vlc_avalonia_get_video_geometry_v1", CallConvs = [typeof(CallConvCdecl)])]
    public static void GetVideoGeometry(void* destination)
    {
        if (destination == null) return;
        lock (CallbackGate) *(VideoGeometry*)destination = s_geometry;
    }

    private static DisplayState? GetState(VLCVoutDisplay* display)
    {
        if (display->Sys == 0)
            return null;
        GCHandle handle = GCHandle.FromIntPtr(display->Sys);
        return handle.Target as DisplayState;
    }

    private static uint SelectOutputChroma(
        uint sourceChroma,
        bool producerDeviceAvailable)
    {
        if (!producerDeviceAvailable)
            return VLCFourCC.BGRA;
        if (sourceChroma is VLCFourCC.D3D11Opaque or
            VLCFourCC.D3D11Opaque10Bit)
        {
            return sourceChroma;
        }

        return sourceChroma switch
        {
            VLCFourCC.I420 or VLCFourCC.YV12 => VLCFourCC.D3D11Opaque,
            I42010LittleEndian or P010 => VLCFourCC.D3D11Opaque10Bit,
            _ => VLCFourCC.BGRA,
        };
    }

    private static bool HasGPUFrameCallback()
    {
        lock (CallbackGate)
            return s_GPUFrameCallback != 0;
    }

    private static void PublishGPUFrame(
        nint sharedHandle,
        uint width,
        uint height,
        ulong resourceGeneration,
        ulong deviceGeneration,
        uint adapterLowPart,
        int adapterHighPart,
        long presentationTimestamp)
    {
        lock (CallbackGate)
        {
            if (s_GPUFrameCallback == 0)
                return;
            var callback =
                (delegate* unmanaged[Cdecl]<nint, nint, uint, uint, ulong, ulong, uint, int, long, void>)s_GPUFrameCallback;
            callback(
                s_GPUFrameCallbackOpaque,
                sharedHandle,
                width,
                height,
                resourceGeneration,
                deviceGeneration,
                adapterLowPart,
                adapterHighPart,
                presentationTimestamp);
        }
    }

    private static void PublishTelemetry(DisplayState state)
    {
        lock (CallbackGate)
        {
            if (s_telemetryCallback == 0)
                return;
            D3D11SharedTexture? texture = state.SharedTexture;
            var callback =
                (delegate* unmanaged[Cdecl]<nint, uint, int, uint, int, int, ulong, uint, int, int, int, void>)s_telemetryCallback;
            callback(
                s_telemetryCallbackOpaque,
                state.RequestedAdapterLowPart,
                state.RequestedAdapterHighPart,
                texture?.AdapterLowPart ?? 0,
                texture?.AdapterHighPart ?? 0,
                texture is null ? 0 : 1,
                texture?.DeviceGeneration ?? 0,
                state.SourceChroma,
                state.SourceDXGIFormat,
                (int)state.FallbackReason,
                state.NativeResult);
        }
    }

    private static void PublishFrame(
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
}

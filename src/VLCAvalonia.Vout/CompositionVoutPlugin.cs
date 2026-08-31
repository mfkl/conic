using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using VLCLR.Module;
using VLCLR.Native;

namespace VLCAvalonia.Vout;

/// <summary>
/// VLC vout that publishes video through a keyed-mutex D3D11 shared texture.
/// The CPU callback remains available as a fallback and diagnostic path.
/// </summary>
public static unsafe class CompositionVoutPlugin
{
    private const int Success = 0;
    private const int GenericError = -1;

    private static readonly nint Copyright =
        Marshal.StringToCoTaskMemUTF8("Copyright (c) VideoLabs");
    private static readonly nint License =
        Marshal.StringToCoTaskMemUTF8("LGPL-2.1-or-later");

    private static nint s_frameCallback;
    private static nint s_frameCallbackOpaque;
    private static nint s_GPUFrameCallback;
    private static nint s_GPUFrameCallbackOpaque;

    private static readonly nint LibVLCCore =
        NativeLibrary.Load("libvlccore.dll");
    private static readonly delegate* unmanaged[Cdecl]<nint, void>
        PictureRelease =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(
                LibVLCCore,
                "picture_Release");

    private sealed class DisplayState
    {
        public nint Pool;
        public D3D11SharedTexture? SharedTexture;
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
        return ModuleBuilder.Create(VLCSetPtr, opaque)
            .WithName("vlc_avalonia")
            .WithShortcut("vlc_avalonia")
            .WithShortName("VLC Avalonia")
            .WithDescription("Pure-C# Avalonia D3D11 composition video output")
            .WithCapability("vout display")
            .WithScore(0)
            .WithOpenCallback(&Open, "CompositionVoutOpen")
            .WithCloseCallback(&Close, "CompositionVoutClose")
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
        Volatile.Write(ref s_frameCallbackOpaque, opaque);
        Volatile.Write(ref s_frameCallback, callback);
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
        Volatile.Write(ref s_GPUFrameCallbackOpaque, opaque);
        Volatile.Write(ref s_GPUFrameCallback, callback);
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
            DisplayState state = new()
            {
                SharedTexture = new D3D11SharedTexture(),
            };
            stateHandle = GCHandle.Alloc(state);

            display->Format = display->Source;
            display->Format.Chroma = VLCFourCC.BGRA;
            display->Format.BitsPerPixel = 32;
            display->Format.RedMask = 0x00ff0000;
            display->Format.GreenMask = 0x0000ff00;
            display->Format.BlueMask = 0x000000ff;

            display->Info = new VLCVoutDisplayInfo
            {
                IsSlow = 1,
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
                state.SharedTexture?.Dispose();
                state.SharedTexture = null;
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
            if (state.Pool != 0)
                return state.Pool;

            uint count = Math.Clamp(requestedPictures, 3u, 8u);
            state.Pool = VLCCore.PicturePoolNewFromFormat(
                in display->Format,
                count);
            return state.Pool;
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
                    nint GPUCallbackPointer = Volatile.Read(
                        ref s_GPUFrameCallback);
                    DisplayState? state = GetState(
                        (VLCVoutDisplay*)displayPtr);
                    if (GPUCallbackPointer != 0)
                    {
                        if (state?.SharedTexture?.Upload(
                                pixels,
                                plane.Pitch,
                                width,
                                height) == true)
                        {
                            D3D11SharedTexture texture =
                                state.SharedTexture;
                            var GPUCallback =
                                (delegate* unmanaged[Cdecl]<nint, nint, uint, uint, ulong, long, void>)GPUCallbackPointer;
                            GPUCallback(
                                Volatile.Read(ref s_GPUFrameCallbackOpaque),
                                texture.SharedHandle,
                                width,
                                height,
                                texture.Generation,
                                picture->Date);
                        }
                    }
                    else
                    {
                        nint callbackPointer = Volatile.Read(
                            ref s_frameCallback);
                        if (callbackPointer != 0)
                        {
                            var callback =
                                (delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)callbackPointer;
                            callback(
                                Volatile.Read(ref s_frameCallbackOpaque),
                                width,
                                height,
                                plane.Pitch,
                                pixels,
                                picture->Date);
                        }
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
        return Success;
    }

    private static DisplayState? GetState(VLCVoutDisplay* display)
    {
        if (display->Sys == 0)
            return null;
        GCHandle handle = GCHandle.FromIntPtr(display->Sys);
        return handle.Target as DisplayState;
    }
}

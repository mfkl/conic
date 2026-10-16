using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;
using Conic.Interop.Linux;

namespace Conic.Vout;

/// <summary>
/// VA-API surfaces for VLC 3's vaapi decoder to decode into (direct rendering),
/// laid out as modules/hw/vaapi/vlc_vaapi.c lays out its own pools. The decoder
/// reads the display from the first picture's <c>picture_sys_t</c> and runs its
/// own copy of that file's helpers on these allocations, so they come from the
/// C allocator, and the callbacks stay callable for as long as a picture lives.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe class VaapiPicturePool
{
    /// <summary>Opens VA-API on a DRM render node, as an instance holding one reference for the caller.</summary>
    public static VLCVaapiInstance* CreateInstance()
    {
        nint display = VaapiConverter.OpenDrmDisplay(out int fd, out _);
        VLCVaapiInstance* instance = (VLCVaapiInstance*)NativeMemory.Alloc((nuint)sizeof(VLCVaapiInstance));
        *instance = new VLCVaapiInstance
        {
            Display = display,
            Native = fd,
            NativeDestroy = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CloseNative,
            ReferenceCount = 1,
        };
        return instance;
    }

    /// <summary><c>vlc_vaapi_HoldInstance</c>.</summary>
    public static nint Hold(VLCVaapiInstance* instance)
    {
        Interlocked.Increment(ref instance->ReferenceCount);
        return instance->Display;
    }

    /// <summary><c>vlc_vaapi_ReleaseInstance</c>: the last reference terminates the display.</summary>
    public static void Release(VLCVaapiInstance* instance)
    {
        if (Interlocked.Decrement(ref instance->ReferenceCount) != 0)
            return;
        VaApi.Terminate(instance->Display);
        if (instance->Native != 0 && instance->NativeDestroy != 0)
            ((delegate* unmanaged[Cdecl]<nint, void>)instance->NativeDestroy)(instance->Native);
        NativeMemory.Free(instance);
    }

    /// <summary>
    /// <c>vlc_vaapi_PoolNew</c>: <paramref name="count"/> surfaces of the visible
    /// size of <paramref name="format"/> (VAOP or VAO0), one pool picture each.
    /// Returns zero on failure.
    /// </summary>
    public static nint Create(VLCVaapiInstance* instance, in VLCVideoFormat format, uint count)
    {
        bool tenBit = format.Chroma == VLCFourCC.VaapiOpaque420TenBit;
        uint width = format.VisibleWidth != 0 ? format.VisibleWidth : format.Width;
        uint height = format.VisibleHeight != 0 ? format.VisibleHeight : format.Height;
        VLCVaapiPoolInstance* pool = (VLCVaapiPoolInstance*)NativeMemory.Alloc((nuint)(VLCVaapiPoolInstance.HeaderSize + count * sizeof(uint)));
        pool->PictureReferenceCount = 0;
        pool->Display = 0;
        pool->Instance = 0;
        pool->RenderTargetCount = count;
        uint* targets = VLCVaapiPoolInstance.RenderTargets(pool);

        VASurfaceAttrib attribute = new() { Type = VASurfaceAttrib.PixelFormat, Flags = VASurfaceAttrib.Settable };
        attribute.Value.Type = VAGenericValue.TypeInteger;
        attribute.Value.Integer = (int)(tenBit ? VaApi.FourCCP010 : VaApi.FourCCNv12);
        if (VaApi.CreateSurfaces(instance->Display, tenBit ? VaApi.RtFormatYuv420TenBit : VaApi.RtFormatYuv420,
                width, height, targets, count, &attribute, 1) != VaApi.Success)
        {
            NativeMemory.Free(pool);
            return 0;
        }

        VLCPicture** pictures = stackalloc VLCPicture*[(int)count];
        uint created = 0;
        for (; created < count; created++)
        {
            VLCVaapiPictureSys* sys = (VLCVaapiPictureSys*)NativeMemory.Alloc((nuint)sizeof(VLCVaapiPictureSys));
            sys->Instance = (nint)pool;
            sys->Context = new VLCVaapiPictureContext
            {
                Destroy = (nint)(delegate* unmanaged[Cdecl]<VLCVaapiPictureContext*, void>)&DestroySysContext,
                Copy = (nint)(delegate* unmanaged[Cdecl]<VLCVaapiPictureContext*, VLCVaapiPictureContext*>)&CopyContext,
                Surface = targets[created],
                PictureReference = 0,
            };
            VLCPictureResource resource = new() { Sys = (nint)sys, Destroy = &DestroyPicture };
            VLCPicture* picture = VLCCore.PictureNewFromResource(in format, in resource);
            if (picture == null)
            {
                NativeMemory.Free(sys);
                break;
            }
            pictures[created] = picture;
        }

        nint result = created == count ? VLCCore.PicturePoolNew(count, pictures) : 0;
        if (result == 0)
        {
            while (created > 0)
                VLCCore.PictureRelease((nint)pictures[--created]);
            VaApi.DestroySurfaces(instance->Display, targets, (int)count);
            NativeMemory.Free(pool);
            return 0;
        }
        pool->PictureReferenceCount = (int)count;
        pool->Display = Hold(instance);
        pool->Instance = (nint)instance;
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyPicture(VLCPicture* picture)
    {
        VLCVaapiPictureSys* sys = (VLCVaapiPictureSys*)picture->Sys;
        VLCVaapiPoolInstance* pool = (VLCVaapiPoolInstance*)sys->Instance;
        if (Interlocked.Decrement(ref pool->PictureReferenceCount) == 0)
        {
            VaApi.DestroySurfaces(pool->Display, VLCVaapiPoolInstance.RenderTargets(pool), (int)pool->RenderTargetCount);
            Release((VLCVaapiInstance*)pool->Instance);
            NativeMemory.Free(pool);
        }
        NativeMemory.Free(sys);
        NativeMemory.Free(picture);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroySysContext(VLCVaapiPictureContext* context)
    {
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyContext(VLCVaapiPictureContext* context)
    {
        VLCCore.PictureRelease(context->PictureReference);
        NativeMemory.Free(context);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VLCVaapiPictureContext* CopyContext(VLCVaapiPictureContext* source)
    {
        VLCVaapiPictureContext* copy = (VLCVaapiPictureContext*)NativeMemory.Alloc((nuint)sizeof(VLCVaapiPictureContext));
        copy->Destroy = (nint)(delegate* unmanaged[Cdecl]<VLCVaapiPictureContext*, void>)&DestroyContext;
        copy->Copy = (nint)(delegate* unmanaged[Cdecl]<VLCVaapiPictureContext*, VLCVaapiPictureContext*>)&CopyContext;
        copy->Surface = source->Surface;
        copy->PictureReference = (nint)VLCCore.PictureHold((VLCPicture*)source->PictureReference);
        return copy;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseNative(nint native) => VaapiConverter.CloseFile((int)native);
}

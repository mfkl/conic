using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop;
using VLCLR.Native;

namespace VLCAvalonia.Vout;

/// <summary>
/// Creates the VLC-owned opaque picture pool on the producer D3D11 device.
/// The layout and ownership rules are the ABI verified by VLCLR WP-04.
/// </summary>
internal static unsafe class D3D11OpaquePicturePool
{
    // Direct3D 11 supports at most 2048 Texture2D array slices. VLC's native
    // D3D11 vout adds two pictures for decoder pressure, which we mirror.
    private const uint MaximumArraySize = 2048;
    private static readonly nint LibVLCCore =
        NativeLibrary.Load("libvlccore.dll");
    private static readonly delegate* unmanaged[Cdecl]<nint, void>
        PictureRelease =
            (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(
                LibVLCCore,
                "picture_Release");
    private static readonly delegate* unmanaged[Cdecl]<
        VLCPicturePoolConfiguration*, nint> PicturePoolNewExtended =
            (delegate* unmanaged[Cdecl]<VLCPicturePoolConfiguration*, nint>)
            NativeLibrary.GetExport(
                LibVLCCore,
                "picture_pool_NewExtended");

    public static nint Create(
        ID3D11Device* device,
        ID3D11DeviceContext* context,
        in VLCVideoFormat format,
        uint requestedPictures,
        DXGI_FORMAT textureFormat,
        out ulong generation)
    {
        generation = 0;
        if (device is null || context is null)
            throw new InvalidOperationException(
                "The opaque pool requires a live producer D3D11 device.");

        uint pictureCount = checked(requestedPictures + 2);
        if (pictureCount is < 5 or > MaximumArraySize)
        {
            throw new InvalidOperationException(
                $"Unsupported opaque picture count {pictureCount} " +
                $"(VLC requested {requestedPictures}).");
        }

        D3D11_TEXTURE2D_DESC description = new()
        {
            Width = format.Width,
            Height = format.Height,
            MipLevels = 1,
            ArraySize = pictureCount,
            Format = textureFormat,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(
                D3D11_BIND_FLAG.D3D11_BIND_DECODER |
                D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
            CPUAccessFlags = 0,
            MiscFlags = 0,
        };

        ID3D11Texture2D* texture = null;
        VLCPicture** pictures = null;
        uint createdPictures = 0;
        nint pool = 0;
        try
        {
            int result = device->CreateTexture2D(
                &description,
                null,
                &texture);
            if (result < 0)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.OpaquePoolCreationFailed,
                    result,
                    $"ID3D11Device.CreateTexture2D({textureFormat} " +
                    $"array[{pictureCount}])");
            }

            pictures = (VLCPicture**)NativeMemory.AllocZeroed(
                pictureCount,
                (nuint)sizeof(VLCPicture*));
            if (pictures is null)
                throw new OutOfMemoryException();

            for (uint index = 0; index < pictureCount; index++)
            {
                VLCD3D11PictureSys* pictureSystem =
                    (VLCD3D11PictureSys*)NativeMemory.AllocZeroed(
                        (nuint)sizeof(VLCD3D11PictureSys));
                if (pictureSystem is null)
                    throw new OutOfMemoryException();

                // NV12 and P010 each expose two shader planes from the same
                // Texture2D array. Each field is one separately owned COM ref.
                texture->AddRef();
                pictureSystem->Textures.Texture0 = (nint)texture;
                texture->AddRef();
                pictureSystem->Textures.Texture1 = (nint)texture;
                context->AddRef();
                pictureSystem->Context = (nint)context;
                pictureSystem->SliceIndex = index;
                pictureSystem->FormatTexture = (int)textureFormat;

                VLCPictureResource resource = new()
                {
                    Sys = (nint)pictureSystem,
                    Destroy = &DestroyPicture,
                };
                VLCPicture* picture = VLCCore.PictureNewFromResource(
                    in format,
                    in resource);
                if (picture is null)
                {
                    ReleasePictureSystem(pictureSystem);
                    NativeMemory.Free(pictureSystem);
                    throw new InvalidOperationException(
                        $"picture_NewFromResource failed for opaque " +
                        $"picture {index} of {pictureCount}.");
                }

                pictures[index] = picture;
                createdPictures++;
                PicturePoolTelemetry.OpaquePictureAllocated();
            }

            VLCPicturePoolConfiguration configuration = new()
            {
                PictureCount = pictureCount,
                Pictures = pictures,
            };
            pool = PicturePoolNewExtended(&configuration);
            if (pool == 0)
            {
                throw new InvalidOperationException(
                    $"picture_pool_NewExtended rejected {pictureCount} " +
                    $"opaque pictures.");
            }

            // The pool consumed every picture reference. From here onward it
            // is the only owner responsible for invoking DestroyPicture.
            createdPictures = 0;
            generation = PicturePoolTelemetry.OpaquePoolCreated();
            return pool;
        }
        finally
        {
            if (pool == 0 && pictures is not null)
            {
                for (uint index = 0; index < createdPictures; index++)
                {
                    if (pictures[index] is not null)
                        PictureRelease((nint)pictures[index]);
                }
            }
            if (pictures is not null)
                NativeMemory.Free(pictures);
            if (texture is not null)
                texture->Release();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyPicture(VLCPicture* picture)
    {
        try
        {
            if (picture is null)
                return;
            VLCD3D11PictureSys* pictureSystem =
                (VLCD3D11PictureSys*)picture->Sys;
            if (pictureSystem is not null)
            {
                ReleasePictureSystem(pictureSystem);
                NativeMemory.Free(pictureSystem);
                picture->Sys = 0;
            }
            VLCCore.Free((nint)picture);
            PicturePoolTelemetry.OpaquePictureReleased();
        }
        catch
        {
            // Exceptions must never cross VLC's unmanaged destroy boundary.
        }
    }

    private static void ReleasePictureSystem(
        VLCD3D11PictureSys* pictureSystem)
    {
        Release(ref pictureSystem->ResourceView0);
        Release(ref pictureSystem->ResourceView1);
        Release(ref pictureSystem->ResourceView2);
        Release(ref pictureSystem->ResourceView3);
        Release(ref pictureSystem->Textures.Texture0);
        Release(ref pictureSystem->Textures.Texture1);
        Release(ref pictureSystem->Textures.Texture2);
        Release(ref pictureSystem->Textures.Texture3);
        Release(ref pictureSystem->Context);
        Release(ref pictureSystem->Decoder);
        Release(ref pictureSystem->ProcessorInput);
        Release(ref pictureSystem->ProcessorOutput);
    }

    private static void Release(ref nint pointer)
    {
        nint owned = pointer;
        pointer = 0;
        if (owned != 0)
            ((IUnknown*)owned)->Release();
    }
}

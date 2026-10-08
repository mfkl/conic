using System.Runtime.InteropServices;

namespace VLCAvalonia.Interop.Linux;

// libva 2.x bindings for the Linux video output: VA-API video processing
// (decoded NV12/P010 surface to an RGB surface on the GPU) and DRM PRIME
// export of the result for the compositor. Layouts were taken from the libva
// 2.20.0 headers (Ubuntu 24.04) and are checked by tests/NativeLayout/vaapi_layout.c.
// Only 64-bit Linux is supported.

/// <summary>VAGenericValue: a type tag and an 8-byte union.</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct VAGenericValue
{
    public const int TypeInteger = 1;
    [FieldOffset(0)] public int Type;
    [FieldOffset(8)] public int Integer;
    [FieldOffset(8)] public nint Pointer;
}

/// <summary>VASurfaceAttrib.</summary>
[StructLayout(LayoutKind.Sequential, Size = 24)]
internal struct VASurfaceAttrib
{
    public const int PixelFormat = 1;
    public const uint Settable = 2;
    public int Type;
    public uint Flags;
    public VAGenericValue Value;
}

/// <summary>VARectangle.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct VARectangle
{
    public short X, Y;
    public ushort Width, Height;
}

/// <summary>One DRM object of a <see cref="VADRMPRIMESurfaceDescriptor"/>.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
internal struct VADRMPRIMEObject
{
    /// <summary>DMA-BUF file descriptor, owned by the caller of vaExportSurfaceHandle.</summary>
    public int Fd;
    public uint Size;
    public ulong DrmFormatModifier;
}

/// <summary>One layer of a <see cref="VADRMPRIMESurfaceDescriptor"/>.</summary>
[StructLayout(LayoutKind.Sequential, Size = 56)]
internal unsafe struct VADRMPRIMELayer
{
    public uint DrmFormat;
    public uint PlaneCount;
    public fixed uint ObjectIndex[4];
    public fixed uint Offset[4];
    public fixed uint Pitch[4];
}

/// <summary>VADRMPRIMESurfaceDescriptor, filled by vaExportSurfaceHandle.</summary>
[StructLayout(LayoutKind.Explicit, Size = 312)]
internal struct VADRMPRIMESurfaceDescriptor
{
    [FieldOffset(0)] public uint FourCC;
    [FieldOffset(4)] public uint Width;
    [FieldOffset(8)] public uint Height;
    [FieldOffset(12)] public uint ObjectCount;
    [FieldOffset(16)] public VADRMPRIMEObject Object0;
    [FieldOffset(32)] public VADRMPRIMEObject Object1;
    [FieldOffset(48)] public VADRMPRIMEObject Object2;
    [FieldOffset(64)] public VADRMPRIMEObject Object3;
    [FieldOffset(80)] public uint LayerCount;
    [FieldOffset(84)] public VADRMPRIMELayer Layer0;
    [FieldOffset(140)] public VADRMPRIMELayer Layer1;
    [FieldOffset(196)] public VADRMPRIMELayer Layer2;
    [FieldOffset(252)] public VADRMPRIMELayer Layer3;

    public readonly VADRMPRIMEObject GetObject(int index) => index switch
    {
        0 => Object0, 1 => Object1, 2 => Object2, 3 => Object3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

/// <summary>VAProcColorProperties.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct VAProcColorProperties
{
    public byte ChromaSampleLocation;
    public byte ColorRange;
    public byte ColourPrimaries;
    public byte TransferCharacteristics;
    public byte MatrixCoefficients;
    private byte _reserved0, _reserved1, _reserved2;
}

/// <summary>VAProcPipelineParameterBuffer (libva 1.x/2.x, 224 bytes).</summary>
[StructLayout(LayoutKind.Explicit, Size = 224)]
internal struct VAProcPipelineParameterBuffer
{
    [FieldOffset(0)] public uint Surface;
    /// <summary>const VARectangle*; null for the whole surface.</summary>
    [FieldOffset(8)] public nint SurfaceRegion;
    [FieldOffset(16)] public int SurfaceColorStandard;
    /// <summary>const VARectangle*; null for the whole target.</summary>
    [FieldOffset(24)] public nint OutputRegion;
    [FieldOffset(32)] public uint OutputBackgroundColor;
    [FieldOffset(36)] public int OutputColorStandard;
    [FieldOffset(40)] public uint PipelineFlags;
    [FieldOffset(44)] public uint FilterFlags;
    [FieldOffset(48)] public nint Filters;
    [FieldOffset(56)] public uint FilterCount;
    [FieldOffset(64)] public nint ForwardReferences;
    [FieldOffset(72)] public uint ForwardReferenceCount;
    [FieldOffset(80)] public nint BackwardReferences;
    [FieldOffset(88)] public uint BackwardReferenceCount;
    [FieldOffset(92)] public uint RotationState;
    [FieldOffset(96)] public nint BlendState;
    [FieldOffset(104)] public uint MirrorState;
    [FieldOffset(112)] public nint AdditionalOutputs;
    [FieldOffset(120)] public uint AdditionalOutputCount;
    [FieldOffset(124)] public uint InputSurfaceFlag;
    [FieldOffset(128)] public uint OutputSurfaceFlag;
    [FieldOffset(132)] public VAProcColorProperties InputColorProperties;
    [FieldOffset(140)] public VAProcColorProperties OutputColorProperties;
    [FieldOffset(148)] public uint ProcessingMode;
    [FieldOffset(152)] public nint OutputHdrMetadata;
}

internal static partial class VaApi
{
    private const string Library = "libva.so.2";
    private const string DrmLibrary = "libva-drm.so.2";

    public const int Success = 0;
    public const uint InvalidId = 0xFFFFFFFF;

    public const uint RtFormatYuv420 = 0x1;
    public const uint RtFormatYuv420TenBit = 0x100;
    public const uint RtFormatRgb32 = 0x20000;

    public const uint FourCCNv12 = 0x3231564E;
    public const uint FourCCP010 = 0x30313050;
    public const uint FourCCBgra = 0x41524742;
    public const uint FourCCRgba = 0x41424752;
    public const uint FourCCBgrx = 0x58524742;

    public const int ProfileNone = -1;
    public const int EntrypointVideoProc = 10;
    public const int ProcPipelineParameterBufferType = 41;
    public const uint ProcPipelineFast = 0x2;
    public const uint FilterScalingDefault = 0;
    public const uint FilterScalingHighQuality = 0x200;

    public const int ColorStandardBT601 = 1;
    public const int ColorStandardBT709 = 2;
    public const int ColorStandardSRGB = 8;
    public const int ColorStandardBT2020 = 12;

    public const uint SurfaceAttribMemTypeDrmPrime2 = 0x40000000;
    public const uint ExportSurfaceReadOnly = 0x1;
    public const uint ExportSurfaceSeparateLayers = 0x4;
    public const uint ExportSurfaceComposedLayers = 0x8;

    [LibraryImport(DrmLibrary, EntryPoint = "vaGetDisplayDRM")]
    public static partial nint GetDisplayDrm(int fd);

    [LibraryImport(Library, EntryPoint = "vaInitialize")]
    public static unsafe partial int Initialize(nint display, int* major, int* minor);

    [LibraryImport(Library, EntryPoint = "vaTerminate")]
    public static partial int Terminate(nint display);

    [LibraryImport(Library, EntryPoint = "vaErrorStr")]
    public static partial nint ErrorString(int status);

    [LibraryImport(Library, EntryPoint = "vaQueryVendorString")]
    public static partial nint QueryVendorString(nint display);

    [LibraryImport(Library, EntryPoint = "vaCreateSurfaces")]
    public static unsafe partial int CreateSurfaces(nint display, uint format, uint width, uint height,
        uint* surfaces, uint count, VASurfaceAttrib* attributes, uint attributeCount);

    [LibraryImport(Library, EntryPoint = "vaDestroySurfaces")]
    public static unsafe partial int DestroySurfaces(nint display, uint* surfaces, int count);

    [LibraryImport(Library, EntryPoint = "vaSyncSurface")]
    public static partial int SyncSurface(nint display, uint surface);

    [LibraryImport(Library, EntryPoint = "vaExportSurfaceHandle")]
    public static unsafe partial int ExportSurfaceHandle(nint display, uint surface, uint memoryType,
        uint flags, VADRMPRIMESurfaceDescriptor* descriptor);

    [LibraryImport(Library, EntryPoint = "vaCreateConfig")]
    public static unsafe partial int CreateConfig(nint display, int profile, int entrypoint,
        nint attributes, int attributeCount, uint* config);

    [LibraryImport(Library, EntryPoint = "vaDestroyConfig")]
    public static partial int DestroyConfig(nint display, uint config);

    [LibraryImport(Library, EntryPoint = "vaCreateContext")]
    public static unsafe partial int CreateContext(nint display, uint config, int width, int height,
        int flags, uint* renderTargets, int renderTargetCount, uint* context);

    [LibraryImport(Library, EntryPoint = "vaDestroyContext")]
    public static partial int DestroyContext(nint display, uint context);

    [LibraryImport(Library, EntryPoint = "vaCreateBuffer")]
    public static unsafe partial int CreateBuffer(nint display, uint context, int type, uint size,
        uint elementCount, void* data, uint* buffer);

    [LibraryImport(Library, EntryPoint = "vaDestroyBuffer")]
    public static partial int DestroyBuffer(nint display, uint buffer);

    [LibraryImport(Library, EntryPoint = "vaBeginPicture")]
    public static partial int BeginPicture(nint display, uint context, uint renderTarget);

    [LibraryImport(Library, EntryPoint = "vaRenderPicture")]
    public static unsafe partial int RenderPicture(nint display, uint context, uint* buffers, int count);

    [LibraryImport(Library, EntryPoint = "vaEndPicture")]
    public static partial int EndPicture(nint display, uint context);

    public static string Describe(int status) =>
        Marshal.PtrToStringUTF8(ErrorString(status)) ?? $"VA status {status}";
}

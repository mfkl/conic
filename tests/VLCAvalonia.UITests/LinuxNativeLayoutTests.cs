using System.Runtime.InteropServices;
using VLCAvalonia.Interop.Linux;
using VLCLR.Native;
using Xunit;

namespace VLCAvalonia.UITests;

/// <summary>
/// The managed mirrors must have the sizes and offsets that the native probes
/// (tests/NativeLayout/vaapi_layout.c and VLCLR's tests/AbiValidation) assert
/// against the C headers and VLC source.
/// </summary>
public sealed unsafe class LinuxNativeLayoutTests
{
    [Fact]
    public void LibvaStructsMatchTheLibva2Headers()
    {
        Assert.Equal(16, sizeof(VAGenericValue));
        Assert.Equal(24, sizeof(VASurfaceAttrib));
        Assert.Equal(8, Offset<VASurfaceAttrib>(nameof(VASurfaceAttrib.Value)));
        Assert.Equal(8, sizeof(VARectangle));
        Assert.Equal(16, sizeof(VADRMPRIMEObject));
        Assert.Equal(56, sizeof(VADRMPRIMELayer));
        Assert.Equal(312, sizeof(VADRMPRIMESurfaceDescriptor));
        Assert.Equal(80, Offset<VADRMPRIMESurfaceDescriptor>(nameof(VADRMPRIMESurfaceDescriptor.LayerCount)));
        Assert.Equal(84, Offset<VADRMPRIMESurfaceDescriptor>(nameof(VADRMPRIMESurfaceDescriptor.Layer0)));
        Assert.Equal(8, sizeof(VAProcColorProperties));
        Assert.Equal(224, sizeof(VAProcPipelineParameterBuffer));
        Assert.Equal(132, Offset<VAProcPipelineParameterBuffer>(nameof(VAProcPipelineParameterBuffer.InputColorProperties)));
        Assert.Equal(152, Offset<VAProcPipelineParameterBuffer>(nameof(VAProcPipelineParameterBuffer.OutputHdrMetadata)));
    }

    [Fact]
    public void VlcVaapiPictureStructsMatchVlc3()
    {
        Assert.Equal(VLCVaapiLayout.InstanceSize, sizeof(VLCVaapiInstance));
        Assert.Equal(24, Offset<VLCVaapiInstance>(nameof(VLCVaapiInstance.ReferenceCount)));
        Assert.Equal(VLCVaapiLayout.PictureContextSize, sizeof(VLCVaapiPictureContext));
        Assert.Equal(16, Offset<VLCVaapiPictureContext>(nameof(VLCVaapiPictureContext.Surface)));
        Assert.Equal(24, Offset<VLCVaapiPictureContext>(nameof(VLCVaapiPictureContext.PictureReference)));
        Assert.Equal(VLCVaapiLayout.PictureSysSize, sizeof(VLCVaapiPictureSys));
        Assert.Equal(8, Offset<VLCVaapiPictureSys>(nameof(VLCVaapiPictureSys.Context)));
        Assert.Equal(8, Offset<VLCVaapiPoolInstance>(nameof(VLCVaapiPoolInstance.Display)));
        Assert.Equal(16, Offset<VLCVaapiPoolInstance>(nameof(VLCVaapiPoolInstance.Instance)));
        Assert.Equal(24, Offset<VLCVaapiPoolInstance>(nameof(VLCVaapiPoolInstance.RenderTargetCount)));
        Assert.Equal(VLCVaapiPoolInstance.HeaderSize, Offset<VLCVaapiPoolInstance>(nameof(VLCVaapiPoolInstance.RenderTargetCount)) + sizeof(uint));
    }

    [Fact]
    public void VaapiFourCCsAreVlcs()
    {
        Assert.Equal("VAOP", FourCC(VLCFourCC.VaapiOpaque420));
        Assert.Equal("VAO0", FourCC(VLCFourCC.VaapiOpaque420TenBit));
        Assert.True(VLCFourCC.IsVaapiOpaque(VLCFourCC.VaapiOpaque420TenBit));
        Assert.False(VLCFourCC.IsVaapiOpaque(VLCFourCC.D3D11Opaque));
    }

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    private static string FourCC(uint value) =>
        new([(char)(value & 0xFF), (char)((value >> 8) & 0xFF), (char)((value >> 16) & 0xFF), (char)(value >> 24)]);
}

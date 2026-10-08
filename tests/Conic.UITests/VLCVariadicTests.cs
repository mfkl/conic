using System.Runtime.InteropServices;
using System.Text;
using VLCLR.Native;
using Xunit;

namespace Conic.UITests;

/// <summary>
/// Calls the C library's own variadic <c>snprintf</c> through
/// <see cref="VLCVariadic"/>, so its real <c>va_arg</c> reads the arguments
/// where the platform convention puts them (the stack on Apple arm64).
/// </summary>
public sealed unsafe class VLCVariadicTests
{
    [Fact]
    public void TheCLibraryReadsIntegersAndPointersFromVariadicSlots()
    {
        if (!VLCPlatform.IsSupported) return;
        byte[] format = Encoding.ASCII.GetBytes("%d|%lld|%s\0");
        byte[] text = Encoding.ASCII.GetBytes("slot\0");
        byte[] output = new byte[64];
        fixed (byte* formatPointer = format) fixed (byte* textPointer = text) fixed (byte* outputPointer = output)
        {
            int written = VLCVariadic.Call(PrintFunction(), (nint)outputPointer, output.Length, (nint)formatPointer,
                -42, 1L << 40, (long)textPointer);
            Assert.Equal("-42|1099511627776|slot", Encoding.ASCII.GetString(output, 0, written));
        }
    }

    [Fact]
    public void AppleArm64PassesDoublesThroughStackSlots()
    {
        if (!VLCVariadic.PassesOnStack) return;
        byte[] format = Encoding.ASCII.GetBytes("%.2f|%d\0");
        byte[] output = new byte[64];
        fixed (byte* formatPointer = format) fixed (byte* outputPointer = output)
        {
            int written = VLCVariadic.Call(PrintFunction(), (nint)outputPointer, output.Length, (nint)formatPointer,
                VLCVaList.Double(-2.5), 7);
            Assert.Equal("-2.50|7", Encoding.ASCII.GetString(output, 0, written));
        }
    }

    [Fact]
    public void AVaListsFirstArgumentCanBeReadWithoutConsumingIt()
    {
        if (!VLCPlatform.IsSupported) return;
        long* slots = stackalloc long[2];
        slots[0] = 0x1234_5678_9ABC;
        slots[1] = 7;
        VLCVaList.SystemVTag tag;
        Assert.Equal((nint)0x1234_5678_9ABC, VLCVaList.PeekPointer(VLCVaList.Create(slots, &tag)));
    }

    [Fact]
    public void DisplayControlQueriesAreNumberedAsOnWindows()
    {
        // Only Windows (and OS/2) builds of VLC 3 have the two fullscreen queries.
        int nativeDisplaySize = OperatingSystem.IsWindows() ? 4 : 2;
        Assert.Equal(VLCVoutDisplayControl.ResetPictures, VLCVoutDisplayControls.FromNative(1));
        Assert.Equal(VLCVoutDisplayControl.ChangeDisplaySize, VLCVoutDisplayControls.FromNative(nativeDisplaySize));
        Assert.Equal(VLCVoutDisplayControl.ChangeSourceCrop, VLCVoutDisplayControls.FromNative(nativeDisplaySize + 4));
    }

    [Fact]
    public void DisplayConfigurationFieldsFollowThePlatformLayout()
    {
        byte* configuration = stackalloc byte[72];
        new Span<byte>(configuration, 72).Clear();
        int shift = OperatingSystem.IsWindows() ? 8 : 0;
        configuration[32 + shift] = 1;
        *(int*)(configuration + 36 + shift) = 3;
        *(int*)(configuration + 40 + shift) = 4;
        Assert.True(VLCVoutDisplayControls.IsDisplayFilled((nint)configuration));
        Assert.Equal((3, 4), VLCVoutDisplayControls.Zoom((nint)configuration));
    }

    // msvcrt's _snprintf and the C library's snprintf share this signature.
    private static nint PrintFunction() => OperatingSystem.IsWindows()
        ? NativeLibrary.GetExport(NativeLibrary.Load("msvcrt.dll"), "_snprintf")
        : NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "snprintf");
}

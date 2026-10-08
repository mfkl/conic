using System.Runtime.InteropServices;
using System.Text;
using VLCLR.Native;
using Xunit;

namespace Conic.UITests;

/// <summary>
/// Hands a <see cref="VLCVaList"/> to the C library's own <c>vsnprintf</c>,
/// which consumes it with the platform's real <c>va_arg</c>.
/// </summary>
public sealed unsafe class VLCVaListTests
{
    [Fact]
    public void TheCLibraryReadsIntegersPointersAndDoublesFromTheSlots()
    {
        if (!VLCPlatform.IsSupported) return;
        byte[] format = Encoding.ASCII.GetBytes("%d|%lld|%.2f|%s|%d|%.1f|%p\0");
        byte[] text = Encoding.ASCII.GetBytes("slot\0");
        byte[] output = new byte[128];
        fixed (byte* formatPointer = format) fixed (byte* textPointer = text) fixed (byte* outputPointer = output)
        {
            // More integer and double arguments than System V passes in registers.
            long* slots = stackalloc long[7];
            slots[0] = -42;
            slots[1] = 1L << 40;
            slots[2] = VLCVaList.Double(3.25);
            slots[3] = (long)textPointer;
            slots[4] = 7;
            slots[5] = VLCVaList.Double(-0.5);
            slots[6] = 0;
            VLCVaList.SystemVTag tag;
            int written = Format(outputPointer, (nuint)output.Length, formatPointer, VLCVaList.Create(slots, &tag));
            string result = Encoding.ASCII.GetString(output, 0, written);
            Assert.StartsWith("-42|1099511627776|3.25|slot|7|-0.5|", result);
        }
    }

    private static int Format(byte* output, nuint size, byte* format, nint arguments)
    {
        // msvcrt's _vsnprintf and glibc's vsnprintf share this signature.
        nint function = OperatingSystem.IsWindows()
            ? NativeLibrary.GetExport(NativeLibrary.Load("msvcrt.dll"), "_vsnprintf")
            : NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "vsnprintf");
        return ((delegate* unmanaged[Cdecl]<byte*, nuint, byte*, nint, int>)function)(output, size, format, arguments);
    }
}

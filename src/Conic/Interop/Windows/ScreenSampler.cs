#if SMOKE
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.Windows;

/// <summary>
/// Reads back what is on screen (the composed desktop, so the GPU video too,
/// which RenderTargetBitmap cannot capture) for the engine settings smoke.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ScreenSampler
{
    /// <summary>Samples the screen rectangle (physical pixels) into a <paramref name="columns"/> by <paramref name="rows"/> grid.</summary>
    public static ScreenSample Capture(int left, int top, int width, int height, int columns = 64, int rows = 36)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint bitmap = CreateCompatibleBitmap(screen, width, height);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, left, top, SourceCopy | CaptureBlt))
                throw new InvalidOperationException($"BitBlt failed ({Marshal.GetLastPInvokeError()}).");
            int[] pixels = new int[width * height];
            BitmapInfoHeader header = new() { Size = sizeof(BitmapInfoHeader), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            fixed (int* data = pixels)
                if (GetDIBits(memory, bitmap, 0, (uint)height, data, &header, 0) == 0)
                    throw new InvalidOperationException("GetDIBits failed.");
            return ScreenSample.FromPixels(pixels, width, height, columns, rows);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private const uint SourceCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        // BITMAPINFO's color table; unused for 32-bit pixels.
        public int Colors0, Colors1, Colors2;
    }

    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);
}
#endif // SMOKE

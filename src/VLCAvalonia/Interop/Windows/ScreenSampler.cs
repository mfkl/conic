#if SMOKE
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.Windows;

/// <summary>
/// Reads back what is on screen (the composed desktop, so the GPU video too,
/// which RenderTargetBitmap cannot capture) for the engine settings smoke.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ScreenSampler
{
    /// <summary>A screen area as a grid of cells, each the mean of its pixels.</summary>
    internal sealed record Sample(int Columns, int Rows, (double R, double G, double B)[] Cells)
    {
        public double Luma => Cells.Average(c => .299 * c.R + .587 * c.G + .114 * c.B);
        /// <summary>The mean spread between the largest and smallest channel: 0 for greys.</summary>
        public double Chroma => Cells.Average(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)));
        public (double R, double G, double B) Mean => (Cells.Average(c => c.R), Cells.Average(c => c.G), Cells.Average(c => c.B));
        /// <summary>The mean absolute per-cell luma difference.</summary>
        public double Difference(Sample other) => Cells.Zip(other.Cells).Average(pair =>
            Math.Abs((.299 * pair.First.R + .587 * pair.First.G + .114 * pair.First.B) - (.299 * pair.Second.R + .587 * pair.Second.G + .114 * pair.Second.B)));
        /// <summary>The mean absolute per-cell color difference, over the three channels.</summary>
        public double ColorDifference(Sample other) => Cells.Zip(other.Cells).Average(pair =>
            (Math.Abs(pair.First.R - pair.Second.R) + Math.Abs(pair.First.G - pair.Second.G) + Math.Abs(pair.First.B - pair.Second.B)) / 3);
        public override string ToString() => $"luma={Luma:0.0} chroma={Chroma:0.0} rgb=({Mean.R:0},{Mean.G:0},{Mean.B:0})";
    }

    /// <summary>Samples the screen rectangle (physical pixels) into a <paramref name="columns"/> by <paramref name="rows"/> grid.</summary>
    public static Sample Capture(int left, int top, int width, int height, int columns = 64, int rows = 36)
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
            var cells = new (double R, double G, double B)[columns * rows];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int x0 = column * width / columns, x1 = (column + 1) * width / columns;
                    int y0 = row * height / rows, y1 = (row + 1) * height / rows;
                    double r = 0, g = 0, b = 0; int count = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int pixel = pixels[y * width + x];
                            r += (pixel >> 16) & 0xFF; g += (pixel >> 8) & 0xFF; b += pixel & 0xFF; count++;
                        }
                    cells[row * columns + column] = count == 0 ? default : (r / count, g / count, b / count);
                }
            return new Sample(columns, rows, cells);
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

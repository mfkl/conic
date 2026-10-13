#if SMOKE
namespace Conic.Interop;

/// <summary>
/// A screen area as a grid of cells, each the mean of its pixels, for the
/// engine settings smoke: what is on screen (the composed desktop, so the GPU
/// video too, which RenderTargetBitmap cannot capture). Windows reads it with
/// GDI (Windows/ScreenSampler), macOS with screencapture (MacOS/MacScreenSampler).
/// </summary>
internal sealed record ScreenSample(int Columns, int Rows, (double R, double G, double B)[] Cells)
{
    public double Luma => Cells.Average(c => .299 * c.R + .587 * c.G + .114 * c.B);
    /// <summary>The mean spread between the largest and smallest channel: 0 for greys.</summary>
    public double Chroma => Cells.Average(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)));
    public (double R, double G, double B) Mean => (Cells.Average(c => c.R), Cells.Average(c => c.G), Cells.Average(c => c.B));
    /// <summary>The mean absolute per-cell luma difference.</summary>
    public double Difference(ScreenSample other) => Cells.Zip(other.Cells).Average(pair =>
        Math.Abs((.299 * pair.First.R + .587 * pair.First.G + .114 * pair.First.B) - (.299 * pair.Second.R + .587 * pair.Second.G + .114 * pair.Second.B)));
    /// <summary>The mean absolute per-cell color difference, over the three channels.</summary>
    public double ColorDifference(ScreenSample other) => Cells.Zip(other.Cells).Average(pair =>
        (Math.Abs(pair.First.R - pair.Second.R) + Math.Abs(pair.First.G - pair.Second.G) + Math.Abs(pair.First.B - pair.Second.B)) / 3);
    public override string ToString() => $"luma={Luma:0.0} chroma={Chroma:0.0} rgb=({Mean.R:0},{Mean.G:0},{Mean.B:0})";

    /// <summary>Averages 0xAARRGGBB pixels, row by row, into a <paramref name="columns"/> by <paramref name="rows"/> grid.</summary>
    public static ScreenSample FromPixels(ReadOnlySpan<int> pixels, int width, int height, int columns, int rows)
    {
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
        return new ScreenSample(columns, rows, cells);
    }
}
#endif // SMOKE

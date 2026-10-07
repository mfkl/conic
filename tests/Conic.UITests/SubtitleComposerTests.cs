using Conic.Interop;
using Xunit;

namespace Conic.UITests;

public sealed unsafe class SubtitleComposerTests
{
    [Fact]
    public void RegionsAreClippedToTheCanvasAndInvisibleOnesDropped()
    {
        byte[] pixels = Solid(4, 2, 10, 20, 30, 255);
        fixed (byte* data = pixels)
        {
            SubtitleRegion[] regions =
            [
                new(-1, 9, 4, 2, 16, (nint)data, 255), // one column left of and one row below the 8x10 canvas
                new(0, 0, 4, 2, 16, (nint)data, 0),    // fully transparent
                new(0, 0, 4, 2, 16, 0, 255),           // no pixels
                new(8, 0, 4, 2, 16, (nint)data, 255),  // right of the canvas
            ];
            int count = SubtitleComposer.Clip(regions, 8, 10);
            Assert.Equal(1, count);
            Assert.Equal(new SubtitleRegion(0, 9, 3, 1, 16, (nint)data + 4, 255), regions[0]);
        }
    }

    [Fact]
    public void RegionsArePlacedInTheirUnionWithRegionAlphaApplied()
    {
        byte[] top = Solid(2, 1, 255, 0, 0, 255), bottom = Solid(1, 1, 0, 0, 255, 200);
        fixed (byte* first = top) fixed (byte* second = bottom)
        {
            SubtitleRegion[] regions = [new(2, 1, 2, 1, 8, (nint)first, 255), new(5, 3, 1, 1, 4, (nint)second, 128)];
            int count = SubtitleComposer.Clip(regions, 10, 10);
            SubtitleBounds bounds = SubtitleComposer.Bounds(regions.AsSpan(0, count));
            Assert.Equal(new SubtitleBounds(2, 1, 4, 3), bounds);
            byte[] canvas = new byte[bounds.ByteCount];
            SubtitleComposer.Compose(regions.AsSpan(0, count), bounds, canvas);
            Assert.Equal([255, 0, 0, 255], Pixel(canvas, bounds, 0, 0));
            Assert.Equal([255, 0, 0, 255], Pixel(canvas, bounds, 1, 0));
            Assert.Equal([0, 0, 0, 0], Pixel(canvas, bounds, 2, 0));
            Assert.Equal([0, 0, 255, 100], Pixel(canvas, bounds, 3, 2)); // 200 * 128 / 255
        }
    }

    [Fact]
    public void LaterRegionsBlendOverEarlierOnes()
    {
        byte[] under = Solid(1, 1, 0, 0, 255, 255), over = Solid(1, 1, 255, 255, 255, 128);
        fixed (byte* first = under) fixed (byte* second = over)
        {
            SubtitleRegion[] regions = [new(0, 0, 1, 1, 4, (nint)first, 255), new(0, 0, 1, 1, 4, (nint)second, 255)];
            SubtitleBounds bounds = SubtitleComposer.Bounds(regions);
            byte[] canvas = new byte[bounds.ByteCount];
            SubtitleComposer.Compose(regions, bounds, canvas);
            Assert.Equal([128, 128, 255, 255], Pixel(canvas, bounds, 0, 0));
        }
    }

    [Fact]
    public void NoRegionsMeansAnEmptyOverlay()
    {
        Assert.Equal(0, SubtitleComposer.Clip([], 1920, 1080));
        Assert.True(SubtitleComposer.Bounds([]).IsEmpty);
        Assert.Equal(0, SubtitleComposer.Bounds([]).ByteCount);
    }

    private static byte[] Solid(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = red; pixels[index + 1] = green; pixels[index + 2] = blue; pixels[index + 3] = alpha;
        }
        return pixels;
    }

    private static byte[] Pixel(byte[] canvas, SubtitleBounds bounds, int x, int y) =>
        canvas.AsSpan((y * bounds.Width + x) * 4, 4).ToArray();
}

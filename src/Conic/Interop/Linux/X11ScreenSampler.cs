#if SMOKE
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.Linux;

/// <summary>
/// Reads back what is on screen for the engine settings smoke with X11's
/// XGetImage on the root window, which includes the composited DMA-BUF video.
/// Conic runs on X11 on Linux (Xorg, or XWayland in a Wayland session).
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class X11ScreenSampler
{
    private const string Library = "libX11.so.6";
    private const int ZPixmap = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int Width, Height, XOffset, Format;
        public byte* Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
    }

    /// <summary>Samples the screen rectangle (physical pixels) into a grid.</summary>
    public static ScreenSample Capture(int left, int top, int width, int height, int columns = 64, int rows = 36)
    {
        if (Environment.GetEnvironmentVariable("DISPLAY") is not { Length: > 0 })
            throw new PlatformNotSupportedException("The screen is read through X11, and no X11 display is set.");
        nint display = XOpenDisplay(0);
        if (display == 0)
            throw new InvalidOperationException("XOpenDisplay failed.");
        try
        {
            XImage* image = XGetImage(display, XDefaultRootWindow(display), left, top, (uint)width, (uint)height, ~0UL, ZPixmap);
            if (image == null)
                throw new InvalidOperationException($"XGetImage failed for {width}x{height} at {left},{top}.");
            try
            {
                if (image->BitsPerPixel != 32 || image->ByteOrder != 0)
                    throw new InvalidOperationException($"Unsupported X11 image: {image->BitsPerPixel} bits per pixel, byte order {image->ByteOrder}.");
                int[] pixels = new int[width * height];
                for (int y = 0; y < height; y++)
                    new ReadOnlySpan<int>(image->Data + (long)y * image->BytesPerLine, width).CopyTo(pixels.AsSpan(y * width, width));
                return ScreenSample.FromPixels(pixels, width, height, columns, rows);
            }
            finally { XDestroyImage(image); }
        }
        finally { XCloseDisplay(display); }
    }

    /// <summary>Moves the X11 pointer to a screen position (physical pixels), for smokes that need it off the controls.</summary>
    public static void WarpPointer(int x, int y)
    {
        nint display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("XOpenDisplay failed.");
        try
        {
            XWarpPointer(display, 0, XDefaultRootWindow(display), 0, 0, 0, 0, x, y);
            XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }

    [LibraryImport(Library)]
    private static partial int XWarpPointer(nint display, nint sourceWindow, nint destinationWindow, int sourceX, int sourceY,
        uint sourceWidth, uint sourceHeight, int destinationX, int destinationY);

    [LibraryImport(Library)]
    private static partial int XFlush(nint display);

    [LibraryImport(Library)]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport(Library)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Library)]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport(Library)]
    private static partial XImage* XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [LibraryImport(Library)]
    private static partial int XDestroyImage(XImage* image);
}
#endif // SMOKE

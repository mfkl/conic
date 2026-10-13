#if SMOKE
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>
/// Reads back what is on screen for the engine settings smoke with
/// screencapture, which sees the composited IOSurface video. It needs the
/// Screen Recording permission for the terminal that runs the smoke.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacScreenSampler
{
    /// <summary>Samples the screen rectangle (physical pixels at <paramref name="scaling"/>) into a grid.</summary>
    public static ScreenSample Capture(PixelRect area, double scaling, int columns = 64, int rows = 36)
    {
        // screencapture takes points; it writes the area at the display's own scale.
        Rect points = area.ToRect(scaling);
        string file = Path.Combine(Path.GetTempPath(), $"vlc-avalonia-sample-{Guid.NewGuid():N}.png");
        try
        {
            using (Process capture = Process.Start(new ProcessStartInfo("/usr/sbin/screencapture",
                ["-x", "-t", "png", "-R", FormattableString.Invariant($"{points.X:0},{points.Y:0},{points.Width:0},{points.Height:0}"), file])
                { UseShellExecute = false })!)
            {
                if (!capture.WaitForExit(10_000) || capture.ExitCode != 0 || !File.Exists(file))
                    throw new InvalidOperationException("screencapture failed (does the terminal have the Screen Recording permission?).");
            }
            using Bitmap bitmap = new(file);
            int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
            int[] pixels = new int[width * height];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), pixels.Length * 4, width * 4); }
            finally { handle.Free(); }
            // 0xAARRGGBB as the sample expects: BGRA bytes on a little-endian CPU.
            if (bitmap.Format == PixelFormat.Rgba8888)
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (int)((uint)pixels[i] & 0xFF00FF00 | ((uint)pixels[i] & 0xFF) << 16 | ((uint)pixels[i] >> 16) & 0xFF);
            return ScreenSample.FromPixels(pixels, width, height, columns, rows);
        }
        finally { File.Delete(file); }
    }
}
#endif // SMOKE

namespace Conic.Interop;

/// <summary>One VLC subtitle region: straight-alpha RGBA rows placed on the subtitle canvas.</summary>
internal readonly record struct SubtitleRegion(int X, int Y, int Width, int Height, int Pitch, nint Pixels, int Alpha);

internal readonly record struct SubtitleBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int ByteCount => IsEmpty ? 0 : Width * Height * 4;
}

/// <summary>
/// Flattens VLC's rendered subtitle regions into one straight-alpha RGBA image
/// covering their union. The canvas is the visible video area.
/// </summary>
internal static unsafe class SubtitleComposer
{
    public const int MaximumCanvas = 16384;

    /// <summary>Clips regions to the canvas in place and returns how many remain visible.</summary>
    public static int Clip(Span<SubtitleRegion> regions, int canvasWidth, int canvasHeight)
    {
        if (canvasWidth is <= 0 or > MaximumCanvas || canvasHeight is <= 0 or > MaximumCanvas) return 0;
        int count = 0;
        foreach (SubtitleRegion region in regions)
        {
            if (region.Pixels == 0 || region.Alpha <= 0 || region.Width <= 0 || region.Height <= 0 || region.Pitch < region.Width * 4) continue;
            int left = Math.Max(region.X, 0), top = Math.Max(region.Y, 0);
            int right = (int)Math.Min((long)region.X + region.Width, canvasWidth), bottom = (int)Math.Min((long)region.Y + region.Height, canvasHeight);
            if (right <= left || bottom <= top) continue;
            nint pixels = region.Pixels + (nint)(top - region.Y) * region.Pitch + (nint)(left - region.X) * 4;
            regions[count++] = region with { X = left, Y = top, Width = right - left, Height = bottom - top, Pixels = pixels, Alpha = Math.Min(region.Alpha, 255) };
        }
        return count;
    }

    public static SubtitleBounds Bounds(ReadOnlySpan<SubtitleRegion> clipped)
    {
        if (clipped.IsEmpty) return default;
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (SubtitleRegion region in clipped)
        {
            left = Math.Min(left, region.X); top = Math.Min(top, region.Y);
            right = Math.Max(right, region.X + region.Width); bottom = Math.Max(bottom, region.Y + region.Height);
        }
        return new(left, top, right - left, bottom - top);
    }

    /// <summary>Draws clipped regions in order (later regions on top) into <paramref name="destination"/>, rows of bounds.Width * 4 bytes.</summary>
    public static void Compose(ReadOnlySpan<SubtitleRegion> clipped, SubtitleBounds bounds, Span<byte> destination)
    {
        if (destination.Length != bounds.ByteCount) throw new ArgumentException("Destination does not match the subtitle bounds.", nameof(destination));
        destination.Clear();
        foreach (SubtitleRegion region in clipped)
        {
            for (int row = 0; row < region.Height; row++)
            {
                byte* source = (byte*)(region.Pixels + (nint)row * region.Pitch);
                Span<byte> target = destination.Slice(((region.Y - bounds.Y + row) * bounds.Width + region.X - bounds.X) * 4, region.Width * 4);
                for (int x = 0; x < target.Length; x += 4)
                    Blend(source + x, target.Slice(x, 4), region.Alpha);
            }
        }
    }

    // Straight-alpha source-over.
    private static void Blend(byte* source, Span<byte> target, int regionAlpha)
    {
        int alpha = (source[3] * regionAlpha + 127) / 255;
        if (alpha == 0) return;
        int targetAlpha = target[3];
        if (targetAlpha == 0 || alpha == 255)
        {
            target[0] = source[0]; target[1] = source[1]; target[2] = source[2]; target[3] = (byte)alpha;
            return;
        }
        int under = targetAlpha * (255 - alpha) / 255;
        int outputAlpha = alpha + under;
        for (int channel = 0; channel < 3; channel++)
            target[channel] = (byte)((source[channel] * alpha + target[channel] * under) / outputAlpha);
        target[3] = (byte)outputAlpha;
    }
}

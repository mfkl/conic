using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Conic.Services;

/// <summary>
/// Reads thumbnails that Linux desktops (GNOME, KDE and other file managers)
/// already generated, following the freedesktop.org Thumbnail Managing
/// Standard. It never generates thumbnails or decodes media.
/// </summary>
internal static class FreedesktopThumbnail
{
    private static readonly string[] Sizes = ["xx-large", "x-large", "large", "normal"];
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private const int MaximumBytes = 4 * 1024 * 1024;

    public static Task<byte[]?> ReadAsync(string path) =>
        OperatingSystem.IsWindows() ? Task.FromResult<byte[]?>(null) : Task.FromResult(Read(path, CacheRoot));

    /// <summary>$XDG_CACHE_HOME/thumbnails, defaulting to ~/.cache/thumbnails.</summary>
    internal static string CacheRoot =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } cache && Path.IsPathRooted(cache)
            ? cache
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "thumbnails");

    internal static byte[]? Read(string path, string cacheRoot)
    {
        FileInfo file = new(path);
        if (!file.Exists) return null;
        string uri = FileUri(file.FullName);
        string name = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(uri))) + ".png";
        long modified = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds();
        foreach (string size in Sizes)
        {
            string candidate = Path.Combine(cacheRoot, size, name);
            try
            {
                FileInfo thumbnail = new(candidate);
                if (!thumbnail.Exists || thumbnail.Length is 0 or > MaximumBytes) continue;
                byte[] bytes = File.ReadAllBytes(candidate);
                // A thumbnail is valid only for the file revision it was made from.
                if (IsCurrent(bytes, uri, modified)) return bytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>
    /// The URI the standard hashes: file:// plus the absolute path with every
    /// byte outside RFC 3986's unreserved, sub-delims, ':', '@' and '/'
    /// percent-encoded as UTF-8, as GLib's g_filename_to_uri does.
    /// </summary>
    internal static string FileUri(string absolutePath)
    {
        StringBuilder uri = new("file://");
        foreach (byte value in Encoding.UTF8.GetBytes(absolutePath))
        {
            char c = (char)value;
            if (value < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=:@/".Contains(c)))
                uri.Append(c);
            else
                uri.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        return uri.ToString();
    }

    /// <summary>Checks the PNG's Thumb::URI and Thumb::MTime text chunks.</summary>
    internal static bool IsCurrent(ReadOnlySpan<byte> png, string uri, long modifiedSeconds)
    {
        if (!png.StartsWith(PngSignature)) return false;
        string? thumbUri = null, thumbModified = null;
        int offset = PngSignature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            if (length < 0 || offset + 12 + length > png.Length) return false;
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = png.Slice(offset + 8, length);
            if (type.SequenceEqual("tEXt"u8))
            {
                int separator = data.IndexOf((byte)0);
                if (separator > 0)
                {
                    string key = Encoding.Latin1.GetString(data[..separator]);
                    string value = Encoding.Latin1.GetString(data[(separator + 1)..]);
                    if (key == "Thumb::URI") thumbUri = value;
                    else if (key == "Thumb::MTime") thumbModified = value;
                }
            }
            else if (type.SequenceEqual("IEND"u8)) break;
            offset += 12 + length;
        }
        return thumbUri == uri &&
            long.TryParse(thumbModified, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) &&
            seconds == modifiedSeconds;
    }
}

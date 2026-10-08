using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using VLCAvalonia.Services;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class FreedesktopThumbnailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "freedesktop-thumbnails-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("/home/user/Videos/clip.mkv", "file:///home/user/Videos/clip.mkv")]
    [InlineData("/home/user/My Videos/été [1].mkv", "file:///home/user/My%20Videos/%C3%A9t%C3%A9%20%5B1%5D.mkv")]
    [InlineData("/media/a&b/x,y=(z)~!.mp4", "file:///media/a&b/x,y=(z)~!.mp4")]
    public void FileUrisAreEscapedLikeGlib(string path, string uri) =>
        Assert.Equal(uri, FreedesktopThumbnail.FileUri(path));

    [Fact]
    public void TheLargestCurrentThumbnailIsReturned()
    {
        string media = Media(out string uri, out long modified);
        string normal = Write("normal", uri, Png(uri, modified));
        Assert.Equal(File.ReadAllBytes(normal), FreedesktopThumbnail.Read(media, _root));
        string large = Write("large", uri, Png(uri, modified, extra: 1));
        Assert.Equal(File.ReadAllBytes(large), FreedesktopThumbnail.Read(media, _root));
    }

    [Fact]
    public void ThumbnailsOfAnotherRevisionOrFileAreIgnored()
    {
        string media = Media(out string uri, out long modified);
        Write("large", uri, Png(uri, modified - 60));
        Assert.Null(FreedesktopThumbnail.Read(media, _root));
        Write("large", uri, Png(uri + "-other", modified));
        Assert.Null(FreedesktopThumbnail.Read(media, _root));
        Write("large", uri, Encoding.ASCII.GetBytes("not a png"));
        Assert.Null(FreedesktopThumbnail.Read(media, _root));
        Assert.Null(FreedesktopThumbnail.Read(Path.Combine(_root, "missing.mkv"), _root));
    }

    private string Media(out string uri, out long modified)
    {
        Directory.CreateDirectory(_root);
        string media = Path.Combine(_root, "clip é.mkv");
        File.WriteAllText(media, "fixture");
        uri = FreedesktopThumbnail.FileUri(Path.GetFullPath(media));
        modified = new DateTimeOffset(new FileInfo(media).LastWriteTimeUtc).ToUnixTimeSeconds();
        return media;
    }

    private string Write(string size, string uri, byte[] png)
    {
        string directory = Path.Combine(_root, size);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(uri))) + ".png");
        File.WriteAllBytes(path, png);
        return path;
    }

    // The reader only inspects the signature and text chunks; CRCs and image data are not needed.
    private static byte[] Png(string uri, long modified, int extra = 0)
    {
        using MemoryStream png = new();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", new byte[13 + extra]);
        Chunk(png, "tEXt", Encoding.Latin1.GetBytes("Thumb::URI\0" + uri));
        Chunk(png, "tEXt", Encoding.Latin1.GetBytes("Thumb::MTime\0" + modified));
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

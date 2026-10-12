using Conic.Controls;
using Xunit;

namespace Conic.UITests;

public sealed class MediaThumbnailTests
{
    [Theory]
    [InlineData(@"C:\music\song.MP3", true)]
    [InlineData("/home/user/track.flac", true)]
    [InlineData("https://example.com/stream.ogg?token=1", true)]
    [InlineData(@"C:\videos\movie.mp4", false)]
    [InlineData("https://example.com/live", false)]
    [InlineData("", false)]
    public void ThumbnailUsesTheMusicPlaceholderForAudioFiles(string source, bool audio) =>
        Assert.Equal(audio, MediaThumbnail.LooksLikeAudio(source));
}

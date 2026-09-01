using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class MediaOpeningTests
{
    [Fact]
    public void CanonicalizesHttpAndRemovesCredentialsFromDisplay()
    {
        MediaOpenResult result = new MediaSourceValidator(new FakeFiles()).Validate("HTTPS://user:secret@EXAMPLE.com:443/a?q=1", MediaSourceKind.NetworkUrl);
        Assert.True(result.Succeeded); Assert.Equal("https://example.com/a?q=1", result.Source!.DisplayName); Assert.DoesNotContain("secret", result.Source.DisplayName);
    }
    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    public void RejectsUnsupportedUrls(string source) => Assert.False(new MediaSourceValidator(new FakeFiles()).Validate(source, MediaSourceKind.NetworkUrl).Succeeded);

    [Fact]
    public void RejectsFoldersUntilQueueCapabilityExists()
    {
        FakeFiles files = new() { Directory = true };
        MediaOpenResult result = new MediaSourceValidator(files).Validate("c:/media", MediaSourceKind.Folder);
        Assert.False(result.Succeeded); Assert.Contains("playlist", result.UserMessage!, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void HistoryDeduplicatesAndHonorsPrivacyAndThresholds()
    {
        MemoryHistory storage = new(); MediaHistoryService history = new(storage, () => DateTimeOffset.UnixEpoch);
        MediaSource source = new("C:\\Media\\One.mp4", MediaSourceKind.File, "One.mp4");
        history.RecordOpened(source, true); history.RecordOpened(source with { CanonicalSource = "c:\\media\\one.mp4" }, true);
        Assert.Single(history.Recent); history.Checkpoint(source, TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(2), true, 30, 20);
        Assert.NotNull(history.FindResume(source)); history.Checkpoint(source, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), true, 30, 20);
        Assert.NotNull(history.FindResume(source)); history.ClearResume(); Assert.Empty(history.Resume); history.ClearRecent(); Assert.Empty(history.Recent);
    }
    private sealed class FakeFiles : IMediaFileSystem
    {
        public bool Directory; public bool File = true;
        public bool FileExists(string path) => File; public bool DirectoryExists(string path) => Directory; public string GetFullPath(string path) => path.Replace('/', '\\');
    }
    private sealed class MemoryHistory : IMediaHistoryStorage { public string? Content; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; }
}

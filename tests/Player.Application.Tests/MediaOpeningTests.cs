using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class MediaOpeningTests
{
    [Fact]
    public void CanonicalizesHttpAndRejectsCredentials()
    {
        MediaOpenResult result = new MediaSourceValidator(new FakeFiles()).Validate("HTTPS://user:secret@EXAMPLE.com:443/a?q=1", MediaSourceKind.NetworkUrl);
        Assert.False(result.Succeeded); Assert.DoesNotContain("secret", result.UserMessage!);
    }
    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    public void RejectsUnsupportedUrls(string source) => Assert.False(new MediaSourceValidator(new FakeFiles()).Validate(source, MediaSourceKind.NetworkUrl).Succeeded);

    [Fact]
    public void AcceptsFoldersForQueueExpansion()
    {
        FakeFiles files = new() { Directory = true };
        MediaOpenResult result = new MediaSourceValidator(files).Validate("c:/media", MediaSourceKind.Folder);
        Assert.True(result.Succeeded); Assert.Equal(MediaSourceKind.Folder, result.Source!.Kind);
    }
    [Fact]
    public void EngineNormalizerPreservesReviewedUrlsAndCanonicalizesFiles()
    {
        Assert.Equal("https://example.com/A?q=x", MediaEngineSourceNormalizer.Normalize("https://example.com/A?q=x"));
        Assert.Throws<ArgumentException>(() => MediaEngineSourceNormalizer.Normalize("https://user:secret@example.com/a"));
    }
    [Fact]
    public void UrlIdentityKeepsPathCaseSignificant()
    {
        MemoryHistory storage = new(); MediaHistoryService history = new(storage);
        history.RecordOpened(new("https://example.com/A", MediaSourceKind.NetworkUrl, "A"), true);
        history.RecordOpened(new("https://example.com/a", MediaSourceKind.NetworkUrl, "a"), true);
        Assert.Equal(2, history.Recent.Count);
    }
    [Fact]
    public void ResumeRemovalAndNearEndClearExistingEntry()
    {
        MemoryHistory storage = new(); MediaHistoryService history = new(storage);
        MediaSource source = new("C:\\One.mp4", MediaSourceKind.File, "One");
        history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromMinutes(2), true, 30, 20);
        history.RemoveResume(source.CanonicalSource); Assert.Null(history.FindResume(source));
        history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromMinutes(2), true, 30, 20);
        history.Checkpoint(source, TimeSpan.FromSeconds(115), TimeSpan.FromMinutes(2), true, 30, 20);
        Assert.Null(history.FindResume(source));
    }
    [Fact]
    public void FileIdentityDeduplicatesCase()
    {
        MemoryHistory storage = new(); MediaHistoryService history = new(storage);
        history.RecordOpened(new("C:\\A.mp4", MediaSourceKind.File, "A"), true);
        history.RecordOpened(new("c:\\a.mp4", MediaSourceKind.File, "a"), true);
        Assert.Single(history.Recent);
    }
    [Fact]
    public void LoadNormalizesFileDuplicatesAndPreservesUrlCase()
    {
        MemoryHistory storage = new() { Content = "{\"Version\":1,\"Recent\":[{\"Source\":\"C:\\\\A.mp4\",\"DisplayName\":\"old\",\"LastOpenedUtc\":\"2020-01-01T00:00:00Z\"},{\"Source\":\"c:\\\\a.mp4\",\"DisplayName\":\"new\",\"LastOpenedUtc\":\"2021-01-01T00:00:00Z\"},{\"Source\":\"https://example.com/A?q=x\",\"DisplayName\":\"old-url\",\"LastOpenedUtc\":\"2020-01-01T00:00:00Z\"},{\"Source\":\"https://example.com/A?q=x\",\"DisplayName\":\"new-url\",\"LastOpenedUtc\":\"2021-01-01T00:00:00Z\"},{\"Source\":\"https://example.com/a?q=x\",\"DisplayName\":\"a\",\"LastOpenedUtc\":\"2021-01-01T00:00:00Z\"}]}" };
        MediaHistoryService history = new(storage); history.Load(); Assert.Equal(3, history.Recent.Count); Assert.Contains(history.Recent, x => x.DisplayName == "new"); Assert.Contains(history.Recent, x => x.DisplayName == "new-url");
    }
    [Fact]
    public void DisabledAndNearEndHistoryWritesFollowPolicy()
    {
        CountingHistory storage = new(); MediaHistoryService history = new(storage); MediaSource source = new("a.mp4", MediaSourceKind.File, "a");
        history.RecordOpened(source, false); history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), false, 30, 20); Assert.Equal(0, storage.Writes);
        history.Checkpoint(source, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(100), true, 30, 20); Assert.Equal(0, storage.Writes);
        history.Checkpoint(source, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100), true, 30, 20); history.Checkpoint(source, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(100), true, 30, 20); Assert.Equal(2, storage.Writes);
    }
    [Fact]
    public void UnsupportedVersionIsPreservedAndResetWithDiagnostic()
    {
        MemoryHistory storage = new() { Content = "{\"Version\":999,\"Recent\":[],\"Resume\":[]}" }; MediaHistoryService service = new(storage); service.Load();
        Assert.Equal(storage.Preserved, "{\"Version\":999,\"Recent\":[],\"Resume\":[]}"); Assert.NotNull(service.LoadDiagnostic); Assert.Contains("\"Version\": 1", storage.Content);
        MediaHistoryService reload = new(storage); reload.Load(); Assert.Null(reload.LoadDiagnostic);
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
    private sealed class MemoryHistory : IMediaHistoryStorage { public string? Content; public string? Preserved; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; public void PreserveInvalid(string content) => Preserved = content; }
    private sealed class CountingHistory : IMediaHistoryStorage { public int Writes; public string? Content; public string? Read() => Content; public void WriteAtomically(string content) { Writes++; Content = content; } }
}

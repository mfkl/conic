using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class LibraryHistoryTests
{
    [Fact]
    public void FavoritesAreExplicitDeduplicatedAndDoNotScan()
    {
        MemoryStorage storage = new();
        MediaHistoryService history = new(storage, () => DateTimeOffset.UnixEpoch);
        MediaSource first = new("C:/media/first.mp4", MediaSourceKind.File, "first.mp4");
        MediaSource duplicate = new("c:/MEDIA/first.mp4", MediaSourceKind.File, "renamed.mp4");

        history.AddFavorite(first);
        history.AddFavorite(duplicate);

        FavoriteMediaEntry favorite = Assert.Single(history.Favorites);
        Assert.Equal("renamed.mp4", favorite.DisplayName);
        Assert.Empty(history.Recent);
        Assert.Empty(history.Resume);
        Assert.Contains("Favorites", storage.Content);
    }

    [Fact]
    public void VersionOneHistoryMigratesAndClearAllRemovesEveryLibrarySection()
    {
        MemoryStorage storage = new()
        {
            Content = "{\"Version\":1,\"Recent\":[{\"Source\":\"a.mp4\",\"DisplayName\":\"a\",\"LastOpenedUtc\":\"1970-01-01T00:00:00+00:00\"}],\"Resume\":[{\"Source\":\"a.mp4\",\"Position\":\"00:00:40\",\"Duration\":\"00:01:00\",\"UpdatedUtc\":\"1970-01-01T00:00:00+00:00\"}]}"
        };
        MediaHistoryService history = new(storage);

        history.Load();
        history.AddFavorite(new("b.mp4", MediaSourceKind.File, "b"));
        history.ClearAll();

        Assert.Empty(history.Recent);
        Assert.Empty(history.Resume);
        Assert.Empty(history.Favorites);
        Assert.Contains("\"Version\": 2", storage.Content);
    }

    [Fact]
    public void MalformedVersionTwoEntriesAreDiscardedAndPersistedAsNormalizedHistory()
    {
        MemoryStorage storage = new()
        {
            Content = "{\"Version\":2,\"Recent\":[{\"Source\":null,\"DisplayName\":\"bad\",\"LastOpenedUtc\":\"1970-01-01T00:00:00+00:00\"},{\"Source\":\"good.mp4\",\"DisplayName\":\"good\",\"LastOpenedUtc\":\"1970-01-01T00:00:00+00:00\"}],\"Resume\":[{\"Source\":\"\",\"Position\":\"00:00:40\",\"Duration\":\"00:01:00\",\"UpdatedUtc\":\"1970-01-01T00:00:00+00:00\"},{\"Source\":\"good.mp4\",\"Position\":\"00:00:40\",\"Duration\":\"00:01:00\",\"UpdatedUtc\":\"1970-01-01T00:00:00+00:00\"}],\"Favorites\":[{\"Source\":null,\"DisplayName\":null,\"AddedUtc\":\"1970-01-01T00:00:00+00:00\"},{\"Source\":\"favorite.mp4\",\"DisplayName\":\"favorite\",\"AddedUtc\":\"1970-01-01T00:00:00+00:00\"}]}"
        };
        MediaHistoryService history = new(storage);

        history.Load();

        Assert.Null(history.LoadDiagnostic);
        Assert.Equal("good.mp4", Assert.Single(history.Recent).Source);
        Assert.Equal("good.mp4", Assert.Single(history.Resume).Source);
        Assert.Equal("favorite.mp4", Assert.Single(history.Favorites).Source);
        Assert.DoesNotContain("\"Source\": null", storage.Content);
    }

    [Fact]
    public void NormalizedCurrentVersionTwoHistoryLoadsWithoutWritingToReadOnlyStorage()
    {
        ReadOnlyStorage storage = new("{\"Version\":2,\"Recent\":[{\"Source\":\"a.mp4\",\"DisplayName\":\"a\",\"LastOpenedUtc\":\"1970-01-01T00:00:00+00:00\"}],\"Resume\":[],\"Favorites\":[]}");
        MediaHistoryService history = new(storage);

        history.Load();

        Assert.Null(history.LoadDiagnostic);
        Assert.Equal("a.mp4", Assert.Single(history.Recent).Source);
        Assert.Equal(0, storage.WriteAttempts);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Recent\":[],\"Resume\":[]}")]
    [InlineData("{\"Version\":2,\"Recent\":[{\"Source\":null,\"DisplayName\":\"bad\",\"LastOpenedUtc\":\"1970-01-01T00:00:00+00:00\"}],\"Resume\":[],\"Favorites\":[]}")]
    public void NormalizationWritebackFailureKeepsHistoryInMemoryAndReportsDiagnostic(string content)
    {
        ReadOnlyStorage storage = new(content);
        MediaHistoryService history = new(storage);

        history.Load();

        Assert.Contains("could not be normalized", history.LoadDiagnostic);
        Assert.Equal(1, storage.WriteAttempts);
        Assert.Empty(history.Recent);
        Assert.Empty(history.Resume);
        Assert.Empty(history.Favorites);
    }

    private sealed class MemoryStorage : IMediaHistoryStorage
    {
        public string? Content { get; set; }
        public string? Read() => Content;
        public void WriteAtomically(string content) => Content = content;
    }

    private sealed class ReadOnlyStorage(string content) : IMediaHistoryStorage
    {
        public int WriteAttempts { get; private set; }
        public string? Read() => content;
        public void WriteAtomically(string value)
        {
            WriteAttempts++;
            throw new UnauthorizedAccessException("read-only test storage");
        }
    }
}

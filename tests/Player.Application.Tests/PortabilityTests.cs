using Xunit;

namespace Player.Application.Tests;

public sealed class PortabilityTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void FileIdentitySurvivesHistoryReloadAndRemoval(bool caseSensitive, int count)
    {
        MemoryHistory storage = new();
        MediaSourceIdentity identity = new(caseSensitive);
        MediaHistoryService history = new(storage, identity: identity);
        MediaSource upper = new("/videos/A.mp4", MediaSourceKind.File, "A");
        MediaSource lower = new("/videos/a.mp4", MediaSourceKind.File, "a");
        foreach (MediaSource source in new[] { upper, lower })
        {
            history.RecordOpened(source, true);
            history.AddFavorite(source);
            history.Checkpoint(source, TimeSpan.FromSeconds(source == upper ? 40 : 60), TimeSpan.FromMinutes(3), true, 30, 20);
        }
        MediaHistoryService reloaded = new(storage, identity: identity);
        reloaded.Load();
        Assert.Equal(count, reloaded.Recent.Count);
        Assert.Equal(count, reloaded.Favorites.Count);
        Assert.Equal(count, reloaded.Resume.Count);
        Assert.Equal(TimeSpan.FromSeconds(caseSensitive ? 40 : 60), reloaded.FindResume(upper)!.Position);
        reloaded.RemoveFavorite(upper.CanonicalSource);
        reloaded.RemoveRecent(upper.CanonicalSource);
        reloaded.RemoveResume(upper.CanonicalSource);
        Assert.Equal(count - 1, reloaded.Favorites.Count);
        Assert.Equal(count - 1, reloaded.Recent.Count);
        Assert.Equal(count - 1, reloaded.Resume.Count);
        Assert.Equal(caseSensitive, reloaded.IsFavorite(lower.CanonicalSource));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UrlPathsRemainCaseSensitiveWithEitherFilePolicy(bool caseSensitive)
    {
        MediaSourceIdentity identity = new(caseSensitive);
        Assert.NotEqual(identity.KeyFromCanonicalSource("https://example.com/A"), identity.KeyFromCanonicalSource("https://example.com/a"));
    }

    [Theory]
    [InlineData("cmd+o")]
    [InlineData("Command+O")]
    [InlineData("Meta+O")]
    public void CommandShortcutsPersistAndDispatchWithoutMatchingControl(string gesture)
    {
        MemorySettings storage = new();
        PlayerSettingsService settings = new(storage);
        int invoked = 0;
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open", "Media", "Ctrl+O"), _ => invoked++);
        Assert.True(new PlayerShortcutEditor(settings, registry).TrySave([new(PlayerCommandId.OpenMedia, gesture)], out _));
        PlayerSettingsService reloaded = new(storage);
        reloaded.Load();
        var active = PlayerShortcutPolicy.Effective(registry, reloaded.Current.ShortcutOverrides);
        Assert.False(PlayerShortcutPolicy.TryDispatch(registry, active, "Ctrl+O"));
        Assert.True(PlayerShortcutPolicy.TryDispatch(registry, active, "Meta+O"));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void UnixDoesNotOfferOrSelectWindowsAudioModules()
    {
        Assert.Equal(["Default"], AudioOutputPolicy.Options(windows: false));
        Assert.Equal("Default", AudioOutputPolicy.Effective("mmdevice", windows: false));
        Assert.Equal("mmdevice", AudioOutputPolicy.Effective("mmdevice", windows: true));
    }

    private sealed class MemoryHistory : IMediaHistoryStorage
    {
        private string? _content;
        public string? Read() => _content;
        public void WriteAtomically(string content) => _content = content;
    }

    private sealed class MemorySettings : IPlayerSettingsStorage
    {
        private string? _content;
        public string? Read() => _content;
        public void WriteAtomically(string content) => _content = content;
        public void PreserveInvalid(string content) { }
    }
}

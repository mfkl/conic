using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerSettingsServiceTests
{
    [Fact]
    public void PersistsAndReloadsNormalizedSettings()
    {
        MemoryStorage storage = new();
        PlayerSettingsService first = new(storage);
        first.Update(s => s with { Theme = "Dark", SkipForwardSeconds = 33 });
        PlayerSettingsService reloaded = new(storage); reloaded.Load();
        Assert.Equal("Dark", reloaded.Current.Theme); Assert.Equal(33, reloaded.Current.SkipForwardSeconds);
    }
    [Fact]
    public void InvalidDocumentIsPreservedAndDefaultsAreRestored()
    {
        MemoryStorage storage = new() { Content = "not json" };
        PlayerSettingsService service = new(storage); service.Load();
        Assert.Equal(PlayerSettings.Default, service.Current); Assert.Equal("not json", storage.Preserved);
    }
    [Fact]
    public void OlderVersionMigratesAndLiveClassificationIsExplicit()
    {
        MemoryStorage storage = new() { Content = "{\"version\":1,\"resumePlayback\":false,\"rememberVolume\":true,\"theme\":\"Dark\",\"density\":\"Compact\",\"defaultSpeed\":1.5,\"skipBackwardSeconds\":8,\"skipForwardSeconds\":12}" };
        PlayerSettingsService service = new(storage); service.Load();
        Assert.Equal(PlayerSettings.CurrentVersion, service.Current.Version);
        Assert.Equal("Dark", service.Current.Theme); Assert.Equal("Compact", service.Current.Density); Assert.Equal(3, service.Current.ControlTimeoutSeconds); Assert.Equal(12, service.Current.SkipForwardSeconds);
        Assert.Contains("\"Version\": 3", storage.Content);
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.SkipForwardSeconds)));
        Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.DefaultSpeed)));
    }
    [Fact]
    public void AtomicWriteFailureDoesNotChangeCurrentSettings()
    {
        MemoryStorage storage = new() { ThrowOnWrite = true }; PlayerSettingsService service = new(storage);
        Assert.Throws<IOException>(() => service.Update(s => s with { Theme = "Dark" }));
        Assert.Equal("System", service.Current.Theme);
    }
    [Fact]
    public void PageAndGlobalResetsHaveDistinctScopes()
    {
        MemoryStorage storage = new(); PlayerSettingsService service = new(storage);
        service.Update(s => s with { Theme = "Dark", SkipForwardSeconds = 42, ResumePlayback = false }); service.ResetAppearance();
        Assert.Equal("System", service.Current.Theme); Assert.Equal(42, service.Current.SkipForwardSeconds); Assert.False(service.Current.ResumePlayback);
        service.ResetAll(); Assert.Equal(PlayerSettings.Default, service.Current);
    }
    private sealed class MemoryStorage : IPlayerSettingsStorage
    { public string? Content; public string? Preserved; public bool ThrowOnWrite; public string? Read() => Content; public void WriteAtomically(string content) { if (ThrowOnWrite) throw new IOException(); Content = content; } public void PreserveInvalid(string content) => Preserved = content; }
}

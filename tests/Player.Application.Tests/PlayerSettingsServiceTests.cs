using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerSettingsServiceTests
{
    [Fact]
    public void ReduceMotionMigratesAsALivePreference()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"Theme\":\"Dark\",\"ReduceMotion\":true}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.True(service.Current.ReduceMotion);
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.ReduceMotion)));
        Assert.Contains($"\"Version\": {PlayerSettings.CurrentVersion}", storage.Content);
    }

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
        Assert.Contains($"\"Version\": {PlayerSettings.CurrentVersion}", storage.Content);
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
    [Fact]
    public void Version5DocumentMigratesToVersion6WithTheDefaultAccent()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":5,\"Theme\":\"Dark\",\"Density\":\"Compact\",\"SkipForwardSeconds\":25,\"ReduceMotion\":true}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(6, PlayerSettings.CurrentVersion);
        Assert.Equal(PlayerSettings.CurrentVersion, service.Current.Version);
        Assert.Equal("Dark", service.Current.Theme);
        Assert.Equal("Compact", service.Current.Density);
        Assert.Equal(25, service.Current.SkipForwardSeconds);
        Assert.True(service.Current.ReduceMotion);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Contains("\"Version\": 6", storage.Content);
        Assert.Contains($"\"Accent\": \"{AccentPalette.DefaultHex}\"", storage.Content);
        Assert.Null(service.LoadDiagnostic);
    }

    [Fact]
    public void OlderVersionsGetTheDefaultAccent()
    {
        MemoryStorage storage = new() { Content = "{\"version\":3,\"theme\":\"Dark\",\"accent\":\"#1B5DD3\"}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        // Accent did not exist before version 6, so an older document cannot set it.
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal("Dark", service.Current.Theme);
    }

    [Fact]
    public void LightThemeAndPaletteAccentsPersistInTheirCanonicalSpelling()
    {
        MemoryStorage storage = new();
        new PlayerSettingsService(storage).Update(s => s with { Theme = "Light", Accent = " #1b5dd3 " });
        PlayerSettingsService reloaded = new(storage);

        reloaded.Load();

        Assert.Equal("Light", reloaded.Current.Theme);
        Assert.Equal("#1B5DD3", reloaded.Current.Accent);
    }

    [Theory]
    [InlineData("\"Theme\":\"Sepia\",\"Accent\":\"#123456\"")]
    [InlineData("\"Theme\":\"light\",\"Accent\":\"orange\"")]
    [InlineData("\"Theme\":null,\"Accent\":null")]
    [InlineData("\"Accent\":\"\"")]
    public void InvalidThemeAndAccentFallBackToDefaults(string values)
    {
        MemoryStorage storage = new() { Content = $"{{\"Version\":6,\"SkipForwardSeconds\":20,{values}}}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(PlayerSettings.Default.Theme, service.Current.Theme);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal(20, service.Current.SkipForwardSeconds);
    }

    [Fact]
    public void ThemeOptionsAndPaletteMatchTheDesign()
    {
        Assert.Equal(["Dark", "Light", "System"], PlayerSettings.Themes);
        Assert.Equal(9, AccentPalette.Colors.Count);
        Assert.Equal(AccentPalette.DefaultHex, AccentPalette.Colors[0].Hex);
        Assert.Equal(AccentPalette.DefaultHex, PlayerSettings.Default.Accent);
        Assert.All(AccentPalette.Colors, color => Assert.Matches("^#[0-9A-F]{6}$", color.Hex));
        Assert.Equal(AccentPalette.Colors.Count, AccentPalette.Colors.Select(color => color.Name).Distinct().Count());
    }

    [Fact]
    public void ThemeAndAccentApplyLiveAndResetWithTheAppearance()
    {
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.Theme)));
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.Accent)));
        PlayerSettingsService service = new(new MemoryStorage());
        service.Update(s => s with { Theme = "Light", Accent = "#8E43FD", SkipForwardSeconds = 42 });

        service.ResetAppearance();

        Assert.Equal(PlayerSettings.Default.Theme, service.Current.Theme);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal(42, service.Current.SkipForwardSeconds);
    }

    [Theory]
    [InlineData("0.5", .5)]
    [InlineData("1.75", 1.75)]
    [InlineData("4", 2)]
    [InlineData("3", 2)]
    [InlineData("0.1", .25)]
    [InlineData("1.1", 1)]
    [InlineData("1.13", 1.25)]
    public void DefaultSpeedSnapsToTheDesignStepsAndKeepsOldValuesInRange(string stored, double expected)
    {
        // Before phase 9a the setting offered 0.5 to 2 and accepted up to 4.
        MemoryStorage storage = new() { Content = $"{{\"Version\":6,\"DefaultSpeed\":{stored}}}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(expected, service.Current.DefaultSpeed);
        Assert.Null(service.LoadDiagnostic);
        Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.DefaultSpeed)));
    }

    private sealed class MemoryStorage : IPlayerSettingsStorage
    { public string? Content; public string? Preserved; public bool ThrowOnWrite; public string? Read() => Content; public void WriteAtomically(string content) { if (ThrowOnWrite) throw new IOException(); Content = content; } public void PreserveInvalid(string content) => Preserved = content; }
}

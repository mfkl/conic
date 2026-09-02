using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerShortcutAndDiagnosticsTests
{
    [Fact]
    public void ShortcutNormalizationIsDeterministicAndConflictsAreRejected()
    {
        Assert.Equal("Ctrl+Shift+S", PlayerShortcutPolicy.Normalize("shift + control + s"));
        Assert.False(PlayerShortcutPolicy.TryValidate([new(PlayerCommandId.OpenMedia, "Ctrl+O"), new(PlayerCommandId.OpenSettings, "control+o")], out string? error));
        Assert.Contains("already assigned", error);
    }

    [Fact]
    public void DiagnosticsNeverIncludesUriOrCodecGuesses()
    {
        MediaInformationState state = new(4, 1, MediaInformationLoadState.Ready, "Movie", new Dictionary<string, string> { ["title"] = "A title", ["description"] = "line one\nline two" });
        string output = MediaDiagnostics.Build(state);
        Assert.Contains("codecStreams=unavailable-through-engine-ABI", output);
        Assert.DoesNotContain("file://", output);
        Assert.DoesNotContain("line one\nline two", output);
    }

    [Fact]
    public void AudioSettingsMigrateNormalizeAndAreRestartOnly()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":3,\"AudioOutputModule\":\"not-allowed\",\"NormalizationMaximumLevel\":50}" };
        PlayerSettingsService service = new(storage); service.Load();
        Assert.Equal("Default", service.Current.AudioOutputModule);
        Assert.Equal(10, service.Current.NormalizationMaximumLevel);
        Assert.Equal(SettingApplyMode.Restart, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.AudioOutputModule)));
    }

    [Fact]
    public void ShortcutSaveValidatesEntireEffectiveMapAndNeverWritesOnConflict()
    {
        MemoryStorage storage = new();
        PlayerSettingsService settings = new(storage);
        PlayerCommandRegistry registry = Registry();
        PlayerShortcutEditor editor = new(settings, registry);

        Assert.False(editor.TrySave([new(PlayerCommandId.OpenMedia, "Ctrl+,")], out string? error));
        Assert.Contains("already assigned", error);
        Assert.Null(storage.Content);
        Assert.Null(settings.Current.ShortcutOverrides);
    }

    [Fact]
    public void NumericAndUnknownShortcutCommandIdsAreIgnored()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"ShortcutOverrides\":{\"999\":\"Ctrl+Q\",\"OpenMedia\":\"Ctrl+M\"}}" };
        PlayerSettingsService settings = new(storage); settings.Load();
        IReadOnlyDictionary<PlayerCommandId, string> active = PlayerShortcutPolicy.Effective(Registry(), settings.Current.ShortcutOverrides);
        Assert.Equal("Ctrl+M", active[PlayerCommandId.OpenMedia]);
        Assert.DoesNotContain((PlayerCommandId)999, active.Keys);
    }

    [Fact]
    public void DiagnosticsRedactsControlCharactersAndNeverUsesUri()
    {
        MediaInformationState state = new(1, 1, MediaInformationLoadState.Ready, "name\u0001\r\nhttps://secret.example/a?token=1", new Dictionary<string, string> { ["title"] = "x\u0002\ny" });
        string text = MediaDiagnostics.Build(state);
        Assert.DoesNotContain('\r', text.Replace("\r\n", ""));
        Assert.DoesNotContain('\u0001', text);
        Assert.DoesNotContain('\u0002', text);
        Assert.DoesNotContain("token=1", text);
    }

    private static PlayerCommandRegistry Registry()
    {
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "Media", "Ctrl+,"), _ => { });
        return registry;
    }

    private sealed class MemoryStorage : IPlayerSettingsStorage
    {
        public string? Content; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; public void PreserveInvalid(string content) { }
    }
}

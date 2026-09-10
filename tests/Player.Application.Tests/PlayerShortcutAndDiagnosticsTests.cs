using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerShortcutAndDiagnosticsTests
{
    [Fact]
    public void CodecFieldsAreCopiedAndDiagnosticsRedactStreamText()
    {
        Dictionary<string, string> fields = new() { ["Codec"] = "H264", ["Description"] = "secret https://example.com/a?token=private\nextra" };
        MediaCodecStream stream = new(0, fields);
        fields["Codec"] = "changed";
        Assert.Equal("H264", stream.Fields["Codec"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)stream.Fields).Add("x", "y"));
        MediaInformationState state = new(1, 1, MediaInformationLoadState.Ready, "Movie", new Dictionary<string, string>(), CodecStreams: [stream]);
        string diagnostics = MediaDiagnostics.Build(state);
        Assert.Contains("codecStreams=1", diagnostics);
        Assert.Contains("stream.0.Codec=H264", diagnostics);
        Assert.DoesNotContain("token=private", diagnostics);
        Assert.DoesNotContain("\nextra", diagnostics);
    }

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
        Assert.Contains("codecStreams=0", output);
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
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"ShortcutOverrides\":{\"0\":\"Ctrl+Q\",\"999\":\"Ctrl+Q\",\"OpenMedia\":\"Ctrl+M\"}}" };
        PlayerSettingsService settings = new(storage); settings.Load();
        IReadOnlyDictionary<PlayerCommandId, string> active = PlayerShortcutPolicy.Effective(Registry(), settings.Current.ShortcutOverrides);
        Assert.Equal("Ctrl+M", active[PlayerCommandId.OpenMedia]);
        Assert.DoesNotContain((PlayerCommandId)999, active.Keys);
    }

    [Theory]
    [InlineData("Ctrl+Foobar")]
    [InlineData("Ctrl+F25")]
    public void UnknownShortcutKeysAreRejected(string gesture) => Assert.False(PlayerShortcutPolicy.TryValidate([new(PlayerCommandId.OpenMedia, gesture)], out _));

    [Theory]
    [InlineData("Ctrl+F24")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+,")]
    public void SupportedShortcutKeyBoundariesAreAccepted(string gesture) => Assert.True(PlayerShortcutPolicy.TryValidate([new(PlayerCommandId.OpenMedia, gesture)], out _));

    [Fact]
    public void SettingsAndUnavailableMetadataAreImmutable()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"ShortcutOverrides\":{\"OpenMedia\":\"Ctrl+M\"}}" };
        PlayerSettingsService settings = new(storage); settings.Load();
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)settings.Current.ShortcutOverrides!).Add("OpenSettings", "Ctrl+,") );
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)MediaInformationState.Unavailable.Metadata).Add("title", "bad"));
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

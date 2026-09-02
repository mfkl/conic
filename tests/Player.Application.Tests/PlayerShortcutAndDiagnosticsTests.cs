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

    private sealed class MemoryStorage : IPlayerSettingsStorage
    {
        public string? Content; public string? Read() => Content; public void WriteAtomically(string content) => Content = content; public void PreserveInvalid(string content) { }
    }
}

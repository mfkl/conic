using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class SubtitleEngineOptionsTests
{
    private static IReadOnlyList<string> Windows(PlayerSettings settings) => PlayerEngineOptions.Arguments(settings, windows: true, macOS: false);

    [Fact]
    public void DefaultsGiveNoOptions()
    {
        Assert.Empty(SubtitleEngineOptions.Arguments(PlayerSettings.Default));
        Assert.Empty(Windows(PlayerSettings.Default));
    }

    [Fact]
    public void TracksAndLoadingAreOptionsOfTheNextCore()
    {
        Assert.Equal(["--no-spu", "--subsdec-encoding=Windows-1251", "--sub-autodetect-fuzzy=4"],
            Windows(PlayerSettings.Default with { SubtitlesEnabled = false, SubtitleEncoding = "Windows-1251", SubtitleMatching = 4 }));
        // Autoloading off makes the matching irrelevant.
        Assert.Equal(["--no-sub-autodetect-file"], Windows(PlayerSettings.Default with { SubtitleAutoload = false, SubtitleMatching = 1 }));
        // The preferred language stays Playback › Tracks' option.
        Assert.Equal(["--sub-language=none"], Windows(PlayerSettings.Default with { SubtitleLanguage = "none" }));
    }

    [Fact]
    public void StyleIsTheTextRenderersOptions()
    {
        PlayerSettings settings = PlayerSettings.Default with
        {
            SubtitleFont = "Segoe UI", SubtitleTextScale = 150, SubtitleBold = true, SubtitleColor = "#FFFF00", SubtitleOpacity = 50,
            SubtitleOutline = "Thick", SubtitleOutlineColor = "#202020", SubtitleShadowColor = "#FF0000", SubtitleShadowDistance = "Medium",
            SubtitleBackground = true, SubtitleBackgroundOpacity = 75, SubtitleBackgroundColor = "#0000FF", SubtitleMargin = 40,
        };

        Assert.Equal([
            "--freetype-font=Segoe UI", "--sub-text-scale=150", "--freetype-bold", "--freetype-color=16776960", "--freetype-opacity=128",
            "--freetype-outline-thickness=6", "--freetype-outline-color=2105376", "--freetype-shadow-color=16711680", "--freetype-shadow-distance=0.1",
            "--freetype-background-opacity=191", "--freetype-background-color=255", "--sub-margin=40"], SubtitleEngineOptions.Arguments(settings));
        // No shadow is a transparent one; the background's color matters only with the background.
        Assert.Equal(["--freetype-shadow-opacity=0"], SubtitleEngineOptions.Arguments(PlayerSettings.Default with { SubtitleShadow = false, SubtitleShadowColor = "#FF0000" }));
        Assert.Empty(SubtitleEngineOptions.Arguments(PlayerSettings.Default with { SubtitleBackgroundColor = "#FF0000", SubtitleBackgroundOpacity = 90 }));
    }

    [Fact]
    public void DelayIsInTenthsAndTheFileNameReplacesTheCoresDefault()
    {
        Assert.Equal(["--sub-delay=-15"], SubtitleEngineOptions.Arguments(PlayerSettings.Default with { SubtitleDelay = -1500 }));
        Assert.Equal(["--video-title-show", "--video-title-timeout=3000"], SubtitleEngineOptions.Arguments(PlayerSettings.Default with { ShowFileNameOnStart = true, FileNameSeconds = 3 }));
    }

    [Fact]
    public void LiveStyleFollowsTheBackgroundSwitch()
    {
        PlayerSettings settings = PlayerSettings.Default with { SubtitleTextScale = 200, SubtitleColor = "#00FF00", SubtitleOutline = "None", SubtitleBackgroundOpacity = 100 };

        Assert.Equal(new LiveSubtitleSettings(200, 0x00FF00, 0, 0, 0), SubtitleEngineOptions.Live(settings));
        Assert.Equal(new LiveSubtitleSettings(200, 0x00FF00, 0, 255, 0x123456), SubtitleEngineOptions.Live(settings with { SubtitleBackground = true, SubtitleBackgroundColor = "#123456" }));
        Assert.Equal(new LiveSubtitleSettings(100, 0xFFFFFF, 4, 0, 0), SubtitleEngineOptions.Live(PlayerSettings.Default));
    }

    [Theory]
    [InlineData("#ffcc00", "#FFCC00")]
    [InlineData("12ab9F", "#12AB9F")]
    [InlineData(" #000000 ", "#000000")]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("white", "#FFFFFF")]
    [InlineData(null, "#FFFFFF")]
    public void ColorsAreSixHexDigits(string? stored, string expected) =>
        Assert.Equal(expected, SubtitleEngineOptions.NormalizeColor(stored, SubtitleEngineOptions.DefaultColor));

    [Fact]
    public void EveryChoiceIsOneVlcHas()
    {
        Assert.Equal("", SubtitleEngineOptions.Encodings[0].Value);
        Assert.Equal(["1", "2", "3", "4"], SubtitleEngineOptions.Matchings.Select(option => option.Value));
        Assert.Equal([0, 2, 4, 6], SubtitleEngineOptions.Outlines.Select(option => SubtitleEngineOptions.OutlineThickness(option.Value)));
        Assert.Equal([.06, .1, .15], SubtitleEngineOptions.ShadowDistances.Select(option => SubtitleEngineOptions.ShadowDistance(option.Value)));
        Assert.Equal((0, 128, 255), (SubtitleEngineOptions.Opacity(0), SubtitleEngineOptions.Opacity(50), SubtitleEngineOptions.Opacity(100)));
    }
}

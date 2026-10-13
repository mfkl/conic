using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerEngineOptionsTests
{
    private static IReadOnlyList<string> Windows(PlayerSettings settings, string? password = null) =>
        PlayerEngineOptions.Arguments(settings, password, windows: true, macOS: false);

    [Fact]
    public void DefaultsAddNoOptions()
    {
        Assert.Empty(Windows(PlayerSettings.Default));
        Assert.Empty(PlayerEngineOptions.Arguments(PlayerSettings.Default, windows: false, macOS: true));
    }

    [Fact]
    public void PlaybackRowsMapToVlcOptions()
    {
        PlayerSettings settings = PlayerSettings.Default with
        {
            AutoPlay = false, AudioLanguage = "fr,en", SubtitleLanguage = "de", StopAfterEachItem = true, SeekPrecisely = false, AviRepair = "WhenNeeded",
        };

        Assert.Equal(["--start-paused", "--audio-language=fr,en", "--sub-language=de", "--play-and-stop", "--input-fast-seek", "--avi-index=3"], Windows(settings));
        Assert.Equal(["--avi-index=1"], Windows(PlayerSettings.Default with { AviRepair = "Always" }));
        // Never is VLC's own behavior without a dialog: no option.
        Assert.Empty(Windows(PlayerSettings.Default with { AviRepair = "Never" }));
    }

    [Fact]
    public void PictureAddsTheAdjustFilterWithOnlyTheChangedValues()
    {
        PlayerSettings settings = PlayerSettings.Default with { Hue = -45.5, Brightness = 1.25, Gamma = 1.5 };

        Assert.Equal(["--video-filter=adjust", "--hue=-45.5", "--brightness=1.25", "--gamma=1.5"], Windows(settings));
        Assert.True(PlayerEngineOptions.IsNeutralPicture(PlayerSettings.Default));
    }

    [Fact]
    public void FloatsUseTheInvariantCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            Assert.Equal(["--video-filter=adjust", "--contrast=0.75", "--saturation=2.5"], Windows(PlayerSettings.Default with { Contrast = .75, Saturation = 2.5 }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void VideoOutputDeinterlacingAndFraming()
    {
        PlayerSettings settings = PlayerSettings.Default with
        {
            VideoEnabled = false, Deinterlace = "On", DeinterlaceMethod = "yadif2x", DefaultAspectRatio = "4:3", DefaultCrop = "235:100", DefaultZoom = "0.5",
        };

        Assert.Equal(["--no-video", "--deinterlace=1", "--deinterlace-mode=yadif2x", "--aspect-ratio=4:3", "--crop=235:100", "--no-autoscale", "--zoom=0.5"], Windows(settings));
        Assert.Equal(["--deinterlace=0"], Windows(PlayerSettings.Default with { Deinterlace = "Off" }));
    }

    [Fact]
    public void HardwareDecodingKeepsTheZeroCopyPathUnlessTurnedOff()
    {
        Assert.Empty(Windows(PlayerSettings.Default with { HardwareDecoding = "Automatic" }));
        Assert.Equal(["--avcodec-hw=d3d11va"], Windows(PlayerSettings.Default with { HardwareDecoding = "Direct3D11" }));
        Assert.Equal(["--avcodec-hw=none"], Windows(PlayerSettings.Default with { HardwareDecoding = "Off" }));
        Assert.Equal(["--avcodec-hw=none", "--no-videotoolbox"], PlayerEngineOptions.Arguments(PlayerSettings.Default with { HardwareDecoding = "Off" }, windows: false, macOS: true));
        Assert.Equal(["Automatic", "Direct3D11", "Off"], PlayerEngineOptions.HardwareDecodingModes(windows: true).Select(option => option.Value));
        Assert.Equal(["Automatic", "Off"], PlayerEngineOptions.HardwareDecodingModes(windows: false).Select(option => option.Value));
    }

    [Fact]
    public void PostProcessingOnlyAppliesWithSoftwareDecoding()
    {
        Assert.Empty(Windows(PlayerSettings.Default with { PostProcessing = 4 }));
        Assert.Equal(["--video-filter=adjust:postproc", "--contrast=1.5", "--postproc-q=4", "--avcodec-hw=none"],
            Windows(PlayerSettings.Default with { PostProcessing = 4, HardwareDecoding = "Off", Contrast = 1.5 }));
    }

    [Fact]
    public void DecoderOptions()
    {
        Assert.Equal(["--avcodec-skiploopfilter=4", "--avcodec-threads=3", "--no-drop-late-frames"],
            Windows(PlayerSettings.Default with { SkipLoopFilter = 4, DecodingThreads = 3, DropLateFrames = false }));
    }

    [Theory]
    [InlineData("Lowest", 100, 100, 100)]
    [InlineData("Low", 300, 300, 300)]
    [InlineData("High", 2000, 2000, 2000)]
    [InlineData("Higher", 5000, 5000, 5000)]
    public void BufferingPresetsSetEverySource(string preset, int file, int network, int live)
    {
        Assert.Equal([$"--file-caching={file}", $"--network-caching={network}", $"--live-caching={live}", $"--disc-caching={file}"],
            Windows(PlayerSettings.Default with { Buffering = preset, FileCaching = 1, NetworkCaching = 2, LiveCaching = 3 }));
    }

    [Fact]
    public void CustomBufferingUsesTheFieldsAndNormalUsesVlcDefaults()
    {
        PlayerSettings custom = PlayerSettings.Default with { Buffering = "Custom", FileCaching = 1500, NetworkCaching = 4000, LiveCaching = 250 };

        Assert.Equal(["--file-caching=1500", "--network-caching=4000", "--live-caching=250"], Windows(custom));
        Assert.Empty(Windows(custom with { Buffering = "Normal" }));
        Assert.Equal((1000, 1000, 300), PlayerEngineOptions.Caching(PlayerSettings.Default));
    }

    [Fact]
    public void ProxyNeedsAScheme()
    {
        PlayerSettings settings = PlayerSettings.Default with { HttpProxy = "user@proxy.example:3128" };

        Assert.Equal(["--http-proxy=http://user@proxy.example:3128", "--http-proxy-pwd=secret"], Windows(settings, "secret"));
        Assert.Equal(["--http-proxy=https://proxy.example"], Windows(settings with { HttpProxy = "https://proxy.example" }));
        // A password alone is not a proxy, and VLC 3 reads the proxy only on Windows.
        Assert.Empty(Windows(PlayerSettings.Default, "secret"));
        Assert.Empty(PlayerEngineOptions.Arguments(settings, "secret", windows: false, macOS: true));
    }

    [Fact]
    public void Certificates()
    {
        Assert.Equal(["--no-gnutls-system-trust", "--gnutls-priorities=SECURE256"],
            Windows(PlayerSettings.Default with { VerifyCertificates = false, CipherPreference = "Secure256" }));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("EN", "en")]
    [InlineData(" fr, en;fr  de ", "fr,en,de")]
    [InlineData("english, fre, 12, e", "fre")]
    [InlineData("none", "none")]
    public void LanguagesAreTwoOrThreeLetterCodes(string value, string expected) =>
        Assert.Equal(expected, PlayerEngineOptions.NormalizeLanguages(value));

    [Fact]
    public void QueueModesRoundTrip()
    {
        foreach (SettingOption option in PlayerEngineOptions.RepeatModes)
            Assert.Equal(option.Value, QueueModeSettings.Setting(QueueModeSettings.Repeat(option.Value)));
        Assert.Equal(QueueOrder.Random, QueueModeSettings.Order(true));
        Assert.Equal(QueueRepeatMode.None, QueueModeSettings.Repeat("bogus"));
    }

    [Fact]
    public void EveryChoiceListHasItsDefault()
    {
        PlayerSettings d = PlayerSettings.Default;
        Assert.Contains(PlayerEngineOptions.RepeatModes, o => o.Value == d.QueueRepeat);
        Assert.Contains(PlayerEngineOptions.PlaybackFinishedActions, o => o.Value == d.PlaybackFinished);
        Assert.Contains(PlayerEngineOptions.AviRepairModes, o => o.Value == d.AviRepair);
        Assert.Contains(PlayerEngineOptions.DeinterlaceModes, o => o.Value == d.Deinterlace);
        Assert.Contains(PlayerEngineOptions.DeinterlaceMethods, o => o.Value == d.DeinterlaceMethod);
        Assert.Contains(PlayerEngineOptions.AspectRatios, o => o.Value == d.DefaultAspectRatio);
        Assert.Contains(PlayerEngineOptions.Crops, o => o.Value == d.DefaultCrop);
        Assert.Contains(PlayerEngineOptions.Zooms, o => o.Value == d.DefaultZoom);
        Assert.Contains(PlayerEngineOptions.BufferingPresets, o => o.Value == d.Buffering);
        Assert.Contains(PlayerEngineOptions.CipherPreferences, o => o.Value == d.CipherPreference);
        Assert.Equal("Balanced", PlayerEngineOptions.CipherPreferences.Single(o => o.Value == "Normal").Label);
    }
}

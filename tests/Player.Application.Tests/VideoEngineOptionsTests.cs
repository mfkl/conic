using Player.Application;
using Xunit;

namespace Player.Application.Tests;

/// <summary>Settings › Video as VLC 3's filters, overlays, crop and snapshot options.</summary>
public sealed class VideoEngineOptionsTests
{
    [Fact]
    public void DefaultsAddNoVideoOptions()
    {
        Assert.Empty(VideoEngineOptions.Arguments(PlayerSettings.Default));
        LiveVideoSettings live = VideoEngineOptions.Live(PlayerSettings.Default);
        Assert.Equal(("", "", 0, -1, "auto"), (live.VideoFilter, live.SubSource, live.Parameters.Count, live.Deinterlace, live.DeinterlaceMethod));
    }

    [Fact]
    public void ThePictureKeepsItsOptionsAsBefore() =>
        Assert.Equal(["--video-filter=adjust", "--hue=20", "--contrast=1.5"],
            VideoEngineOptions.Arguments(PlayerSettings.Default with { Hue = 20, Contrast = 1.5 }));

    [Fact]
    public void EffectsChainInVlcsOrderWithTheirParameters()
    {
        PlayerSettings settings = PlayerSettings.Default with
        {
            Contrast = 1.2, VideoTransform = "90", Sepia = true, SepiaIntensity = 80, Posterize = true, PosterizeLevel = 4,
            ColorExtract = true, ColorExtractColor = "#00FF00", Sharpen = true, Denoise = true, Anaglyph = true,
        };
        // The picture last: after CPU filters VLC uses its CPU adjust (see Filters).
        Assert.Equal(["transform", "sepia", "posterize", "extract", "sharpen", "hqdn3d", "anaglyph", "adjust"], VideoEngineOptions.Filters(settings));
        IReadOnlyList<string> arguments = VideoEngineOptions.Arguments(settings);
        Assert.Equal("--video-filter=transform:sepia:posterize:extract:sharpen:hqdn3d:anaglyph:adjust", arguments[0]);
        Assert.Contains("--transform-type=90", arguments);
        Assert.Contains("--sepia-intensity=80", arguments);
        Assert.Contains("--posterize-level=4", arguments);
        Assert.Contains("--extract-component=65280", arguments);
        Assert.Contains("--sharpen-sigma=0.05", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--hue=", StringComparison.Ordinal));
    }

    [Fact]
    public void OverlaysAreSubSources()
    {
        PlayerSettings settings = PlayerSettings.Default with { LogoFile = "C:/logo.png", LogoPosition = "9", LogoOpacity = 128, MarqueeText = "Hello", MarqueePosition = "4" };
        IReadOnlyList<string> arguments = VideoEngineOptions.Arguments(settings);
        Assert.Equal("--sub-source=logo:marq", arguments[0]);
        Assert.Contains("--logo-file=C:/logo.png", arguments);
        Assert.Contains("--logo-position=9", arguments);
        Assert.Contains("--logo-opacity=128", arguments);
        Assert.Contains("--marq-marquee=Hello", arguments);
        Assert.Contains("--marq-position=4", arguments);
        LiveVideoSettings live = VideoEngineOptions.Live(settings);
        Assert.Equal(("logo:marq", "Hello", 9L), (live.SubSource, live.Parameters["marq-marquee"], live.Parameters["logo-position"]));
    }

    [Fact]
    public void APixelCropReplacesTheCropRatio()
    {
        PlayerSettings settings = PlayerSettings.Default with { DefaultCrop = "16:9", CropLeft = 10, CropBottom = 20 };
        IReadOnlyList<string> arguments = PlayerEngineOptions.Arguments(settings, windows: true);
        Assert.Contains("--crop=10+0+0+20", arguments);
        Assert.DoesNotContain("--crop=16:9", arguments);
        Assert.Equal((10, 0, 0, 20), VideoEngineOptions.Live(settings).Crop);
        Assert.Contains("--crop=16:9", PlayerEngineOptions.Arguments(settings with { CropLeft = 0, CropBottom = 0 }, windows: true));
    }

    [Fact]
    public void SnapshotsGoWhereSettingsSay() =>
        Assert.Equal(["--snapshot-path=D:/Shots", "--snapshot-format=jpg"], VideoEngineOptions.Arguments(PlayerSettings.Default with { SnapshotFolder = "D:/Shots", SnapshotFormat = "jpg" }));

    [Fact]
    public void DeinterlacingIsLiveInVlcsValues()
    {
        LiveVideoSettings live = VideoEngineOptions.Live(PlayerSettings.Default with { Deinterlace = "On", DeinterlaceMethod = "yadif2x" });
        Assert.Equal((1, "yadif2x"), (live.Deinterlace, live.DeinterlaceMethod));
        Assert.Equal(0, VideoEngineOptions.Live(PlayerSettings.Default with { Deinterlace = "Off" }).Deinterlace);
    }

    [Fact]
    public void LiveSettingsCompareByContent()
    {
        PlayerSettings settings = PlayerSettings.Default with { Sepia = true, MarqueeText = "Hi" };
        Assert.Equal(VideoEngineOptions.Live(settings), VideoEngineOptions.Live(settings with { }));
        Assert.True(VideoEngineOptions.Live(settings).SameFilters(VideoEngineOptions.Live(settings with { CropTop = 4 })));
        Assert.False(VideoEngineOptions.Live(settings).SameFilters(VideoEngineOptions.Live(settings with { SepiaIntensity = 10 })));
    }

    [Fact]
    public void NormalizeKeepsValuesInVlcsRanges()
    {
        PlayerSettings value = VideoEngineOptions.Normalize(PlayerSettings.Default with
        {
            VideoTransform = "45", SepiaIntensity = 999, PosterizeLevel = 1, ColorExtractColor = "green", SharpenSigma = 9,
            LogoPosition = "77", LogoOpacity = -3, CropLeft = -1, CropRight = 99_999, SnapshotFormat = "gif",
        });
        Assert.Equal(("None", 255, 2, "#FF0000", 2d), (value.VideoTransform, value.SepiaIntensity, value.PosterizeLevel, value.ColorExtractColor, value.SharpenSigma));
        Assert.Equal(("6", 0, 0, VideoEngineOptions.MaximumCrop, "png"), (value.LogoPosition, value.LogoOpacity, value.CropLeft, value.CropRight, value.SnapshotFormat));
    }

    [Fact]
    public void CompressorAndRoomParametersBecomeVlcOptions()
    {
        Assert.Equal(["--audio-filter=compressor", "--compressor-ratio=8", "--compressor-makeup-gain=3"],
            AudioEngineOptions.Arguments(PlayerSettings.Default with { Compressor = true, CompressorRatio = 8, CompressorMakeupGain = 3 }, windows: true));
        Assert.Equal(["--audio-filter=spatializer", "--spatializer-roomsize=0.5"],
            AudioEngineOptions.Arguments(PlayerSettings.Default with { SpatialAudio = true, SpatialAudioMode = "Room", SpatializerRoomSize = .5 }, windows: true));
        // Off, their parameters stay out of the options.
        Assert.Empty(AudioEngineOptions.Arguments(PlayerSettings.Default with { CompressorRatio = 8 }, windows: true));
        LiveAudioSettings live = AudioEngineOptions.Live(PlayerSettings.Default with { CompressorThreshold = -20 }, windows: true);
        Assert.Equal(-20, live.CompressorThreshold);
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.SpatializerDamp)));
    }

    [Fact]
    public void XspfPlaylistsRoundTrip()
    {
        string xspf = PlaylistDocument.WriteXspf([@"C:\Media\A & B.mkv", "https://example.com/a.mp4?x=1&y=2"]);
        Assert.Contains("<location>file:///C:/Media/A%20&amp;%20B.mkv</location>", xspf);
        Assert.Equal([@"C:\Media\A & B.mkv", "https://example.com/a.mp4?x=1&y=2"], PlaylistDocument.Parse(xspf, "list.xspf"));
    }

    [Fact]
    public void XspfRefusesDocumentTypes() =>
        Assert.Throws<InvalidDataException>(() => PlaylistDocument.ParseXspf("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/x\">]><playlist><trackList><track><location>&e;</location></track></trackList></playlist>"));

    [Fact]
    public void PlsEntriesKeepTheirNumberedOrder() =>
        Assert.Equal(["b.mp3", "http://radio.example/stream"],
            PlaylistDocument.Parse("[playlist]\nFile2=http://radio.example/stream\nTitle2=Radio\nFile1=b.mp3\nNumberOfEntries=2\n", "radio.pls"));

    [Fact]
    public void M3uStaysTheDefault() =>
        Assert.StartsWith("#EXTM3U", PlaylistDocument.Write(["a.mp4"], "queue.m3u8"));
}

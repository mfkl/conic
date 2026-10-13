using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class AudioEngineOptionsTests
{
    private const string Device = "{0.0.0.00000000}.{2695a7c2-c4a3-4e73-9320-bfecba0ea5ff}";

    private static IReadOnlyList<string> Windows(PlayerSettings settings) => PlayerEngineOptions.Arguments(settings, windows: true, macOS: false);

    [Fact]
    public void OutputModuleDeviceAndPassthroughAreOptionsOfTheNextCore()
    {
        PlayerSettings settings = PlayerSettings.Default with { AudioOutputModule = "mmdevice", AudioDevice = Device, AudioPassthrough = true };

        Assert.Equal(["--aout=mmdevice", $"--mmdevice-audio-device={Device}", "--spdif"], Windows(settings));
        // The device is an MMDevice endpoint: the default output uses it, the others do not.
        Assert.Equal([$"--mmdevice-audio-device={Device}"], Windows(PlayerSettings.Default with { AudioDevice = Device }));
        Assert.Equal(["--aout=directsound"], Windows(PlayerSettings.Default with { AudioOutputModule = "directsound", AudioDevice = Device }));
        // Only Windows has a module and device choice; passthrough off is VLC's default.
        Assert.Empty(PlayerEngineOptions.Arguments(settings with { AudioPassthrough = false }, windows: false, macOS: true));
        Assert.Equal(["--no-audio"], Windows(PlayerSettings.Default with { AudioEnabled = false }));
    }

    [Fact]
    public void EffectsAreOneFilterChainWithTheirParameters()
    {
        PlayerSettings settings = PlayerSettings.Default with
        {
            Karaoke = true, PitchShift = -2.5, Equalizer = true, EqualizerBands = "8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2", EqualizerPreamp = 5,
            Compressor = true, SpatialAudio = true, NormalizationMaximumLevel = 1.5,
        };

        Assert.Equal([
            "--audio-filter=karaoke:scaletempo_pitch:equalizer:compressor:spatialaudio:normvol",
            "--equalizer-bands=8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2", "--equalizer-preamp=5", "--norm-max-level=1.5", "--pitch-shift=-2.5",
            "--spatialaudio-headphones"], Windows(settings));
        Assert.Equal(["--audio-filter=spatializer"], Windows(PlayerSettings.Default with { SpatialAudio = true, SpatialAudioMode = "Room" }));
        // An equalizer that would end the chain is followed by the neutral gain filter (16-bit outputs).
        Assert.Equal(["equalizer", "gain"], AudioEngineOptions.Filters(PlayerSettings.Default with { Equalizer = true }));
        Assert.Equal(["equalizer", "compressor"], AudioEngineOptions.Filters(PlayerSettings.Default with { Equalizer = true, Compressor = true }));
        // The equalizer's bands and preamp matter only while it is on.
        Assert.Empty(Windows(PlayerSettings.Default with { EqualizerBands = "1 2 3 4 5 6 7 8 9 10", EqualizerPreamp = 0 }));
    }

    [Fact]
    public void LoudnessDolbyResamplerAndDelay()
    {
        Assert.Equal(["--audio-replay-gain-mode=album", "--audio-replay-gain-preamp=6.5"], Windows(PlayerSettings.Default with { ReplayGain = "album", ReplayGainPreamp = 6.5 }));
        Assert.Empty(Windows(PlayerSettings.Default with { ReplayGainPreamp = 6.5 }));
        Assert.Equal(["--force-dolby-surround=1", "--src-converter-type=4", "--audio-desync=-250"],
            Windows(PlayerSettings.Default with { DolbySurround = "On", ResamplerQuality = "Fast", AudioDelay = -250 }));
        Assert.Equal(["--force-dolby-surround=2", "--src-converter-type=0"], Windows(PlayerSettings.Default with { DolbySurround = "Off", ResamplerQuality = "Best" }));
    }

    [Fact]
    public void FloatsUseTheInvariantCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            Assert.Equal(["--audio-filter=equalizer:gain", "--equalizer-bands=0.5 0 0 0 0 0 0 0 0 -1.5", "--equalizer-preamp=10.5"],
                Windows(PlayerSettings.Default with { Equalizer = true, EqualizerBands = "0.5 0 0 0 0 0 0 0 0 -1.5", EqualizerPreamp = 10.5 }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LiveSettingsAreTheOutputVariablesOfTheSameChoices()
    {
        Assert.Equal(new LiveAudioSettings("", "0 0 0 0 0 0 0 0 0 0", 12, AudioEngineOptions.DefaultNormalizationLevel, 0, false, "none", 0, ""),
            AudioEngineOptions.Live(PlayerSettings.Default, windows: true));
        PlayerSettings settings = PlayerSettings.Default with { Equalizer = true, Karaoke = true, NormalizationMaximumLevel = 4, ReplayGain = "track", AudioDevice = Device };
        LiveAudioSettings live = AudioEngineOptions.Live(settings, windows: true);
        Assert.Equal(("karaoke:equalizer:normvol", 4d, "track", Device), (live.Filters, live.NormalizationLevel, live.ReplayGainMode, live.Device));
        // No device to choose with another output module, or off Windows.
        Assert.Null(AudioEngineOptions.Live(settings with { AudioOutputModule = "waveout" }, windows: true).Device);
        Assert.Null(AudioEngineOptions.Live(settings, windows: false).Device);
    }

    [Fact]
    public void PresetsAreVlcsTenBandTables()
    {
        Assert.Equal(10, AudioEngineOptions.EqualizerFrequencies.Count);
        Assert.All(AudioEngineOptions.EqualizerPresets.Where(p => p.Value != AudioEngineOptions.CustomEqualizerPreset), preset => Assert.Equal(10, preset.Bands.Count));
        Assert.Equal(19, AudioEngineOptions.EqualizerPresets.Count);
        Assert.Equal("rock", AudioEngineOptions.PresetFor(5, "8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2"));
        Assert.Equal("flat", AudioEngineOptions.PresetFor(12, "0 0 0 0 0 0 0 0 0 0"));
        Assert.Equal(AudioEngineOptions.CustomEqualizerPreset, AudioEngineOptions.PresetFor(11, "0 0 0 0 0 0 0 0 0 0"));
    }

    [Theory]
    [InlineData("", "0 0 0 0 0 0 0 0 0 0")]
    [InlineData("1.25 x -30 25", "1.2 0 -20 20 0 0 0 0 0 0")]
    [InlineData("1,2;3 4 5 6 7 8 9 10 11 12", "1 2 3 4 5 6 7 8 9 10")]
    public void BandsAreTenGainsInRange(string value, string expected) => Assert.Equal(expected, AudioEngineOptions.NormalizeBands(value));

    [Fact]
    public void PresetsFollowTheBands()
    {
        PlayerSettings rock = AudioEngineOptions.Normalize(PlayerSettings.Default with { EqualizerPreset = "rock", EqualizerPreamp = 5, EqualizerBands = "8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2" });
        Assert.Equal("rock", rock.EqualizerPreset);
        // A stored preset whose bands were changed is Custom; an unknown one is found from its bands.
        Assert.Equal(AudioEngineOptions.CustomEqualizerPreset, AudioEngineOptions.Normalize(rock with { EqualizerBands = "0 0 0 0 0 0 0 0 0 1" }).EqualizerPreset);
        Assert.Equal("rock", AudioEngineOptions.Normalize(rock with { EqualizerPreset = "bogus" }).EqualizerPreset);
    }

    [Fact]
    public void EveryChoiceListHasItsDefault()
    {
        PlayerSettings d = PlayerSettings.Default;
        Assert.Contains(AudioEngineOptions.ReplayGainModes, o => o.Value == d.ReplayGain);
        Assert.Contains(AudioEngineOptions.SpatialAudioModes, o => o.Value == d.SpatialAudioMode);
        Assert.Contains(AudioEngineOptions.DolbySurroundModes, o => o.Value == d.DolbySurround);
        Assert.Contains(AudioEngineOptions.ResamplerQualities, o => o.Value == d.ResamplerQuality);
        Assert.Contains(AudioEngineOptions.EqualizerPresets, o => o.Value == d.EqualizerPreset);
        Assert.Equal(d, AudioEngineOptions.Normalize(d));
    }
}

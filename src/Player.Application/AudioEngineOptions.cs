using System.Globalization;

namespace Player.Application;

/// <summary>An equalizer preset as VLC 3 defines it (its equalizer module's
/// table, the same values libvlc's presets return): the preamp and the ten
/// bands, in dB.</summary>
public sealed record EqualizerPreset(string Value, string Label, double Preamp, IReadOnlyList<double> Bands)
{
    public override string ToString() => Label;
}

/// <summary>
/// The audio settings a playing core takes at once (VlcCorePlayer.LiveAudio),
/// as the values of its audio output's variables: the filter chain, the
/// equalizer, the filters' parameters, replay gain and the output device
/// (null when the output module has no device to choose).
/// </summary>
public sealed record LiveAudioSettings(
    string Filters,
    string EqualizerBands,
    double EqualizerPreamp,
    double NormalizationLevel,
    double PitchShift,
    bool SpatialHeadphones,
    string ReplayGainMode,
    double ReplayGainPreamp,
    string? Device);

/// <summary>
/// Settings › Audio as libVLC options (phase 9c). The effects are VLC 3 audio
/// filters; each new core starts with them (<see cref="Arguments"/>) and a
/// playing core takes them at once (<see cref="Live"/>). The choices the rows
/// offer live here, so a stored value always maps to something VLC 3.0.24
/// has.
/// </summary>
public static class AudioEngineOptions
{
    // The volume slider's range: VLC 3 amplifies up to 300 %.
    public const int MinimumMaximumVolume = 100, MaximumVolumeLimit = 300;
    public const int MaximumVolumeStep = 25;
    // normvol's default maximum level and range.
    public const double DefaultNormalizationLevel = 2, MinimumNormalizationLevel = .5, MaximumNormalizationLevel = 10;
    // The equalizer's preamp leaves the level unchanged at 12 dB (VLC's
    // default): the filter attenuates its input by that much.
    public const double NeutralEqualizerPreamp = 12, MaximumEqualizerGain = 20;
    public const double MaximumPitchShift = 12, MaximumReplayGainPreamp = 20;
    public const string CustomEqualizerPreset = "custom";

    /// <summary>The bands of VLC's equalizer (equalizer-vlcfreqs, its default).</summary>
    public static IReadOnlyList<string> EqualizerFrequencies { get; } = ["60 Hz", "170 Hz", "310 Hz", "600 Hz", "1 kHz", "3 kHz", "6 kHz", "12 kHz", "14 kHz", "16 kHz"];

    public static IReadOnlyList<EqualizerPreset> EqualizerPresets { get; } =
    [
        new("flat", "Flat", 12, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        new("classical", "Classical", 12, [0, 0, 0, 0, 0, 0, -7.2, -7.2, -7.2, -9.6]),
        new("club", "Club", 6, [0, 0, 8, 5.6, 5.6, 5.6, 3.2, 0, 0, 0]),
        new("dance", "Dance", 5, [9.6, 7.2, 2.4, 0, 0, -5.6, -7.2, -7.2, 0, 0]),
        new("fullbass", "Full bass", 5, [-8, 9.6, 9.6, 5.6, 1.6, -4, -8, -10.4, -11.2, -11.2]),
        new("fullbasstreble", "Full bass and treble", 4, [7.2, 5.6, 0, -7.2, -4.8, 1.6, 8, 11.2, 12, 12]),
        new("fulltreble", "Full treble", 3, [-9.6, -9.6, -9.6, -4, 2.4, 11.2, 16, 16, 16, 16.8]),
        new("headphones", "Headphones", 4, [4.8, 11.2, 5.6, -3.2, -2.4, 1.6, 4.8, 9.6, 12.8, 14.4]),
        new("largehall", "Large hall", 5, [10.4, 10.4, 5.6, 5.6, 0, -4.8, -4.8, -4.8, 0, 0]),
        new("live", "Live", 7, [-4.8, 0, 4, 5.6, 5.6, 5.6, 4, 2.4, 2.4, 2.4]),
        new("party", "Party", 6, [7.2, 7.2, 0, 0, 0, 0, 0, 0, 7.2, 7.2]),
        new("pop", "Pop", 6, [-1.6, 4.8, 7.2, 8, 5.6, 0, -2.4, -2.4, -1.6, -1.6]),
        new("reggae", "Reggae", 8, [0, 0, 0, -5.6, 0, 6.4, 6.4, 0, 0, 0]),
        new("rock", "Rock", 5, [8, 4.8, -5.6, -8, -3.2, 4, 8.8, 11.2, 11.2, 11.2]),
        new("ska", "Ska", 6, [-2.4, -4.8, -4, 0, 4, 5.6, 8.8, 9.6, 11.2, 9.6]),
        new("soft", "Soft", 5, [4.8, 1.6, 0, -2.4, 0, 4, 8, 9.6, 11.2, 12]),
        new("softrock", "Soft rock", 7, [4, 4, 2.4, 0, -4, -5.6, -3.2, 0, 2.4, 8.8]),
        new("techno", "Techno", 5, [8, 5.6, 0, -5.6, -4.8, 0, 8, 9.6, 9.6, 8.8]),
        new(CustomEqualizerPreset, "Custom", NeutralEqualizerPreamp, []),
    ];

    public static IReadOnlyList<SettingOption> ReplayGainModes { get; } = [new("none", "Off"), new("track", "Track"), new("album", "Album")];
    // Headphones: VLC's binaural renderer (spatialaudio, with its HRTF);
    // Room: its spatializer reverb.
    public static IReadOnlyList<SettingOption> SpatialAudioModes { get; } = [new("Headphones", "Headphone virtualization"), new("Room", "Room")];
    // --force-dolby-surround: 0 detects the Dolby Surround flag, 1 forces it, 2 ignores it.
    public static IReadOnlyList<SettingOption> DolbySurroundModes { get; } = [new("Automatic", "Automatic"), new("On", "On"), new("Off", "Off")];
    // libsamplerate's converters (--src-converter-type): linear, the fastest
    // sinc (VLC's default) and the best sinc.
    public static IReadOnlyList<SettingOption> ResamplerQualities { get; } = [new("Fast", "Fast"), new("Balanced", "Balanced"), new("Best", "Best")];

    /// <summary>The audio settings a new core starts with; they apply at the next open.</summary>
    public static IReadOnlySet<string> NextOpenKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.AudioEnabled), nameof(PlayerSettings.AudioOutputModule), nameof(PlayerSettings.AudioPassthrough),
        nameof(PlayerSettings.DolbySurround), nameof(PlayerSettings.ResamplerQuality), nameof(PlayerSettings.AudioDelay),
    };

    /// <summary>The audio settings a playing core also takes at once.</summary>
    public static IReadOnlySet<string> LiveKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.AudioDevice), nameof(PlayerSettings.NormalizationMaximumLevel), nameof(PlayerSettings.ReplayGain),
        nameof(PlayerSettings.ReplayGainPreamp), nameof(PlayerSettings.Equalizer), nameof(PlayerSettings.EqualizerPreset),
        nameof(PlayerSettings.EqualizerPreamp), nameof(PlayerSettings.EqualizerBands),
        nameof(PlayerSettings.SpatialAudio), nameof(PlayerSettings.SpatialAudioMode), nameof(PlayerSettings.Compressor),
        nameof(PlayerSettings.PitchShift), nameof(PlayerSettings.Karaoke),
    };

    /// <summary>The device belongs to the MMDevice output (VLC's default on Windows).</summary>
    public static bool UsesDevice(PlayerSettings settings, bool? windows = null) =>
        (windows ?? OperatingSystem.IsWindows()) && AudioOutputPolicy.Effective(settings.AudioOutputModule, windows) is "Default" or "mmdevice";

    /// <summary>The audio filters, in the order VLC chains them.</summary>
    public static IReadOnlyList<string> Filters(PlayerSettings settings)
    {
        List<string> filters = [];
        if (settings.Karaoke) filters.Add("karaoke");
        if (settings.PitchShift != 0) filters.Add("scaletempo_pitch");
        if (settings.Equalizer) filters.Add("equalizer");
        if (settings.Compressor) filters.Add("compressor");
        if (settings.SpatialAudio) filters.Add(settings.SpatialAudioMode == "Room" ? "spatializer" : "spatialaudio");
        if (settings.NormalizationMaximumLevel is not null) filters.Add("normvol");
        // VLC 3 plays 16-bit sources (WAV, FLAC) through a 16-bit output,
        // and the equalizer refuses to end a chain that has to produce it;
        // the gain filter at its neutral 1.0 ends the chain instead.
        if (filters.Count > 0 && filters[^1] == "equalizer") filters.Add("gain");
        return filters;
    }

    /// <summary>The VLC options for Settings › Audio; none for VLC's defaults.</summary>
    public static IReadOnlyList<string> Arguments(PlayerSettings settings, bool? windows = null)
    {
        bool isWindows = windows ?? OperatingSystem.IsWindows();
        List<string> arguments = [];
        if (!settings.AudioEnabled) arguments.Add("--no-audio");
        string output = AudioOutputPolicy.Effective(settings.AudioOutputModule, isWindows);
        if (output != "Default") arguments.Add("--aout=" + output);
        if (UsesDevice(settings, isWindows) && settings.AudioDevice.Length > 0) arguments.Add("--mmdevice-audio-device=" + settings.AudioDevice);
        if (settings.AudioPassthrough == true) arguments.Add("--spdif");
        if (Filters(settings) is { Count: > 0 } filters) arguments.Add("--audio-filter=" + string.Join(':', filters));
        if (settings.Equalizer)
        {
            arguments.Add("--equalizer-bands=" + settings.EqualizerBands);
            arguments.Add("--equalizer-preamp=" + Format(settings.EqualizerPreamp));
        }
        if (settings.NormalizationMaximumLevel is double level) arguments.Add("--norm-max-level=" + Format(level));
        if (settings.PitchShift != 0) arguments.Add("--pitch-shift=" + Format(settings.PitchShift));
        if (settings.SpatialAudio && settings.SpatialAudioMode != "Room") arguments.Add("--spatialaudio-headphones");
        if (settings.ReplayGain != "none")
        {
            arguments.Add("--audio-replay-gain-mode=" + settings.ReplayGain);
            if (settings.ReplayGainPreamp != 0) arguments.Add("--audio-replay-gain-preamp=" + Format(settings.ReplayGainPreamp));
        }
        if (settings.DolbySurround == "On") arguments.Add("--force-dolby-surround=1");
        else if (settings.DolbySurround == "Off") arguments.Add("--force-dolby-surround=2");
        if (settings.ResamplerQuality == "Fast") arguments.Add("--src-converter-type=4");
        else if (settings.ResamplerQuality == "Best") arguments.Add("--src-converter-type=0");
        // The input's audio delay starts at this value (input_ControlVarInit).
        if (settings.AudioDelay != 0) arguments.Add("--audio-desync=" + settings.AudioDelay.ToString(CultureInfo.InvariantCulture));
        return arguments.AsReadOnly();
    }

    /// <summary>The audio output variables for <paramref name="settings"/>, for a playing core.</summary>
    public static LiveAudioSettings Live(PlayerSettings settings, bool? windows = null) => new(
        string.Join(':', Filters(settings)),
        settings.EqualizerBands,
        settings.EqualizerPreamp,
        settings.NormalizationMaximumLevel ?? DefaultNormalizationLevel,
        settings.PitchShift,
        settings.SpatialAudio && settings.SpatialAudioMode != "Room",
        settings.ReplayGain,
        settings.ReplayGainPreamp,
        UsesDevice(settings, windows) ? settings.AudioDevice : null);

    /// <summary>The preset whose preamp and bands these are, or Custom.</summary>
    public static string PresetFor(double preamp, string bands) =>
        EqualizerPresets.FirstOrDefault(preset => preset.Value != CustomEqualizerPreset && preset.Preamp == preamp && FormatBands(preset.Bands) == bands)?.Value
        ?? CustomEqualizerPreset;

    /// <summary>"1.25 x  -30" -> "1.3 0 -20 0 0 0 0 0 0 0": ten gains in dB, one decimal, in range.</summary>
    public static string NormalizeBands(string? value)
    {
        double[] bands = new double[EqualizerFrequencies.Count];
        string[] parts = (value ?? string.Empty).Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < bands.Length && index < parts.Length; index++)
            if (double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double gain))
                bands[index] = Range(gain, -MaximumEqualizerGain, MaximumEqualizerGain, 0, 1);
        return FormatBands(bands);
    }

    public static IReadOnlyList<double> ParseBands(string bands) => NormalizeBands(bands).Split(' ')
        .Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();

    public static string FormatBands(IEnumerable<double> bands) => string.Join(' ', bands.Select(Format));

    /// <summary>Puts the audio fields in their ranges and choices.</summary>
    public static PlayerSettings Normalize(PlayerSettings value)
    {
        int maximum = Math.Clamp(value.MaximumVolume, MinimumMaximumVolume, MaximumVolumeLimit);
        string bands = NormalizeBands(value.EqualizerBands);
        double preamp = Range(value.EqualizerPreamp, -MaximumEqualizerGain, MaximumEqualizerGain, NeutralEqualizerPreamp, 1);
        string preset = EqualizerPresets.Any(option => option.Value == value.EqualizerPreset) ? value.EqualizerPreset : PresetFor(preamp, bands);
        // A preset other than Custom names the bands it holds.
        if (preset != CustomEqualizerPreset && PresetFor(preamp, bands) != preset) preset = CustomEqualizerPreset;
        return value with
        {
            AudioDevice = (value.AudioDevice ?? string.Empty).Trim(),
            MaximumVolume = maximum,
            StartupVolume = Math.Clamp(value.StartupVolume, 0, MaximumVolumeLimit),
            VolumeStep = Math.Clamp(value.VolumeStep, 1, MaximumVolumeStep),
            NormalizationMaximumLevel = value.NormalizationMaximumLevel is double level && double.IsFinite(level)
                ? Math.Clamp(level, MinimumNormalizationLevel, MaximumNormalizationLevel) : null,
            ReplayGain = Choice(value.ReplayGain, ReplayGainModes, "none"),
            ReplayGainPreamp = Range(value.ReplayGainPreamp, -MaximumReplayGainPreamp, MaximumReplayGainPreamp, 0, 1),
            EqualizerPreset = preset,
            EqualizerPreamp = preamp,
            EqualizerBands = bands,
            SpatialAudioMode = Choice(value.SpatialAudioMode, SpatialAudioModes, "Headphones"),
            PitchShift = Range(value.PitchShift, -MaximumPitchShift, MaximumPitchShift, 0, 1),
            DolbySurround = Choice(value.DolbySurround, DolbySurroundModes, "Automatic"),
            ResamplerQuality = Choice(value.ResamplerQuality, ResamplerQualities, "Balanced"),
            AudioDelay = Math.Clamp(value.AudioDelay, -MediaDelayPolicy.MaximumMilliseconds, MediaDelayPolicy.MaximumMilliseconds),
        };
    }

    private static string Choice(string? value, IReadOnlyList<SettingOption> options, string fallback) =>
        options.Any(option => option.Value == value) ? value! : fallback;

    private static double Range(double value, double minimum, double maximum, double neutral, int decimals) =>
        double.IsFinite(value) ? Math.Round(Math.Clamp(value, minimum, maximum), decimals) : neutral;

    private static string Format(double value) => (value == 0 ? 0 : value).ToString("0.###", CultureInfo.InvariantCulture);
}

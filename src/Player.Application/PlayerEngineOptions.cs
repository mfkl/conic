using System.Globalization;

namespace Player.Application;

/// <summary>A choice of a Settings dropdown: the value stored in the settings and the label shown.</summary>
public sealed record SettingOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The one place that turns <see cref="PlayerSettings"/> into libVLC options.
/// Every open starts a new VLC core (VlcCorePlayer), so these options are
/// that core's startup arguments: a change applies from the next open on,
/// to every item of that queue. Only values that differ from VLC's defaults
/// produce an option. The choices the Settings rows offer live here too, so
/// a stored value always maps to an option VLC 3 understands.
/// </summary>
public static class PlayerEngineOptions
{
    // VLC 3's defaults (src/libvlc-module.c).
    public const int DefaultFileCaching = 1000, DefaultNetworkCaching = 1000;
    public const int MaximumCaching = 60000;
    public const int MaximumDecodingThreads = 16;

    /// <summary>The settings this class maps to options; they apply at the next open.</summary>
    public static IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.AutoPlay), nameof(PlayerSettings.AudioLanguage), nameof(PlayerSettings.SubtitleLanguage),
        nameof(PlayerSettings.StopAfterEachItem), nameof(PlayerSettings.SeekPrecisely), nameof(PlayerSettings.AviRepair),
        nameof(PlayerSettings.VideoEnabled), nameof(PlayerSettings.Hue), nameof(PlayerSettings.Brightness),
        nameof(PlayerSettings.Contrast), nameof(PlayerSettings.Saturation), nameof(PlayerSettings.Gamma),
        nameof(PlayerSettings.Deinterlace), nameof(PlayerSettings.DeinterlaceMethod), nameof(PlayerSettings.DefaultAspectRatio),
        nameof(PlayerSettings.DefaultCrop), nameof(PlayerSettings.DefaultZoom), nameof(PlayerSettings.HardwareDecoding),
        nameof(PlayerSettings.SkipLoopFilter), nameof(PlayerSettings.DecodingThreads),
        nameof(PlayerSettings.DropLateFrames), nameof(PlayerSettings.Buffering), nameof(PlayerSettings.FileCaching),
        nameof(PlayerSettings.NetworkCaching), nameof(PlayerSettings.HttpProxy),
        nameof(PlayerSettings.HttpProxyPasswordProtected), nameof(PlayerSettings.VerifyCertificates), nameof(PlayerSettings.CipherPreference),
    };

    public static IReadOnlyList<SettingOption> ResumeModes { get; } = [new("Ask", "Ask"), new("Always", "Always"), new("Never", "Never")];
    public static IReadOnlyList<SettingOption> RepeatModes { get; } = [new("Off", "Off"), new("One", "Current item"), new("All", "All items")];
    public static IReadOnlyList<SettingOption> PlaybackFinishedActions { get; } = [new("Stop", "Stop"), new("Home", "Go to Home"), new("Quit", "Quit VLC")];
    // VLC's "Ask" needs a dialog the app does not provide; without one VLC
    // plays the file as is, which is what Never does.
    public static IReadOnlyList<SettingOption> AviRepairModes { get; } = [new("Never", "Never"), new("WhenNeeded", "When the index is broken"), new("Always", "Always")];
    public static IReadOnlyList<SettingOption> DeinterlaceModes { get; } = [new("Automatic", "Automatic"), new("On", "On"), new("Off", "Off")];
    // The modes both VLC's software deinterlacer and its Direct3D 11 one
    // (used with hardware decoding) implement; D3D11 turns any other into Blend.
    public static IReadOnlyList<SettingOption> DeinterlaceMethods { get; } =
        [new("Automatic", "Automatic"), new("blend", "Blend"), new("bob", "Bob"), new("x", "X"), new("yadif2x", "Yadif (2x)"), new("ivtc", "Film NTSC (IVTC)")];
    // VLC 3's own lists (src/video_output/vout_intf.c).
    public static IReadOnlyList<SettingOption> AspectRatios { get; } =
        [new("", "Automatic"), new("16:9", "16:9"), new("4:3", "4:3"), new("1:1", "1:1"), new("16:10", "16:10"), new("221:100", "2.21:1"), new("235:100", "2.35:1"), new("239:100", "2.39:1"), new("5:4", "5:4")];
    public static IReadOnlyList<SettingOption> Crops { get; } =
        [new("", "None"), new("16:10", "16:10"), new("16:9", "16:9"), new("4:3", "4:3"), new("185:100", "1.85:1"), new("221:100", "2.21:1"), new("235:100", "2.35:1"), new("239:100", "2.39:1"), new("5:3", "5:3"), new("5:4", "5:4"), new("1:1", "1:1")];
    public static IReadOnlyList<SettingOption> Zooms { get; } =
        [new("fit", "Fit to window"), new("0.25", "1:4 Quarter"), new("0.5", "1:2 Half"), new("1", "1:1 Original"), new("2", "2:1 Double")];
    public static IReadOnlyList<SettingOption> LoopFilterModes { get; } =
        [new("0", "Never"), new("1", "Non-reference frames"), new("2", "Bidirectional frames"), new("3", "Non-key frames"), new("4", "All frames")];
    // VLC's Qt preferences use these values for its caching policies.
    public static IReadOnlyList<SettingOption> BufferingPresets { get; } =
        [new("Lowest", "Lowest latency"), new("Low", "Low latency"), new("Normal", "Normal"), new("High", "High latency"), new("Higher", "Higher latency"), new("Custom", "Custom")];
    // GnuTLS priority strings (modules/misc/gnutls.c).
    public static IReadOnlyList<SettingOption> CipherPreferences { get; } =
        [new("Performance", "Performance"), new("Normal", "Balanced"), new("Secure128", "Secure (128-bit)"), new("Secure256", "Secure (256-bit)")];

    /// <summary>Hardware decoding choices: Direct3D 11 exists only on Windows.
    /// DXVA2 is left out: its D3D9 surfaces cannot reach the app's D3D11
    /// output without a copy through system memory.</summary>
    public static IReadOnlyList<SettingOption> HardwareDecodingModes(bool? windows = null) => (windows ?? OperatingSystem.IsWindows())
        ? [new("Automatic", "Automatic"), new("Direct3D11", "Direct3D 11"), new("Off", "Off")]
        : [new("Automatic", "Automatic"), new("Off", "Off")];

    /// <summary>The caching (local files, network streams) a Buffering choice uses, in milliseconds.</summary>
    public static (int File, int Network) Caching(PlayerSettings settings) => settings.Buffering switch
    {
        "Lowest" => (100, 100),
        "Low" => (300, 300),
        "High" => (2000, 2000),
        "Higher" => (5000, 5000),
        "Custom" => (settings.FileCaching, settings.NetworkCaching),
        _ => (DefaultFileCaching, DefaultNetworkCaching),
    };

    /// <summary>
    /// The VLC options for <paramref name="settings"/>, in a stable order.
    /// <paramref name="proxyPassword"/> is the HTTP proxy password, already
    /// unprotected. The options go to libVLC in process (not a command line).
    /// </summary>
    public static IReadOnlyList<string> Arguments(PlayerSettings settings, string? proxyPassword = null, bool? windows = null, bool? macOS = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool isWindows = windows ?? OperatingSystem.IsWindows();
        bool isMacOS = macOS ?? OperatingSystem.IsMacOS();
        List<string> arguments = [];

        // Playback.
        if (!settings.AutoPlay) arguments.Add("--start-paused");
        if (NormalizeLanguages(settings.AudioLanguage) is { Length: > 0 } audio) arguments.Add($"--audio-language={audio}");
        if (NormalizeLanguages(settings.SubtitleLanguage) is { Length: > 0 } subtitles) arguments.Add($"--sub-language={subtitles}");
        if (settings.StopAfterEachItem) arguments.Add("--play-and-stop");
        if (!settings.SeekPrecisely) arguments.Add("--input-fast-seek");
        if (settings.AviRepair == "WhenNeeded") arguments.Add("--avi-index=3");
        else if (settings.AviRepair == "Always") arguments.Add("--avi-index=1");

        // Video.
        if (!settings.VideoEnabled) arguments.Add("--no-video");
        List<string> filters = [];
        List<string> filterOptions = [];
        if (!IsNeutralPicture(settings))
        {
            filters.Add("adjust");
            if (settings.Hue != 0) filterOptions.Add($"--hue={Format(settings.Hue)}");
            if (settings.Brightness != 1) filterOptions.Add($"--brightness={Format(settings.Brightness)}");
            if (settings.Contrast != 1) filterOptions.Add($"--contrast={Format(settings.Contrast)}");
            if (settings.Saturation != 1) filterOptions.Add($"--saturation={Format(settings.Saturation)}");
            if (settings.Gamma != 1) filterOptions.Add($"--gamma={Format(settings.Gamma)}");
        }
        if (filters.Count > 0) arguments.Add($"--video-filter={string.Join(':', filters)}");
        arguments.AddRange(filterOptions);
        if (settings.Deinterlace == "On") arguments.Add("--deinterlace=1");
        else if (settings.Deinterlace == "Off") arguments.Add("--deinterlace=0");
        if (settings.DeinterlaceMethod != "Automatic") arguments.Add($"--deinterlace-mode={settings.DeinterlaceMethod}");
        if (settings.DefaultAspectRatio.Length > 0) arguments.Add($"--aspect-ratio={settings.DefaultAspectRatio}");
        if (settings.DefaultCrop.Length > 0) arguments.Add($"--crop={settings.DefaultCrop}");
        if (settings.DefaultZoom != "fit") { arguments.Add("--no-autoscale"); arguments.Add($"--zoom={settings.DefaultZoom}"); }

        // Performance. The core already starts with --avcodec-hw=any; a later
        // occurrence of an option replaces an earlier one.
        if (settings.HardwareDecoding == "Direct3D11" && isWindows) arguments.Add("--avcodec-hw=d3d11va");
        else if (settings.HardwareDecoding == "Off")
        {
            arguments.Add("--avcodec-hw=none");
            // VideoToolbox is a separate decoder module on macOS, ahead of avcodec.
            if (isMacOS) arguments.Add("--no-videotoolbox");
        }
        if (settings.SkipLoopFilter > 0) arguments.Add($"--avcodec-skiploopfilter={settings.SkipLoopFilter.ToString(CultureInfo.InvariantCulture)}");
        if (settings.DecodingThreads > 0) arguments.Add($"--avcodec-threads={settings.DecodingThreads.ToString(CultureInfo.InvariantCulture)}");
        if (!settings.DropLateFrames) arguments.Add("--no-drop-late-frames");

        // Network.
        if (settings.Buffering != "Normal")
        {
            (int file, int network) = Caching(settings);
            arguments.Add($"--file-caching={file.ToString(CultureInfo.InvariantCulture)}");
            arguments.Add($"--network-caching={network.ToString(CultureInfo.InvariantCulture)}");
            // The presets set live capture and discs too, as VLC's own
            // policies do; Custom leaves them at VLC's defaults.
            if (settings.Buffering != "Custom")
            {
                arguments.Add($"--live-caching={file.ToString(CultureInfo.InvariantCulture)}");
                arguments.Add($"--disc-caching={file.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        // VLC 3 reads the HTTP proxy options only on Windows.
        if (isWindows && ProxyUrl(settings.HttpProxy) is { } proxy)
        {
            arguments.Add($"--http-proxy={proxy}");
            if (!string.IsNullOrEmpty(proxyPassword)) arguments.Add($"--http-proxy-pwd={proxyPassword}");
        }
        if (!settings.VerifyCertificates) arguments.Add("--no-gnutls-system-trust");
        if (settings.CipherPreference != "Normal") arguments.Add($"--gnutls-priorities={settings.CipherPreference.ToUpperInvariant()}");
        return arguments.AsReadOnly();
    }

    public static bool IsNeutralPicture(PlayerSettings settings) =>
        settings.Hue == 0 && settings.Brightness == 1 && settings.Contrast == 1 && settings.Saturation == 1 && settings.Gamma == 1;

    /// <summary>The proxy as the URL VLC needs: <c>host:port</c> gains <c>http://</c>
    /// (VLC rejects a proxy without a scheme). Null when empty.</summary>
    public static string? ProxyUrl(string? proxy)
    {
        string value = (proxy ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        return value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
    }

    /// <summary>"en, FR;de" -> "en,fr,de": two- or three-letter codes (or
    /// "none"), lower case, no repeats, at most eight.</summary>
    public static string NormalizeLanguages(string? value) => string.Join(',',
        (value ?? string.Empty).Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.ToLowerInvariant())
            .Where(code => code == "none" || (code is { Length: 2 or 3 } && code.All(char.IsAsciiLetterLower)))
            .Distinct(StringComparer.Ordinal)
            .Take(8));

    /// <summary>Puts the version 7 fields in their ranges and choices.</summary>
    public static PlayerSettings Normalize(PlayerSettings value) => value with
    {
        AudioLanguage = NormalizeLanguages(value.AudioLanguage),
        SubtitleLanguage = NormalizeLanguages(value.SubtitleLanguage),
        QueueRepeat = Choice(value.QueueRepeat, RepeatModes, "Off"),
        PlaybackFinished = Choice(value.PlaybackFinished, PlaybackFinishedActions, "Stop"),
        AviRepair = Choice(value.AviRepair, AviRepairModes, "Never"),
        Hue = Range(value.Hue, -180, 180, 0),
        Brightness = Range(value.Brightness, 0, 2, 1),
        Contrast = Range(value.Contrast, 0, 2, 1),
        Saturation = Range(value.Saturation, 0, 3, 1),
        Gamma = Range(value.Gamma, .01, 10, 1),
        Deinterlace = Choice(value.Deinterlace, DeinterlaceModes, "Automatic"),
        DeinterlaceMethod = Choice(value.DeinterlaceMethod, DeinterlaceMethods, "Automatic"),
        DefaultAspectRatio = Choice(value.DefaultAspectRatio, AspectRatios, ""),
        DefaultCrop = Choice(value.DefaultCrop, Crops, ""),
        DefaultZoom = Choice(value.DefaultZoom, Zooms, "fit"),
        HardwareDecoding = Choice(value.HardwareDecoding, HardwareDecodingModes(), "Automatic"),
        SkipLoopFilter = Math.Clamp(value.SkipLoopFilter, 0, 4),
        DecodingThreads = Math.Clamp(value.DecodingThreads, 0, MaximumDecodingThreads),
        Buffering = Choice(value.Buffering, BufferingPresets, "Normal"),
        FileCaching = Math.Clamp(value.FileCaching, 0, MaximumCaching),
        NetworkCaching = Math.Clamp(value.NetworkCaching, 0, MaximumCaching),
        HttpProxy = (value.HttpProxy ?? string.Empty).Trim(),
        HttpProxyPasswordProtected = string.IsNullOrEmpty(value.HttpProxyPasswordProtected) ? null : value.HttpProxyPasswordProtected,
        CipherPreference = Choice(value.CipherPreference, CipherPreferences, "Normal"),
    };

    private static string Choice(string? value, IReadOnlyList<SettingOption> options, string fallback) =>
        options.Any(option => option.Value == value) ? value! : fallback;

    // Two decimals are all a slider shows; NaN and infinities take the neutral value.
    private static double Range(double value, double minimum, double maximum, double neutral) =>
        double.IsFinite(value) ? Math.Round(Math.Clamp(value, minimum, maximum), 2) : neutral;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Maps the stored repeat and shuffle choices to the queue's modes and back.</summary>
public static class QueueModeSettings
{
    public static QueueRepeatMode Repeat(string? value) => value switch { "One" => QueueRepeatMode.CurrentItem, "All" => QueueRepeatMode.All, _ => QueueRepeatMode.None };
    public static string Setting(QueueRepeatMode mode) => mode switch { QueueRepeatMode.CurrentItem => "One", QueueRepeatMode.All => "All", _ => "Off" };
    public static QueueOrder Order(bool shuffle) => shuffle ? QueueOrder.Random : QueueOrder.Normal;
}

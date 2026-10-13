using System.Globalization;

namespace Player.Application;

/// <summary>
/// The subtitle style a playing core takes at once (VlcCorePlayer.LiveSubtitles):
/// values of variables VLC's text renderer reads each time it lays out a line
/// (sub-text-scale, freetype-color, freetype-outline-thickness and the
/// background's opacity and color). Colors are 0xRRGGBB, opacity 0 to 255.
/// </summary>
public sealed record LiveSubtitleSettings(
    int TextScale,
    int Color,
    int OutlineThickness,
    int BackgroundOpacity,
    int BackgroundColor);

/// <summary>
/// Settings › Subtitles as libVLC options (phase 9d). VLC renders the
/// subtitles itself (its freetype text renderer draws each line into the
/// subpicture the app's video output composes, SubtitleOverlay), so the
/// style is VLC's. Each new core starts with <see cref="Arguments"/>; the
/// <see cref="LiveKeys"/> also reach a playing core, from its next line.
/// The choices the rows offer live here, so a stored value always maps to
/// something VLC 3.0.24 has.
/// </summary>
public static class SubtitleEngineOptions
{
    // sub-autodetect-fuzzy: 3 (VLC's default) matches the title with extra characters.
    public const int DefaultMatching = 3;
    // sub-text-scale is 10 to 500 in VLC; the row offers the useful part.
    public const int MinimumTextScale = 50, MaximumTextScale = 300;
    public const int MaximumMargin = 1000;
    public const int MinimumFileNameSeconds = 1, MaximumFileNameSeconds = 10;
    public const string DefaultColor = "#FFFFFF", DefaultOutlineColor = "#000000";

    /// <summary>VLC's subtitle decoder encodings (modules/codec/subsdec.c), the
    /// common ones; Automatic tries UTF-8 and then the system's code page.</summary>
    public static IReadOnlyList<SettingOption> Encodings { get; } =
    [
        new("", "Automatic"), new("UTF-8", "Universal (UTF-8)"), new("UTF-16", "Universal (UTF-16)"),
        new("Windows-1252", "Western European (Windows-1252)"), new("ISO-8859-15", "Western European (Latin-9)"),
        new("Windows-1250", "Eastern European (Windows-1250)"), new("ISO-8859-2", "Eastern European (Latin-2)"),
        new("Windows-1251", "Cyrillic (Windows-1251)"), new("KOI8-R", "Russian (KOI8-R)"), new("KOI8-U", "Ukrainian (KOI8-U)"),
        new("Windows-1253", "Greek (Windows-1253)"), new("Windows-1254", "Turkish (Windows-1254)"),
        new("Windows-1255", "Hebrew (Windows-1255)"), new("Windows-1256", "Arabic (Windows-1256)"),
        new("Windows-1257", "Baltic (Windows-1257)"), new("Windows-1258", "Vietnamese (Windows-1258)"),
        new("Windows-874", "Thai (Windows-874)"), new("GB18030", "Simplified Chinese (GB18030)"),
        new("Big5", "Traditional Chinese (Big5)"), new("Shift_JIS", "Japanese (Shift JIS)"), new("EUC-JP", "Japanese (EUC-JP)"),
        new("CP949", "Korean (CP949)"),
    ];

    // --sub-autodetect-fuzzy 1 to 4 (0 is the same as autoloading off).
    public static IReadOnlyList<SettingOption> Matchings { get; } =
        [new("1", "Any subtitle file"), new("2", "Any file containing the title"), new("3", "Title with extra characters"), new("4", "Exact title")];
    // VLC's outline thickness list (freetype.c).
    public static IReadOnlyList<SettingOption> Outlines { get; } = [new("None", "None"), new("Thin", "Thin"), new("Normal", "Normal"), new("Thick", "Thick")];
    // Small is VLC's default shadow distance (0.06 of the font height).
    public static IReadOnlyList<SettingOption> ShadowDistances { get; } = [new("Small", "Small"), new("Medium", "Medium"), new("Large", "Large")];

    /// <summary>The subtitle settings a new core starts with; they apply at the next open.</summary>
    public static IReadOnlySet<string> NextOpenKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.SubtitlesEnabled), nameof(PlayerSettings.SubtitleEncoding), nameof(PlayerSettings.SubtitleAutoload),
        nameof(PlayerSettings.SubtitleMatching), nameof(PlayerSettings.SubtitleFont), nameof(PlayerSettings.SubtitleBold),
        nameof(PlayerSettings.SubtitleOpacity), nameof(PlayerSettings.SubtitleOutlineColor), nameof(PlayerSettings.SubtitleShadow),
        nameof(PlayerSettings.SubtitleShadowColor), nameof(PlayerSettings.SubtitleShadowDistance), nameof(PlayerSettings.SubtitleMargin),
        nameof(PlayerSettings.SubtitleDelay), nameof(PlayerSettings.ShowFileNameOnStart), nameof(PlayerSettings.FileNameSeconds),
    };

    /// <summary>The subtitle settings a playing core also takes, from its next line.</summary>
    public static IReadOnlySet<string> LiveKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.SubtitleTextScale), nameof(PlayerSettings.SubtitleColor), nameof(PlayerSettings.SubtitleOutline),
        nameof(PlayerSettings.SubtitleBackground), nameof(PlayerSettings.SubtitleBackgroundColor), nameof(PlayerSettings.SubtitleBackgroundOpacity),
    };

    /// <summary>freetype-outline-thickness for an Outline choice.</summary>
    public static int OutlineThickness(string outline) => outline switch { "None" => 0, "Thin" => 2, "Thick" => 6, _ => 4 };

    /// <summary>freetype-shadow-distance for a Shadow distance choice.</summary>
    public static double ShadowDistance(string distance) => distance switch { "Medium" => .1, "Large" => .15, _ => .06 };

    /// <summary>A percentage as VLC's 0 to 255 opacity.</summary>
    public static int Opacity(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) * 2.55, MidpointRounding.AwayFromZero);

    /// <summary>"#rrggbb" or "rrggbb" -> "#RRGGBB"; anything else is <paramref name="fallback"/>.</summary>
    public static string NormalizeColor(string? value, string fallback)
    {
        string hex = (value ?? string.Empty).Trim().TrimStart('#');
        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? "#" + hex.ToUpperInvariant() : fallback;
    }

    /// <summary>A normalized "#RRGGBB" as VLC's 0xRRGGBB integer.</summary>
    public static int ColorValue(string color) => int.Parse(NormalizeColor(color, DefaultColor).AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>The VLC options for Settings › Subtitles; none for VLC's defaults.</summary>
    public static IReadOnlyList<string> Arguments(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<string> arguments = [];
        if (!settings.SubtitlesEnabled) arguments.Add("--no-spu");
        if (settings.SubtitleEncoding.Length > 0) arguments.Add("--subsdec-encoding=" + settings.SubtitleEncoding);
        if (!settings.SubtitleAutoload) arguments.Add("--no-sub-autodetect-file");
        else if (settings.SubtitleMatching != DefaultMatching) arguments.Add("--sub-autodetect-fuzzy=" + Integer(settings.SubtitleMatching));

        // Appearance: VLC's text renderer reads these when it starts.
        if (settings.SubtitleFont.Length > 0) arguments.Add("--freetype-font=" + settings.SubtitleFont);
        if (settings.SubtitleTextScale != 100) arguments.Add("--sub-text-scale=" + Integer(settings.SubtitleTextScale));
        if (settings.SubtitleBold) arguments.Add("--freetype-bold");
        if (settings.SubtitleColor != DefaultColor) arguments.Add("--freetype-color=" + Integer(ColorValue(settings.SubtitleColor)));
        if (settings.SubtitleOpacity != 100) arguments.Add("--freetype-opacity=" + Integer(Opacity(settings.SubtitleOpacity)));
        if (settings.SubtitleOutline != "Normal") arguments.Add("--freetype-outline-thickness=" + Integer(OutlineThickness(settings.SubtitleOutline)));
        if (settings.SubtitleOutlineColor != DefaultOutlineColor) arguments.Add("--freetype-outline-color=" + Integer(ColorValue(settings.SubtitleOutlineColor)));
        if (!settings.SubtitleShadow) arguments.Add("--freetype-shadow-opacity=0");
        else
        {
            if (settings.SubtitleShadowColor != DefaultOutlineColor) arguments.Add("--freetype-shadow-color=" + Integer(ColorValue(settings.SubtitleShadowColor)));
            if (settings.SubtitleShadowDistance != "Small") arguments.Add("--freetype-shadow-distance=" + ShadowDistance(settings.SubtitleShadowDistance).ToString("0.###", CultureInfo.InvariantCulture));
        }
        if (settings.SubtitleBackground)
        {
            arguments.Add("--freetype-background-opacity=" + Integer(Opacity(settings.SubtitleBackgroundOpacity)));
            if (settings.SubtitleBackgroundColor != DefaultOutlineColor) arguments.Add("--freetype-background-color=" + Integer(ColorValue(settings.SubtitleBackgroundColor)));
        }
        if (settings.SubtitleMargin != 0) arguments.Add("--sub-margin=" + Integer(settings.SubtitleMargin));

        // Timing: VLC's sub-delay is in tenths of a second (each input's spu-delay starts at it).
        if (settings.SubtitleDelay != 0) arguments.Add("--sub-delay=" + Integer(settings.SubtitleDelay / 100));
        // The core starts with --no-video-title-show; a later option replaces it.
        if (settings.ShowFileNameOnStart)
        {
            arguments.Add("--video-title-show");
            arguments.Add("--video-title-timeout=" + Integer(settings.FileNameSeconds * 1000));
        }
        return arguments.AsReadOnly();
    }

    /// <summary>The text renderer variables for <paramref name="settings"/>, for a playing core.</summary>
    public static LiveSubtitleSettings Live(PlayerSettings settings) => new(
        settings.SubtitleTextScale,
        ColorValue(settings.SubtitleColor),
        OutlineThickness(settings.SubtitleOutline),
        settings.SubtitleBackground ? Opacity(settings.SubtitleBackgroundOpacity) : 0,
        ColorValue(settings.SubtitleBackgroundColor));

    /// <summary>Puts the subtitle fields in their ranges and choices.</summary>
    public static PlayerSettings Normalize(PlayerSettings value) => value with
    {
        SubtitleEncoding = Choice(value.SubtitleEncoding, Encodings, ""),
        SubtitleMatching = Math.Clamp(value.SubtitleMatching, 1, 4),
        SubtitleFont = NormalizeFont(value.SubtitleFont),
        SubtitleTextScale = Math.Clamp(value.SubtitleTextScale, MinimumTextScale, MaximumTextScale),
        SubtitleColor = NormalizeColor(value.SubtitleColor, DefaultColor),
        SubtitleOpacity = Math.Clamp(value.SubtitleOpacity, 0, 100),
        SubtitleOutline = Choice(value.SubtitleOutline, Outlines, "Normal"),
        SubtitleOutlineColor = NormalizeColor(value.SubtitleOutlineColor, DefaultOutlineColor),
        SubtitleShadowColor = NormalizeColor(value.SubtitleShadowColor, DefaultOutlineColor),
        SubtitleShadowDistance = Choice(value.SubtitleShadowDistance, ShadowDistances, "Small"),
        SubtitleBackgroundColor = NormalizeColor(value.SubtitleBackgroundColor, DefaultOutlineColor),
        SubtitleBackgroundOpacity = Math.Clamp(value.SubtitleBackgroundOpacity, 0, 100),
        SubtitleMargin = Math.Clamp(value.SubtitleMargin, 0, MaximumMargin),
        SubtitleDelay = (int)Math.Round(MediaDelayPolicy.Clamp(value.SubtitleDelay) / 100d, MidpointRounding.AwayFromZero) * 100,
        FileNameSeconds = Math.Clamp(value.FileNameSeconds, MinimumFileNameSeconds, MaximumFileNameSeconds),
    };

    // A family name, as Windows and VLC know it; no control characters.
    private static string NormalizeFont(string? font)
    {
        string value = new((font ?? string.Empty).Where(character => !char.IsControl(character)).ToArray());
        value = value.Trim();
        return value.Length > 100 ? value[..100].Trim() : value;
    }

    private static string Choice(string? value, IReadOnlyList<SettingOption> options, string fallback) =>
        options.Any(option => option.Value == value) ? value! : fallback;

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
}

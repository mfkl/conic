using System.Globalization;

namespace Player.Application;

/// <summary>
/// The video settings a playing core takes at once (VlcCorePlayer.LiveVideo):
/// VLC's video filter chain and subpicture sources with their parameters,
/// deinterlacing, and the pixel crop.
/// </summary>
public sealed record LiveVideoSettings(
    string VideoFilter,
    string SubSource,
    IReadOnlyDictionary<string, object> Parameters,
    int Deinterlace,
    string DeinterlaceMethod,
    (int Left, int Top, int Right, int Bottom) Crop)
{
    public bool Equals(LiveVideoSettings? other) =>
        other is not null && VideoFilter == other.VideoFilter && SubSource == other.SubSource && Deinterlace == other.Deinterlace &&
        DeinterlaceMethod == other.DeinterlaceMethod && Crop == other.Crop &&
        Parameters.Count == other.Parameters.Count && Parameters.All(pair => other.Parameters.TryGetValue(pair.Key, out object? value) && Equals(pair.Value, value));

    public override int GetHashCode() => HashCode.Combine(VideoFilter, SubSource, Deinterlace, DeinterlaceMethod, Crop, Parameters.Count);

    /// <summary>Whether <paramref name="other"/> has the same filters, sources and parameters.</summary>
    public bool SameFilters(LiveVideoSettings? other) => other is not null && VideoFilter == other.VideoFilter && SubSource == other.SubSource &&
        Parameters.Count == other.Parameters.Count && Parameters.All(pair => other.Parameters.TryGetValue(pair.Key, out object? value) && Equals(pair.Value, value));
}

/// <summary>
/// Settings › Video as VLC 3's own video filters, the ones of its Effects and
/// Filters window: the picture (adjust), rotation (transform), colors,
/// sharpening and the other effects, the logo and marquee overlays, plus
/// deinterlacing and the pixel crop. Each new core starts with them
/// (<see cref="Arguments"/>); a playing core takes them at once
/// (<see cref="Live"/>). The choices the rows offer live here.
/// </summary>
public static class VideoEngineOptions
{
    public const int DefaultSepiaIntensity = 120, DefaultPosterizeLevel = 6, DefaultLogoOpacity = 255;
    public const double DefaultSharpenSigma = .05;
    public const int MaximumCrop = 4000;

    // VLC's transform-type values.
    public static IReadOnlyList<SettingOption> Transforms { get; } =
    [
        new("None", "None"), new("90", "Rotate 90°"), new("180", "Rotate 180°"), new("270", "Rotate 270°"),
        new("hflip", "Flip horizontally"), new("vflip", "Flip vertically"), new("transpose", "Transpose"), new("antitranspose", "Anti-transpose"),
    ];

    // VLC's logo-position and marq-position values.
    public static IReadOnlyList<SettingOption> OverlayPositions { get; } =
    [
        new("6", "Top right"), new("5", "Top left"), new("10", "Bottom right"), new("9", "Bottom left"),
        new("4", "Top"), new("8", "Bottom"), new("0", "Center"),
    ];

    public static IReadOnlyList<SettingOption> SnapshotFormats { get; } = [new("png", "PNG"), new("jpg", "JPEG"), new("tiff", "TIFF")];

    /// <summary>The video settings a playing core takes at once.</summary>
    public static IReadOnlySet<string> LiveKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.Hue), nameof(PlayerSettings.Brightness), nameof(PlayerSettings.Contrast), nameof(PlayerSettings.Saturation), nameof(PlayerSettings.Gamma),
        nameof(PlayerSettings.Deinterlace), nameof(PlayerSettings.DeinterlaceMethod),
        nameof(PlayerSettings.VideoTransform), nameof(PlayerSettings.InvertColors), nameof(PlayerSettings.Sepia), nameof(PlayerSettings.SepiaIntensity),
        nameof(PlayerSettings.Posterize), nameof(PlayerSettings.PosterizeLevel), nameof(PlayerSettings.ColorExtract), nameof(PlayerSettings.ColorExtractColor),
        nameof(PlayerSettings.ColorThreshold), nameof(PlayerSettings.ColorThresholdColor), nameof(PlayerSettings.Sharpen), nameof(PlayerSettings.SharpenSigma),
        nameof(PlayerSettings.Debanding), nameof(PlayerSettings.FilmGrain), nameof(PlayerSettings.Denoise), nameof(PlayerSettings.MotionBlur),
        nameof(PlayerSettings.Mirror), nameof(PlayerSettings.AntiFlicker), nameof(PlayerSettings.Anaglyph),
        nameof(PlayerSettings.LogoFile), nameof(PlayerSettings.LogoPosition), nameof(PlayerSettings.LogoOpacity),
        nameof(PlayerSettings.MarqueeText), nameof(PlayerSettings.MarqueePosition),
        nameof(PlayerSettings.CropLeft), nameof(PlayerSettings.CropTop), nameof(PlayerSettings.CropRight), nameof(PlayerSettings.CropBottom),
    };

    /// <summary>The snapshot settings a new core starts with.</summary>
    public static IReadOnlySet<string> NextOpenKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.SnapshotFolder), nameof(PlayerSettings.SnapshotFormat),
    };

    /// <summary>
    /// The video filters, in the order VLC's Effects and Filters window
    /// chains them, except the picture (adjust), which goes last when other
    /// filters run: with hardware decoding VLC picks its Direct3D 11 adjust
    /// for GPU pictures, and a CPU filter after that one stops playback (VLC
    /// 3.0.24 cannot convert its output back), while after the CPU filters
    /// VLC picks its CPU adjust, which also has gamma.
    /// </summary>
    public static IReadOnlyList<string> Filters(PlayerSettings settings)
    {
        List<string> filters = [];
        if (settings.VideoTransform != "None") filters.Add("transform");
        if (settings.InvertColors) filters.Add("invert");
        if (settings.Sepia) filters.Add("sepia");
        if (settings.Posterize) filters.Add("posterize");
        if (settings.ColorExtract) filters.Add("extract");
        if (settings.ColorThreshold) filters.Add("colorthres");
        if (settings.Sharpen) filters.Add("sharpen");
        if (settings.Debanding) filters.Add("gradfun");
        if (settings.FilmGrain) filters.Add("grain");
        if (settings.Denoise) filters.Add("hqdn3d");
        if (settings.MotionBlur) filters.Add("motionblur");
        if (settings.Mirror) filters.Add("mirror");
        if (settings.AntiFlicker) filters.Add("antiflicker");
        if (settings.Anaglyph) filters.Add("anaglyph");
        if (!PlayerEngineOptions.IsNeutralPicture(settings)) filters.Add("adjust");
        return filters;
    }

    /// <summary>The subpicture sources: the logo when it has a file, the marquee when it has text.</summary>
    public static IReadOnlyList<string> SubSources(PlayerSettings settings)
    {
        List<string> sources = [];
        if (settings.LogoFile.Length > 0) sources.Add("logo");
        if (settings.MarqueeText.Length > 0) sources.Add("marq");
        return sources;
    }

    /// <summary>The parameters of the active filters and sources, as the values of VLC's options.</summary>
    public static IReadOnlyDictionary<string, object> Parameters(PlayerSettings settings)
    {
        Dictionary<string, object> parameters = new(StringComparer.Ordinal);
        if (!PlayerEngineOptions.IsNeutralPicture(settings))
        {
            parameters["hue"] = (float)settings.Hue;
            parameters["brightness"] = (float)settings.Brightness;
            parameters["contrast"] = (float)settings.Contrast;
            parameters["saturation"] = (float)settings.Saturation;
            parameters["gamma"] = (float)settings.Gamma;
        }
        if (settings.VideoTransform != "None") parameters["transform-type"] = settings.VideoTransform;
        if (settings.Sepia) parameters["sepia-intensity"] = (long)settings.SepiaIntensity;
        if (settings.Posterize) parameters["posterize-level"] = (long)settings.PosterizeLevel;
        if (settings.ColorExtract) parameters["extract-component"] = (long)SubtitleEngineOptions.ColorValue(settings.ColorExtractColor);
        if (settings.ColorThreshold) parameters["colorthres-color"] = (long)SubtitleEngineOptions.ColorValue(settings.ColorThresholdColor);
        if (settings.Sharpen) parameters["sharpen-sigma"] = (float)settings.SharpenSigma;
        if (settings.LogoFile.Length > 0)
        {
            parameters["logo-file"] = settings.LogoFile;
            parameters["logo-position"] = long.Parse(settings.LogoPosition, CultureInfo.InvariantCulture);
            parameters["logo-opacity"] = (long)settings.LogoOpacity;
        }
        if (settings.MarqueeText.Length > 0)
        {
            parameters["marq-marquee"] = settings.MarqueeText;
            parameters["marq-position"] = long.Parse(settings.MarqueePosition, CultureInfo.InvariantCulture);
        }
        return parameters;
    }

    /// <summary>Whether any pixel is cut from an edge.</summary>
    public static bool HasPixelCrop(PlayerSettings settings) => settings.CropLeft + settings.CropTop + settings.CropRight + settings.CropBottom > 0;

    /// <summary>The VLC options for these settings; none for VLC's defaults.</summary>
    public static IReadOnlyList<string> Arguments(PlayerSettings settings)
    {
        List<string> arguments = [];
        if (Filters(settings) is { Count: > 0 } filters) arguments.Add("--video-filter=" + string.Join(':', filters));
        if (SubSources(settings) is { Count: > 0 } sources) arguments.Add("--sub-source=" + string.Join(':', sources));
        foreach ((string name, object value) in Parameters(settings))
        {
            // The picture keeps its options to what differs from neutral, as before.
            if (name is "hue" && settings.Hue == 0 || name is "brightness" && settings.Brightness == 1 || name is "contrast" && settings.Contrast == 1 ||
                name is "saturation" && settings.Saturation == 1 || name is "gamma" && settings.Gamma == 1)
                continue;
            arguments.Add($"--{name}={Format(value)}");
        }
        if (HasPixelCrop(settings)) arguments.Add($"--crop={CropValue(settings)}");
        if (settings.SnapshotFolder.Length > 0) arguments.Add("--snapshot-path=" + settings.SnapshotFolder);
        if (settings.SnapshotFormat != "png") arguments.Add("--snapshot-format=" + settings.SnapshotFormat);
        return arguments.AsReadOnly();
    }

    /// <summary>The video values for <paramref name="settings"/>, for a playing core.</summary>
    public static LiveVideoSettings Live(PlayerSettings settings) => new(
        string.Join(':', Filters(settings)),
        string.Join(':', SubSources(settings)),
        Parameters(settings),
        settings.Deinterlace switch { "On" => 1, "Off" => 0, _ => -1 },
        settings.DeinterlaceMethod == "Automatic" ? "auto" : settings.DeinterlaceMethod,
        (settings.CropLeft, settings.CropTop, settings.CropRight, settings.CropBottom));

    /// <summary>Puts the video effect fields in their ranges and choices.</summary>
    public static PlayerSettings Normalize(PlayerSettings value) => value with
    {
        VideoTransform = Choice(value.VideoTransform, Transforms, "None"),
        SepiaIntensity = Math.Clamp(value.SepiaIntensity, 0, 255),
        PosterizeLevel = Math.Clamp(value.PosterizeLevel, 2, 256),
        ColorExtractColor = SubtitleEngineOptions.NormalizeColor(value.ColorExtractColor, "#FF0000"),
        ColorThresholdColor = SubtitleEngineOptions.NormalizeColor(value.ColorThresholdColor, "#FF0000"),
        SharpenSigma = double.IsFinite(value.SharpenSigma) ? Math.Round(Math.Clamp(value.SharpenSigma, 0, 2), 2) : DefaultSharpenSigma,
        LogoFile = (value.LogoFile ?? string.Empty).Trim(),
        LogoPosition = Choice(value.LogoPosition, OverlayPositions, "6"),
        LogoOpacity = Math.Clamp(value.LogoOpacity, 0, 255),
        MarqueeText = (value.MarqueeText ?? string.Empty).Trim(),
        MarqueePosition = Choice(value.MarqueePosition, OverlayPositions, "8"),
        CropLeft = Math.Clamp(value.CropLeft, 0, MaximumCrop),
        CropTop = Math.Clamp(value.CropTop, 0, MaximumCrop),
        CropRight = Math.Clamp(value.CropRight, 0, MaximumCrop),
        CropBottom = Math.Clamp(value.CropBottom, 0, MaximumCrop),
        SnapshotFolder = (value.SnapshotFolder ?? string.Empty).Trim(),
        SnapshotFormat = Choice(value.SnapshotFormat, SnapshotFormats, "png"),
    };

    private static string CropValue(PlayerSettings settings) =>
        string.Create(CultureInfo.InvariantCulture, $"{settings.CropLeft}+{settings.CropTop}+{settings.CropRight}+{settings.CropBottom}");

    private static string Choice(string? value, IReadOnlyList<SettingOption> options, string fallback) =>
        options.Any(option => option.Value == value) ? value! : fallback;

    private static string Format(object value) => value switch
    {
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

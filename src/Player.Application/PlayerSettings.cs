using System.Text.Json;

namespace Player.Application;

public enum SettingApplyMode { Live, NextOpen, Restart }

public sealed record PlayerSettings(
    int Version,
    bool ResumePlayback,
    bool RememberVolume,
    string Theme,
    string Density,
    int ControlTimeoutSeconds,
    double DefaultSpeed,
    int SkipBackwardSeconds,
    int SkipForwardSeconds,
    bool RememberRecentMedia = true,
    bool RememberResumeHistory = true,
    int ResumeMinimumElapsedSeconds = 30,
    int ResumeMaximumRemainingSeconds = 30,
    string AudioOutputModule = "Default",
    double? NormalizationMaximumLevel = null,
    bool? AudioPassthrough = null,
    IReadOnlyDictionary<string, string>? ShortcutOverrides = null,
    bool ReduceMotion = false,
    string Accent = AccentPalette.DefaultHex,
    // Version 7 (phase 9b): Playback, Video, Performance and Network. Every
    // field below has a default, so an older document reads as is.
    // Playback › Startup: Continue where you left off is Never (ResumePlayback
    // off), Ask (on) or Always (on, and ResumeAutomatically).
    bool ResumeAutomatically = false,
    bool AutoPlay = true,
    // Playback › Tracks: comma-separated ISO 639 codes, empty for no preference.
    string AudioLanguage = "",
    string SubtitleLanguage = "",
    // Playback › Order.
    string QueueRepeat = "Off",
    bool QueueShuffle = false,
    bool StopAfterEachItem = false,
    string PlaybackFinished = "Stop",
    // Playback › Seeking and Files.
    bool SeekPrecisely = true,
    string AviRepair = "Never",
    // Video › Output.
    bool VideoEnabled = true,
    bool StartFullscreen = false,
    bool StopScreensaver = true,
    // Video › Picture (the adjust filter's ranges and neutral values).
    double Hue = 0,
    double Brightness = 1,
    double Contrast = 1,
    double Saturation = 1,
    double Gamma = 1,
    // Video › Deinterlacing and Framing ("" is the media's own aspect/crop).
    string Deinterlace = "Automatic",
    string DeinterlaceMethod = "Automatic",
    string DefaultAspectRatio = "",
    string DefaultCrop = "",
    string DefaultZoom = "fit",
    // Video › Effects: VLC 3's Effects and Filters (VideoEngineOptions), live.
    string VideoTransform = "None",
    bool InvertColors = false,
    bool Sepia = false,
    int SepiaIntensity = VideoEngineOptions.DefaultSepiaIntensity,
    bool Posterize = false,
    int PosterizeLevel = VideoEngineOptions.DefaultPosterizeLevel,
    bool ColorExtract = false,
    string ColorExtractColor = "#FF0000",
    bool ColorThreshold = false,
    string ColorThresholdColor = "#FF0000",
    bool Sharpen = false,
    double SharpenSigma = VideoEngineOptions.DefaultSharpenSigma,
    bool Debanding = false,
    bool FilmGrain = false,
    bool Denoise = false,
    bool MotionBlur = false,
    bool Mirror = false,
    bool AntiFlicker = false,
    bool Anaglyph = false,
    // Video › Overlay: VLC's logo and marquee; positions are VLC's values.
    string LogoFile = "",
    string LogoPosition = "6",
    int LogoOpacity = VideoEngineOptions.DefaultLogoOpacity,
    string MarqueeText = "",
    string MarqueePosition = "8",
    // Video › Framing: pixels cut from each edge (overrides the crop ratio).
    int CropLeft = 0,
    int CropTop = 0,
    int CropRight = 0,
    int CropBottom = 0,
    // Video › Snapshots: "" is the system's pictures folder.
    string SnapshotFolder = "",
    string SnapshotFormat = "png",
    // Performance.
    string HardwareDecoding = "Automatic",
    int SkipLoopFilter = 0,
    int DecodingThreads = 0,
    bool DropLateFrames = true,
    // Network: caching in milliseconds, used as is only with Buffering Custom
    // (live capture and discs keep VLC's defaults: the app opens neither).
    string Buffering = "Normal",
    int FileCaching = PlayerEngineOptions.DefaultFileCaching,
    int NetworkCaching = PlayerEngineOptions.DefaultNetworkCaching,
    string HttpProxy = "",
    // The proxy password, protected for the current Windows user (DPAPI), base64.
    string? HttpProxyPasswordProtected = null,
    bool VerifyCertificates = true,
    string CipherPreference = "Normal",
    // Phase 9c: Audio (AudioEngineOptions). Every field below has a default,
    // so a version 7 document reads as is.
    bool AudioEnabled = true,
    // The MMDevice endpoint ID; empty is the system default.
    string AudioDevice = "",
    // Percent: the volume slider's maximum, the volume each start begins at
    // when Remember volume is off, and the arrow keys' step on the slider.
    int MaximumVolume = 200,
    int StartupVolume = 100,
    int VolumeStep = 2,
    // Loudness; normalization is NormalizationMaximumLevel (null when off).
    string ReplayGain = "none",
    double ReplayGainPreamp = 0,
    // Effects. The equalizer's gains are in dB: its preamp is neutral at 12.
    bool Equalizer = false,
    string EqualizerPreset = "flat",
    double EqualizerPreamp = AudioEngineOptions.NeutralEqualizerPreamp,
    string EqualizerBands = "0 0 0 0 0 0 0 0 0 0",
    bool SpatialAudio = false,
    string SpatialAudioMode = "Headphones",
    bool Compressor = false,
    double PitchShift = 0,
    bool Karaoke = false,
    // The compressor's and the Room spatializer's parameters (VLC's defaults).
    double CompressorRmsPeak = 0.2,
    double CompressorAttack = 25,
    double CompressorRelease = 100,
    double CompressorThreshold = -11,
    double CompressorRatio = 4,
    double CompressorKnee = 5,
    double CompressorMakeupGain = 7,
    double SpatializerRoomSize = 0.85,
    double SpatializerWidth = 1,
    double SpatializerWet = 0.4,
    double SpatializerDry = 0.5,
    double SpatializerDamp = 0.5,
    // Advanced, and the audio delay new media starts with (milliseconds).
    string DolbySurround = "Automatic",
    string ResamplerQuality = "Balanced",
    int AudioDelay = 0,
    // VLC 3's Audio › Stereo Mode (live) and time-stretching (on in VLC).
    string StereoMode = "Unset",
    bool TimeStretch = true,
    // Version 8 (phase 9d): Subtitles (SubtitleEngineOptions), Controls,
    // Interface and Library (InterfaceOptions). Every field below has a
    // default; Migrate turns AutoHideControls off for a version 7 or older
    // document with Reduce motion on, which used to keep the controls up.
    // Subtitles › Tracks and Loading; the preferred language is SubtitleLanguage.
    bool SubtitlesEnabled = true,
    string SubtitleEncoding = "",
    bool SubtitleAutoload = true,
    int SubtitleMatching = SubtitleEngineOptions.DefaultMatching,
    // Subtitles › Appearance: VLC's text renderer (freetype) draws them.
    // Colors are #RRGGBB, opacities and the text size percentages, the
    // margin in video pixels from the bottom.
    string SubtitleFont = "",
    int SubtitleTextScale = 100,
    bool SubtitleBold = false,
    string SubtitleColor = "#FFFFFF",
    int SubtitleOpacity = 100,
    string SubtitleOutline = "Normal",
    string SubtitleOutlineColor = "#000000",
    bool SubtitleShadow = true,
    string SubtitleShadowColor = "#000000",
    string SubtitleShadowDistance = "Small",
    bool SubtitleBackground = false,
    string SubtitleBackgroundColor = "#000000",
    int SubtitleBackgroundOpacity = 50,
    int SubtitleMargin = 0,
    // Subtitles › Timing (milliseconds, in VLC's 100 ms steps) and On-Screen Messages.
    int SubtitleDelay = 0,
    bool ShowFileNameOnStart = false,
    int FileNameSeconds = 5,
    bool ShortcutFeedback = true,
    double ShortcutFeedbackSeconds = 1,
    // Controls › Skip Amounts (the short skip is SkipBackwardSeconds and
    // SkipForwardSeconds) and Mouse.
    int VeryShortSkipSeconds = 3,
    int MediumSkipSeconds = 60,
    int LongSkipSeconds = 300,
    string ClickAction = "None",
    string DoubleClickAction = "Fullscreen",
    string WheelAction = "Volume",
    string HorizontalWheelAction = "Seek",
    bool ReverseWheel = false,
    // Interface › Window and Playback Controls.
    bool RememberWindowGeometry = true,
    bool KeepOnTop = false,
    bool ShowFileNameInTitle = true,
    // VLC's one-instance: a second start hands its media to the running Conic.
    bool OneInstance = false,
    bool ResizeToVideo = false,
    bool PauseWhenMinimized = false,
    bool ShowControlsInFullscreen = true,
    bool AutoHideControls = true,
    // Library › Opening Files.
    bool EnqueueOnOpen = false,
    string FolderOpening = "Subfolders",
    bool ShowHiddenFiles = false)
{
    public const int CurrentVersion = 8;
    public const double MinimumDefaultSpeed = .25, MaximumDefaultSpeed = 2;
    public static IReadOnlyList<string> Themes { get; } = Array.AsReadOnly(new[] { "Dark", "Light", "System" });
    // Dark is the default and the fallback for an unknown value; a stored
    // "System" stays as it is.
    public static PlayerSettings Default { get; } = new(CurrentVersion, true, true, "Dark", "Comfortable", 3, 1, 10, 10, true, true, 30, 30, "Default");
}

public interface IPlayerSettingsStorage
{
    string? Read();
    void WriteAtomically(string content);
    void PreserveInvalid(string content);
}

public sealed class PlayerSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly IPlayerSettingsStorage _storage;

    public PlayerSettingsService(IPlayerSettingsStorage storage) => _storage = storage;
    public PlayerSettings Current { get; private set; } = PlayerSettings.Default;
    public string? LoadDiagnostic { get; private set; }
    public event EventHandler<PlayerSettings>? Changed;

    public void Load()
    {
        string? document;
        try { document = _storage.Read(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Current = PlayerSettings.Default; LoadDiagnostic = "Settings could not be read; defaults are in use."; return; }
        if (string.IsNullOrWhiteSpace(document)) { Current = PlayerSettings.Default; return; }
        try
        {
            using JsonDocument json = JsonDocument.Parse(document);
            int version = TryGet(json.RootElement, "Version", out JsonElement versionValue) ? versionValue.GetInt32() : 1;
            bool migrated = version != PlayerSettings.CurrentVersion;
            Current = Normalize(Upgrade(Migrate(json.RootElement, version), version));
            if (migrated)
                _storage.WriteAtomically(JsonSerializer.Serialize(Current, JsonOptions));
            LoadDiagnostic = null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            try { _storage.PreserveInvalid(document); } catch (Exception preserveException) when (preserveException is IOException or UnauthorizedAccessException) { }
            Current = PlayerSettings.Default;
            LoadDiagnostic = "Settings were invalid or unavailable; defaults are in use.";
            try { _storage.WriteAtomically(JsonSerializer.Serialize(Current, JsonOptions)); } catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Update(Func<PlayerSettings, PlayerSettings> update)
    {
        PlayerSettings next = Normalize(update(Current));
        _storage.WriteAtomically(JsonSerializer.Serialize(next, JsonOptions));
        Current = next;
        Changed?.Invoke(this, next);
    }

    public void ResetAppearance() => Update(current => current with { Theme = PlayerSettings.Default.Theme, Accent = PlayerSettings.Default.Accent, Density = PlayerSettings.Default.Density, ControlTimeoutSeconds = PlayerSettings.Default.ControlTimeoutSeconds, ReduceMotion = PlayerSettings.Default.ReduceMotion });
    public void ResetPlayback() => Update(current => current with { DefaultSpeed = PlayerSettings.Default.DefaultSpeed, SkipBackwardSeconds = PlayerSettings.Default.SkipBackwardSeconds, SkipForwardSeconds = PlayerSettings.Default.SkipForwardSeconds });
    public void ResetGeneral() => Update(current => current with { ResumePlayback = PlayerSettings.Default.ResumePlayback, RememberVolume = PlayerSettings.Default.RememberVolume, RememberRecentMedia = PlayerSettings.Default.RememberRecentMedia, RememberResumeHistory = PlayerSettings.Default.RememberResumeHistory, ResumeMinimumElapsedSeconds = PlayerSettings.Default.ResumeMinimumElapsedSeconds, ResumeMaximumRemainingSeconds = PlayerSettings.Default.ResumeMaximumRemainingSeconds });
    public void ResetAll() => Update(_ => PlayerSettings.Default);

    public static SettingApplyMode ApplyMode(string key) => key switch
    {
        nameof(PlayerSettings.Theme) or nameof(PlayerSettings.Accent) or nameof(PlayerSettings.Density) or nameof(PlayerSettings.ControlTimeoutSeconds) or nameof(PlayerSettings.ReduceMotion)
            or nameof(PlayerSettings.SkipBackwardSeconds) or nameof(PlayerSettings.SkipForwardSeconds) => SettingApplyMode.Live,
        nameof(PlayerSettings.RememberRecentMedia) or nameof(PlayerSettings.RememberResumeHistory) => SettingApplyMode.Live,
        // The app applies these itself, at once: the queue modes go to the
        // playing queue, the end action and the screensaver follow playback.
        nameof(PlayerSettings.QueueRepeat) or nameof(PlayerSettings.QueueShuffle) or nameof(PlayerSettings.PlaybackFinished)
            or nameof(PlayerSettings.StopScreensaver) or nameof(PlayerSettings.MaximumVolume) or nameof(PlayerSettings.VolumeStep) => SettingApplyMode.Live,
        // The audio effects, replay gain and the device also reach a playing core.
        _ when AudioEngineOptions.LiveKeys.Contains(key) => SettingApplyMode.Live,
        // The video filters, overlays, deinterlacing and pixel crop too.
        _ when VideoEngineOptions.LiveKeys.Contains(key) => SettingApplyMode.Live,
        _ when VideoEngineOptions.NextOpenKeys.Contains(key) => SettingApplyMode.NextOpen,
        // The subtitle text's size, color, outline and background reach a
        // playing core; the controls, window and mouse settings are the app's own.
        _ when SubtitleEngineOptions.LiveKeys.Contains(key) || InterfaceOptions.LiveKeys.Contains(key) => SettingApplyMode.Live,
        _ when SubtitleEngineOptions.NextOpenKeys.Contains(key) || InterfaceOptions.NextOpenKeys.Contains(key) => SettingApplyMode.NextOpen,
        // RememberVolume and StartupVolume decide the volume the next start
        // begins at, so they are Restart.
        nameof(PlayerSettings.DefaultSpeed) or nameof(PlayerSettings.ResumePlayback) or nameof(PlayerSettings.ResumeAutomatically)
            or nameof(PlayerSettings.ResumeMinimumElapsedSeconds) or nameof(PlayerSettings.ResumeMaximumRemainingSeconds)
            or nameof(PlayerSettings.StartFullscreen) => SettingApplyMode.NextOpen,
        // Every open starts a new VLC core; these are its options (PlayerEngineOptions).
        _ when PlayerEngineOptions.Keys.Contains(key) || AudioEngineOptions.NextOpenKeys.Contains(key) => SettingApplyMode.NextOpen,
        _ => SettingApplyMode.Restart,
    };

    private static PlayerSettings Migrate(JsonElement root, int version) => version switch
    {
        // Version 6 added Accent, version 7 the Playback, Video, Performance
        // and Network fields, version 8 the Subtitles, Controls, Interface
        // and Library ones. Every added field has a default, so a version 5,
        // 6 or 7 document reads as is: the values it holds stay and the
        // missing ones take the record's defaults (then Upgrade).
        5 or 6 or 7 or PlayerSettings.CurrentVersion => JsonSerializer.Deserialize<PlayerSettings>(root, JsonOptions) ?? throw new JsonException("Settings document was empty."),
        0 or 1 or 2 or 3 or 4 => PlayerSettings.Default with
        {
            ResumePlayback = Read(root, nameof(PlayerSettings.ResumePlayback), PlayerSettings.Default.ResumePlayback),
            RememberVolume = Read(root, nameof(PlayerSettings.RememberVolume), PlayerSettings.Default.RememberVolume),
            Theme = Read(root, nameof(PlayerSettings.Theme), PlayerSettings.Default.Theme),
            Density = Read(root, nameof(PlayerSettings.Density), PlayerSettings.Default.Density),
            DefaultSpeed = Read(root, nameof(PlayerSettings.DefaultSpeed), PlayerSettings.Default.DefaultSpeed),
            SkipBackwardSeconds = Read(root, nameof(PlayerSettings.SkipBackwardSeconds), PlayerSettings.Default.SkipBackwardSeconds),
            SkipForwardSeconds = Read(root, nameof(PlayerSettings.SkipForwardSeconds), PlayerSettings.Default.SkipForwardSeconds),
            RememberRecentMedia = Read(root, nameof(PlayerSettings.RememberRecentMedia), PlayerSettings.Default.RememberRecentMedia),
            RememberResumeHistory = Read(root, nameof(PlayerSettings.RememberResumeHistory), PlayerSettings.Default.RememberResumeHistory),
            ResumeMinimumElapsedSeconds = Read(root, nameof(PlayerSettings.ResumeMinimumElapsedSeconds), PlayerSettings.Default.ResumeMinimumElapsedSeconds),
            ResumeMaximumRemainingSeconds = Read(root, nameof(PlayerSettings.ResumeMaximumRemainingSeconds), PlayerSettings.Default.ResumeMaximumRemainingSeconds),
            AudioOutputModule = Read(root, nameof(PlayerSettings.AudioOutputModule), PlayerSettings.Default.AudioOutputModule),
            NormalizationMaximumLevel = Read<double?>(root, nameof(PlayerSettings.NormalizationMaximumLevel), null),
            AudioPassthrough = Read<bool?>(root, nameof(PlayerSettings.AudioPassthrough), null),
            ShortcutOverrides = Read<IReadOnlyDictionary<string, string>?>(root, nameof(PlayerSettings.ShortcutOverrides), null),
            ReduceMotion = Read(root, nameof(PlayerSettings.ReduceMotion), PlayerSettings.Default.ReduceMotion),
        },
        _ => throw new JsonException("Unsupported settings version."),
    };

    // Stored values whose meaning changed. Before version 8 Reduce motion
    // also kept the playback controls from hiding; Auto-hide the controls
    // now does that, so those users keep their controls up.
    private static PlayerSettings Upgrade(PlayerSettings value, int version) =>
        version < 8 ? value with { AutoHideControls = !value.ReduceMotion } : value;

    private static T Read<T>(JsonElement root, string name, T fallback) => TryGet(root, name, out JsonElement value)
        ? JsonSerializer.Deserialize<T>(value, JsonOptions) ?? fallback : fallback;

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (JsonProperty property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default; return false;
    }

    private static PlayerSettings Normalize(PlayerSettings value) => InterfaceOptions.Normalize(SubtitleEngineOptions.Normalize(AudioEngineOptions.Normalize(VideoEngineOptions.Normalize(PlayerEngineOptions.Normalize(value))))) with
    {
        Version = PlayerSettings.CurrentVersion,
        Theme = PlayerSettings.Themes.Contains(value.Theme) ? value.Theme : PlayerSettings.Default.Theme,
        Accent = AccentPalette.Find(value.Accent) ?? PlayerSettings.Default.Accent,
        Density = value.Density is "Compact" or "Comfortable" ? value.Density : PlayerSettings.Default.Density,
        ControlTimeoutSeconds = Math.Clamp(value.ControlTimeoutSeconds, 1, 15),
        DefaultSpeed = NormalizeSpeed(value.DefaultSpeed),
        SkipBackwardSeconds = Math.Clamp(value.SkipBackwardSeconds, 1, 120),
        SkipForwardSeconds = Math.Clamp(value.SkipForwardSeconds, 1, 120),
        ResumeMinimumElapsedSeconds = Math.Clamp(value.ResumeMinimumElapsedSeconds, 0, 3600),
        ResumeMaximumRemainingSeconds = Math.Clamp(value.ResumeMaximumRemainingSeconds, 0, 3600),
        AudioOutputModule = value.AudioOutputModule is "Default" or "mmdevice" or "directsound" or "waveout" ? value.AudioOutputModule : "Default",
        ShortcutOverrides = NormalizeShortcuts(value.ShortcutOverrides),
    };

    // The design's speeds run from 0.25 to 2 in 0.25 steps. Older documents
    // could hold up to 4 or a value off the steps; they snap to the nearest
    // step in range instead of falling back to 1.
    private static double NormalizeSpeed(double speed) => double.IsFinite(speed)
        ? Math.Clamp(Math.Round(speed * 4, MidpointRounding.AwayFromZero) / 4, PlayerSettings.MinimumDefaultSpeed, PlayerSettings.MaximumDefaultSpeed)
        : PlayerSettings.Default.DefaultSpeed;

    private static IReadOnlyDictionary<string, string>? NormalizeShortcuts(IReadOnlyDictionary<string, string>? shortcuts)
    {
        if (shortcuts is null) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in shortcuts)
            if (PlayerShortcutPolicy.TryParseNamedCommand(pair.Key, out PlayerCommandId command) && PlayerShortcutPolicy.Normalize(pair.Value) is { Length: > 0 } gesture && !result.Values.Contains(gesture, StringComparer.OrdinalIgnoreCase)) result[command.ToString()] = gesture;
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }
}

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
    string Accent = AccentPalette.DefaultHex)
{
    public const int CurrentVersion = 6;
    public const double MinimumDefaultSpeed = .25, MaximumDefaultSpeed = 2;
    public static IReadOnlyList<string> Themes { get; } = Array.AsReadOnly(new[] { "Dark", "Light", "System" });
    public static PlayerSettings Default { get; } = new(CurrentVersion, true, true, "System", "Comfortable", 3, 1, 10, 10, true, true, 30, 30, "Default");
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
            Current = Normalize(Migrate(json.RootElement, version));
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
        // RememberVolume decides the volume the next start begins at, so it is Restart.
        nameof(PlayerSettings.DefaultSpeed) or nameof(PlayerSettings.ResumePlayback)
            or nameof(PlayerSettings.ResumeMinimumElapsedSeconds) or nameof(PlayerSettings.ResumeMaximumRemainingSeconds) => SettingApplyMode.NextOpen,
        _ => SettingApplyMode.Restart,
    };

    private static PlayerSettings Migrate(JsonElement root, int version) => version switch
    {
        // Version 6 added Accent; a version 5 document reads as is and the
        // missing value takes the record's default.
        5 or PlayerSettings.CurrentVersion => JsonSerializer.Deserialize<PlayerSettings>(root, JsonOptions) ?? throw new JsonException("Settings document was empty."),
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

    private static T Read<T>(JsonElement root, string name, T fallback) => TryGet(root, name, out JsonElement value)
        ? JsonSerializer.Deserialize<T>(value, JsonOptions) ?? fallback : fallback;

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (JsonProperty property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default; return false;
    }

    private static PlayerSettings Normalize(PlayerSettings value) => value with
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
        NormalizationMaximumLevel = value.NormalizationMaximumLevel is double level && double.IsFinite(level) ? Math.Clamp(level, .5, 10) : null,
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

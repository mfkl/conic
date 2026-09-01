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
    int SkipForwardSeconds)
{
    public const int CurrentVersion = 2;
    public static PlayerSettings Default { get; } = new(CurrentVersion, true, true, "System", "Comfortable", 3, 1, 10, 10);
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
    public event EventHandler<PlayerSettings>? Changed;

    public void Load()
    {
        string? document = _storage.Read();
        if (string.IsNullOrWhiteSpace(document)) { Current = PlayerSettings.Default; return; }
        try
        {
            Current = Normalize(Migrate(JsonSerializer.Deserialize<PlayerSettings>(document, JsonOptions)
                ?? throw new JsonException("Settings document was empty.")));
        }
        catch (JsonException)
        {
            _storage.PreserveInvalid(document);
            Current = PlayerSettings.Default;
        }
    }

    public void Update(Func<PlayerSettings, PlayerSettings> update)
    {
        PlayerSettings next = Normalize(update(Current));
        _storage.WriteAtomically(JsonSerializer.Serialize(next, JsonOptions));
        Current = next;
        Changed?.Invoke(this, next);
    }

    public void ResetAppearance() => Update(current => current with { Theme = PlayerSettings.Default.Theme, Density = PlayerSettings.Default.Density, ControlTimeoutSeconds = PlayerSettings.Default.ControlTimeoutSeconds });
    public void ResetPlayback() => Update(current => current with { DefaultSpeed = PlayerSettings.Default.DefaultSpeed, SkipBackwardSeconds = PlayerSettings.Default.SkipBackwardSeconds, SkipForwardSeconds = PlayerSettings.Default.SkipForwardSeconds });
    public void ResetGeneral() => Update(current => current with { ResumePlayback = PlayerSettings.Default.ResumePlayback, RememberVolume = PlayerSettings.Default.RememberVolume });
    public void ResetAll() => Update(_ => PlayerSettings.Default);

    public static SettingApplyMode ApplyMode(string key) => key switch
    {
        nameof(PlayerSettings.Theme) or nameof(PlayerSettings.Density) or nameof(PlayerSettings.ControlTimeoutSeconds)
            or nameof(PlayerSettings.SkipBackwardSeconds) or nameof(PlayerSettings.SkipForwardSeconds) => SettingApplyMode.Live,
        nameof(PlayerSettings.DefaultSpeed) or nameof(PlayerSettings.ResumePlayback) or nameof(PlayerSettings.RememberVolume) => SettingApplyMode.NextOpen,
        _ => SettingApplyMode.Restart,
    };

    private static PlayerSettings Migrate(PlayerSettings value) => value.Version switch
    {
        PlayerSettings.CurrentVersion => value,
        0 or 1 => value with { Version = PlayerSettings.CurrentVersion, Theme = string.IsNullOrWhiteSpace(value.Theme) ? "System" : value.Theme, Density = string.IsNullOrWhiteSpace(value.Density) ? "Comfortable" : value.Density },
        _ => throw new JsonException("Unsupported settings version."),
    };

    private static PlayerSettings Normalize(PlayerSettings value) => value with
    {
        Version = PlayerSettings.CurrentVersion,
        Theme = value.Theme is "Dark" or "System" ? value.Theme : PlayerSettings.Default.Theme,
        Density = value.Density is "Compact" or "Comfortable" ? value.Density : PlayerSettings.Default.Density,
        ControlTimeoutSeconds = Math.Clamp(value.ControlTimeoutSeconds, 1, 15),
        DefaultSpeed = Math.Clamp(value.DefaultSpeed, .25, 4),
        SkipBackwardSeconds = Math.Clamp(value.SkipBackwardSeconds, 1, 120),
        SkipForwardSeconds = Math.Clamp(value.SkipForwardSeconds, 1, 120),
    };
}

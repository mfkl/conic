using System;
using System.IO;
using System.Text.Json;

namespace Conic.Services;

/// <summary>A volume in percent (0 to 200, as the volume slider) and the mute state.</summary>
internal sealed record RememberedVolume(double Volume, bool Muted)
{
    /// <summary>Where playback starts when Remember volume is off or nothing was saved.</summary>
    public static RememberedVolume Default { get; } = new(100, false);
}

/// <summary>Keeps the volume that Settings › Audio › Remember volume restores at the next start.</summary>
internal interface IVolumeMemory
{
    RememberedVolume? Load();
    void Save(RememberedVolume value);
}

/// <summary>
/// Stores the remembered volume in its own small file (volume.json in the
/// profile), like the window geometry, so a volume change never rewrites the
/// settings document or raises its change notifications.
/// </summary>
internal sealed class FileVolumeMemory(string path) : IVolumeMemory
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public RememberedVolume? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            RememberedVolume? value = JsonSerializer.Deserialize<RememberedVolume>(File.ReadAllText(path), Options);
            return value is not null && double.IsFinite(value.Volume)
                ? value with { Volume = Math.Clamp(value.Volume, 0, 200) }
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file starts at the default volume.
            return null;
        }
    }

    public void Save(RememberedVolume value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Failing to remember the volume must never block playback or shutdown.
        }
    }
}

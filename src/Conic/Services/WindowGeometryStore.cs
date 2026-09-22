using System;
using System.IO;
using System.Text.Json;

namespace Conic.Services;

/// <summary>Persists the main window's size, position and state across runs.</summary>
internal sealed class WindowGeometryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public WindowGeometryStore(string path) => _path = path;

    public WindowGeometry? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<WindowGeometry>(File.ReadAllText(_path), Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable geometry file must never block startup.
            return null;
        }
    }

    public void Save(WindowGeometry geometry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(geometry, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Failing to persist geometry must never block shutdown.
        }
    }
}

internal sealed record WindowGeometry(
    double Width,
    double Height,
    int? X,
    int? Y,
    string State);

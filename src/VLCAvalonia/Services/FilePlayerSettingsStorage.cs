using Player.Application;

namespace VLCAvalonia.Services;

internal sealed class FilePlayerSettingsStorage : IPlayerSettingsStorage
{
    private readonly string _path;
    public FilePlayerSettingsStorage(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VLCAvalonia", "settings.json");
    public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;
    public void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = Path.Combine(Path.GetDirectoryName(_path)!, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream)) { writer.Write(content); writer.Flush(); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public void PreserveInvalid(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".json", content);
    }
}

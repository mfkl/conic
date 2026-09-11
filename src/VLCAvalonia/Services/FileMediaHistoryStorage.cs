using Player.Application;

namespace VLCAvalonia.Services;

internal sealed class FileMediaHistoryStorage : IMediaHistoryStorage
{
    private readonly string _path;
    public FileMediaHistoryStorage(string? path = null) => _path = path ?? PlayerProfile.FilePath("media-history.json");
    public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;
    public void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = Path.Combine(Path.GetDirectoryName(_path)!, "." + Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream)) { writer.Write(content); writer.Flush(); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
    public void PreserveInvalid(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".invalid-" + Guid.NewGuid().ToString("N") + ".json", content);
    }
}

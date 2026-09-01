using Player.Application;

namespace VLCAvalonia.Services;

internal sealed class FileMediaHistoryStorage : IMediaHistoryStorage
{
    private readonly string _path;
    public FileMediaHistoryStorage(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VLCAvalonia", "media-history.json");
    public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;
    public void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, _path, true);
    }
}

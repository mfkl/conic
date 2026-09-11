using Player.Application;
using Conic.Services;
using Xunit;

namespace Conic.UITests;

public sealed class FileMediaHistoryStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vlc-history-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_dir, "history.json");
    [Fact]
    public void WritesAndReplacesWithoutTemporaryOrphans()
    {
        FileMediaHistoryStorage storage = new(PathName); storage.WriteAtomically("first"); Assert.Equal("first", storage.Read());
        storage.WriteAtomically("second"); Assert.Equal("second", storage.Read()); Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
    [Fact]
    public void CorruptHistoryIsPreservedAndReloadsAsEmpty()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(PathName, "{ nope"); MediaHistoryService service = new(new FileMediaHistoryStorage(PathName)); service.Load();
        string backup = Assert.Single(Directory.GetFiles(_dir, "history.json.invalid-*.json")); Assert.Equal("{ nope", File.ReadAllText(backup));
        MediaHistoryService reload = new(new FileMediaHistoryStorage(PathName)); reload.Load(); Assert.Null(reload.LoadDiagnostic); Assert.Empty(reload.Recent);
    }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
}

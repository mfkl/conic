using Player.Application;
using VLCAvalonia.Services;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class FilePlayerSettingsStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VLCAvalonia-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => System.IO.Path.Combine(_directory, "settings.json");

    [Fact]
    public void WritesReplacesAndLeavesNoTemporaryFiles()
    {
        FilePlayerSettingsStorage storage = new(SettingsPath);
        storage.WriteAtomically("first");
        Assert.Equal("first", storage.Read());
        storage.WriteAtomically("second");
        Assert.Equal("second", storage.Read());
        Assert.Empty(Directory.GetFiles(_directory, ".settings-*.tmp"));
    }

    [Fact]
    public void CorruptDocumentIsBackedUpAndRewrittenAsDefaults()
    {
        Directory.CreateDirectory(_directory);
        const string corrupt = "{ invalid";
        File.WriteAllText(SettingsPath, corrupt);
        PlayerSettingsService service = new(new FilePlayerSettingsStorage(SettingsPath));
        service.Load();
        string backup = Assert.Single(Directory.GetFiles(_directory, "settings.json.invalid-*.json"));
        Assert.Equal(corrupt, File.ReadAllText(backup));
        Assert.Contains($"\"Version\": {PlayerSettings.CurrentVersion}", File.ReadAllText(SettingsPath));
        PlayerSettingsService reloaded = new(new FilePlayerSettingsStorage(SettingsPath));
        reloaded.Load();
        Assert.Equal(PlayerSettings.Default, reloaded.Current);
        Assert.Null(reloaded.LoadDiagnostic);
    }

    [Fact]
    public void ReadFailureFallsBackAndSuccessfulReloadClearsDiagnostic()
    {
        PlayerSettingsService service = new(new ThrowingReadStorage());
        service.Load();
        Assert.Equal(PlayerSettings.Default, service.Current);
        Assert.NotNull(service.LoadDiagnostic);
        FilePlayerSettingsStorage valid = new(SettingsPath);
        valid.WriteAtomically("{\"Version\":2,\"ResumePlayback\":true,\"RememberVolume\":true,\"Theme\":\"System\",\"Density\":\"Comfortable\",\"ControlTimeoutSeconds\":3,\"DefaultSpeed\":1,\"SkipBackwardSeconds\":10,\"SkipForwardSeconds\":10}");
        service = new PlayerSettingsService(valid);
        service.Load();
        Assert.Null(service.LoadDiagnostic);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class ThrowingReadStorage : IPlayerSettingsStorage
    {
        public string? Read() => throw new IOException();
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }
}

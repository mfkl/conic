using Conic.Services;
using Xunit;

namespace Conic.UITests;

public sealed class FileVolumeMemoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Conic-volume-" + Guid.NewGuid().ToString("N"));
    private string VolumePath => Path.Combine(_directory, "volume.json");

    [Fact]
    public void SavesAndLoadsTheVolumeAndMute()
    {
        FileVolumeMemory memory = new(VolumePath);
        Assert.Null(memory.Load());

        memory.Save(new RememberedVolume(135, true));

        Assert.Equal(new RememberedVolume(135, true), new FileVolumeMemory(VolumePath).Load());
    }

    [Theory]
    [InlineData("{ broken", null)]
    [InlineData("{\"Volume\":500,\"Muted\":false}", 200d)]
    [InlineData("{\"Volume\":-3,\"Muted\":true}", 0d)]
    public void UnreadableOrOutOfRangeFilesStayUsable(string content, double? expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(VolumePath, content);

        Assert.Equal(expected, new FileVolumeMemory(VolumePath).Load()?.Volume);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

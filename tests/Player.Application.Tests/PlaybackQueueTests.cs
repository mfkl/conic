using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlaybackQueueTests
{
    [Fact]
    public void DailyUseDelayPolicyUsesMillisecondsAndAProductBound()
    {
        Assert.Equal(-10_000, MediaDelayPolicy.Clamp(-99_000));
        Assert.Equal(10_000, MediaDelayPolicy.Clamp(99_000));
        Assert.Equal(100, MediaDelayPolicy.StepMilliseconds);
    }
    [Fact]
    public void QueueNavigationOnlyEnablesForSemanticNeighbors()
    {
        QueueItem[] items = [new("a", "A", "a", 0, true), new("b", "B", "b", 1, false)];
        PlaybackQueueState first = new(2, 1, QueueLoadState.Ready, items, 0, QueueRepeatMode.None, QueueOrder.Normal);
        Assert.False(first.CanPrevious); Assert.True(first.CanNext);
        PlaybackQueueState wrapped = first with { CurrentIndex = 0, Repeat = QueueRepeatMode.All };
        Assert.True(wrapped.CanPrevious); Assert.True(wrapped.CanNext);
        PlaybackQueueState random = first with { Order = QueueOrder.Random };
        Assert.True(random.CanPrevious); Assert.True(random.CanNext);
    }

    [Fact]
    public void PolledStatesWithEqualListsAreEqual()
    {
        static QueueItem[] Items() => [new("a", "A", "a", 0, true), new("b", "B", "b", 1, false)];
        PlaybackQueueState queue = new(2, 1, QueueLoadState.Ready, Items(), 0, QueueRepeatMode.None, QueueOrder.Normal);
        Assert.Equal(queue, queue with { Items = Items() });
        Assert.Equal(queue.GetHashCode(), (queue with { Items = Items() }).GetHashCode());
        Assert.NotEqual(queue, queue with { Items = [Items()[0] with { IsCurrent = false }, Items()[1]] });

        static MediaTrack[] Tracks() => [new("-1", "Disable", true, false), new("2", "English", false, true)];
        MediaCapabilitiesState capabilities = new(2, 1, MediaCapabilityLoadState.Ready, Tracks(), Tracks(), [new("0", "Title", true)], [], new(0, 0));
        Assert.Equal(capabilities, capabilities with { AudioTracks = Tracks(), SubtitleTracks = Tracks(), Titles = [new("0", "Title", true)], Chapters = [] });
        Assert.Equal(capabilities.GetHashCode(), (capabilities with { SubtitleTracks = Tracks() }).GetHashCode());
        Assert.NotEqual(capabilities, capabilities with { SubtitleTracks = [new("-1", "Disable", true, true), new("2", "English", false, false)] });
        Assert.NotEqual(capabilities, capabilities with { Delays = new(0, 100) });
    }

    [Fact]
    public void M3uParsingRejectsInvalidAndBoundedContent()
    {
        Assert.Equal(["first.mp4", "https://example.test/second.mp4"], M3uPlaylistDocument.Parse("#EXTM3U\n#EXTINF:1,title\nfirst.mp4\nhttps://example.test/second.mp4\n"));
        Assert.Throws<InvalidDataException>(() => M3uPlaylistDocument.Parse("bad\0entry"));
        Assert.Throws<InvalidDataException>(() => M3uPlaylistDocument.Write(["a\nb"]));
    }

    [Fact]
    public async Task FolderExpansionIsSortedBoundedAndCancellable()
    {
        string root = Path.Combine(Path.GetTempPath(), "vlc-avalonia-expansion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "b.mp4"), "x"); File.WriteAllText(Path.Combine(root, "a.mp4"), "x");
            MediaSourceExpander expander = new(new MediaSourceValidator(new SystemMediaFileSystem()));
            MediaSourceExpansion result = await expander.ExpandAsync(root, MediaSourceKind.Folder, 1);
            Assert.Single(result.Sources); Assert.True(result.WasBounded); Assert.EndsWith("a.mp4", result.Sources[0].CanonicalSource, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

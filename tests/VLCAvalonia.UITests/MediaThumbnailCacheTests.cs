using VLCAvalonia.Services;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class MediaThumbnailCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vlc-thumbnail-tests-" + Guid.NewGuid().ToString("N"));
    private string CacheDirectory => Path.Combine(_root, "cache");
    private string MakeMedia()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "video.mp4");
        File.WriteAllText(path, "fixture");
        return path;
    }

    [Fact]
    public async Task CoalescesCardsAndCancellationDoesNotCancelAnotherCard()
    {
        string path = MakeMedia();
        TaskCompletionSource<byte[]?> extraction = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        MediaThumbnailCache cache = new(CacheDirectory, _ => { Interlocked.Increment(ref calls); started.TrySetResult(); return extraction.Task; });
        using CancellationTokenSource cancelled = new();
        Task<byte[]?> first = cache.GetAsync(path, cancelled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<byte[]?> second = cache.GetAsync(new Uri(path).AbsoluteUri);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        extraction.SetResult([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PersistsAcrossInstancesAndReextractsWhenTheFileChanges()
    {
        string path = MakeMedia();
        MediaThumbnailCache cache = new(CacheDirectory, _ => Task.FromResult<byte[]?>([1, 2, 3]));
        Assert.NotNull(await cache.GetAsync(path));
        int calls = 0;
        MediaThumbnailCache restarted = new(CacheDirectory, _ => { calls++; return Task.FromResult<byte[]?>([4]); });
        Assert.Equal(new byte[] { 1, 2, 3 }, await restarted.GetAsync(path));
        Assert.Equal(0, calls);
        File.AppendAllText(path, "changed");
        Assert.Equal(new byte[] { 4 }, await restarted.GetAsync(path));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://example.invalid/movie.mp4")]
    [InlineData("file://server/share/movie.mp4")]
    [InlineData(@"\\server\share\movie.mp4")]
    [InlineData("missing-relative.mp4")]
    public async Task DoesNotSendRemoteOrInvalidSourcesToTheProvider(string source)
    {
        int calls = 0;
        MediaThumbnailCache cache = new(CacheDirectory, _ => { calls++; return Task.FromResult<byte[]?>([1]); });
        Assert.Null(await cache.GetAsync(source));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ProviderFailureLeavesAnEmptyResultWithoutBreakingBrowsing()
    {
        MediaThumbnailCache cache = new(CacheDirectory, _ => throw new IOException("No thumbnail handler"));
        Assert.Null(await cache.GetAsync(MakeMedia()));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

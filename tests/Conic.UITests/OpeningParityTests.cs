using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

/// <summary>The ways VLC 3 opens media that Conic follows: several files, several URLs, more protocols.</summary>
public sealed class OpeningParityTests
{
    [Fact]
    public async Task OpenFileWithSeveralFilesOpensThemAsOneQueue()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new FilesPicker(["a.mp4", "b.mkv", "c.mp3"]), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        vm.Registry.Execute(PlayerCommandId.OpenMedia);
        await WaitFor(() => transport.OpenedQueues.Count > 0);

        Assert.Equal(["a.mp4", "b.mkv", "c.mp3"], Assert.Single(transport.OpenedQueues));
    }

    [Fact]
    public async Task OpenFileWithOneFileOpensIt()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new FilesPicker(["a.mp4"]), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        vm.Registry.Execute(PlayerCommandId.OpenMedia);
        await WaitFor(() => transport.OpenedQueues.Count > 0);

        Assert.Equal(["a.mp4"], Assert.Single(transport.OpenedQueues));
    }

    [Fact]
    public void OpenNetworkStreamOpensOneUrlPerLine()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new FilesPicker([]), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        vm.UrlText = "https://example.com/a.mp4\r\n\r\n  rtsp://camera.local/live  \nudp://@:1234\n";
        vm.Registry.Execute(PlayerCommandId.SubmitUrl);

        Assert.Equal(["https://example.com/a.mp4", "rtsp://camera.local/live", "udp://@:1234"], Assert.Single(transport.OpenedQueues));
        Assert.False(vm.HasOpenError);
    }

    [Fact]
    public void ABadLineOpensNothingAndSaysWhichLine()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new FilesPicker([]), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        vm.UrlText = "https://example.com/a.mp4\ngopher://example.com/b";
        vm.Registry.Execute(PlayerCommandId.SubmitUrl);

        Assert.Empty(transport.OpenedQueues);
        Assert.StartsWith("Line 2: ", vm.OpenError);
    }

    [Fact]
    public void OneUrlOpensAsBefore()
    {
        QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new FilesPicker([]), () => { }, sourceValidator: new MediaSourceValidator(new ExistingFiles()));

        vm.UrlText = "smb://server/share/movie.mkv";
        vm.Registry.Execute(PlayerCommandId.SubmitUrl);

        Assert.Equal(["smb://server/share/movie.mkv"], Assert.Single(transport.OpenedQueues));
    }

    internal static async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FilesPicker(string[] files) : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult(files.FirstOrDefault());
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickMediaFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(files);
    }

    internal sealed class QueueTransport : IPlayerTransport, IQueueTransport
    {
        public PlaybackQueueState QueueState { get; private set; } = PlaybackQueueState.Initial;
        public event EventHandler<PlaybackQueueState>? QueueStateChanged;
        public List<IReadOnlyList<string>> OpenedQueues { get; } = [];
        public List<IReadOnlyList<string>> Enqueued { get; } = [];
        public void Publish(PlaybackQueueState state) { QueueState = state; QueueStateChanged?.Invoke(this, state); }
        public void Open(string source) { } public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) { } public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
        public void OpenQueue(IReadOnlyList<string> sources) => OpenedQueues.Add(sources.ToArray()); public void Enqueue(IEnumerable<string> sources) => Enqueued.Add(sources.ToArray()); public void SelectAndPlay(string token) { } public void Move(string token, int destinationIndex) { } public void Remove(string token) { } public void ClearQueue() { } public void Previous() { } public void Next() { } public void SetRepeatMode(QueueRepeatMode mode) { } public void SetOrder(QueueOrder order) { }
    }

    internal sealed class ExistingFiles : IMediaFileSystem { public bool FileExists(string path) => true; public bool DirectoryExists(string path) => false; public string GetFullPath(string path) => path; }
}

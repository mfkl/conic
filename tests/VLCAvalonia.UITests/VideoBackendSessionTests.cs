using Avalonia;
using Avalonia.Media;
using Player.Contracts;
using VLCAvalonia.Interop;
using VLCAvalonia.Video;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class VideoBackendSessionTests
{
    [Fact]
    public void QueuedNotificationsFromADetachedSessionCannotUpdateItsReplacement()
    {
        List<Action> dispatcher = [];
        List<string> notifications = [];
        using VideoBackendSession oldSession = new(new Backend());
        oldSession.PostNotification(dispatcher.Add, () => notifications.Add("old"));
        oldSession.Dispose();
        using VideoBackendSession currentSession = new(new Backend());
        currentSession.PostNotification(dispatcher.Add, () => notifications.Add("current"));
        foreach (Action callback in dispatcher) callback();
        Assert.Equal(["current"], notifications);
    }

    [Fact]
    public async Task DeviceSelectionCompletesBeforePlayerCreation()
    {
        Backend backend = new();
        using VideoBackendSession session = new(backend);
        Task<IMediaEngine?> initialization = session.InitializeAsync();
        Assert.Equal(0, backend.PlayersCreated);
        Assert.Same(initialization, session.InitializeAsync());

        backend.Ready.SetResult();
        Assert.Same(backend.Player, await initialization);
        Assert.Equal(1, backend.PlayersCreated);
    }

    [Fact]
    public async Task DetachDuringInitializationNeverStartsAPlayer()
    {
        Backend backend = new();
        VideoBackendSession session = new(backend);
        Task<IMediaEngine?> initialization = session.InitializeAsync();
        session.Dispose();
        backend.Ready.SetResult();

        Assert.Null(await initialization);
        Assert.Equal(0, backend.PlayersCreated);
        Assert.Equal(["backend"], backend.DisposalOrder);
        session.Dispose();
        Assert.Single(backend.DisposalOrder);
    }

    [Fact]
    public async Task ReattachmentOwnsADifferentBackendAndIgnoresOldInitialization()
    {
        Backend oldBackend = new();
        using VideoBackendSession oldSession = new(oldBackend);
        Task<IMediaEngine?> oldInitialization = oldSession.InitializeAsync();
        oldSession.Dispose();
        Backend currentBackend = new();
        using VideoBackendSession currentSession = new(currentBackend);
        Task<IMediaEngine?> currentInitialization = currentSession.InitializeAsync();
        currentBackend.Ready.SetResult();
        Assert.Same(currentBackend.Player, await currentInitialization);

        oldBackend.Ready.SetResult();
        Assert.Null(await oldInitialization);
        Assert.Equal(0, oldBackend.PlayersCreated);
        Assert.Empty(currentBackend.DisposalOrder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalStopsPlayerBeforeReleasingBackendEvenIfPlayerDisposalThrows(bool throws)
    {
        Backend backend = new();
        VideoBackendSession session = new(backend);
        backend.Player.ThrowOnDispose = throws;
        backend.Ready.SetResult();
        await session.InitializeAsync();

        if (throws) Assert.Throws<InvalidOperationException>(session.Dispose);
        else session.Dispose();
        session.Dispose();
        Assert.Equal(["player", "backend"], backend.DisposalOrder);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.InitializeAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedInitializationOrPlayerCreationReleasesBackend(bool failPlayerCreation)
    {
        Backend backend = new() { FailPlayerCreation = failPlayerCreation };
        using VideoBackendSession session = new(backend);
        Task<IMediaEngine?> initialization = session.InitializeAsync();
        if (failPlayerCreation) backend.Ready.SetResult();
        else backend.Ready.SetException(new InvalidOperationException("device selection failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => initialization);
        Assert.Equal(["backend"], backend.DisposalOrder);
    }

    private sealed class Backend : IVideoBackend
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> DisposalOrder { get; } = [];
        public Engine Player { get; }
        public Backend() => Player = new(DisposalOrder);
        public bool FailPlayerCreation { get; init; }
        public int PlayersCreated { get; private set; }
        public event Action<string>? StatusChanged { add { } remove { } }
        public event Action<ulong, ulong, string>? GpuFramePresented { add { } remove { } }
        public event Action? PresentationChanged { add { } remove { } }
        public VideoGeometry Geometry => default;
        public VideoSubtitle? Subtitle => null;
        public Size VideoSize => default;
        public bool HasCpuFrame => false;
        public bool HasGpuFrame => false;
        public Task InitializeAsync() => Ready.Task;
        public IMediaEngine CreatePlayer()
        {
            if (FailPlayerCreation) throw new InvalidOperationException("native startup failed");
            PlayersCreated++;
            return Player;
        }
        public Task PresentLatestFrameAsync() => Task.CompletedTask;
        public void UpdateLayout() { }
        public void Render(DrawingContext context) { }
        public void Dispose() => DisposalOrder.Add("backend");
    }

    private sealed class Engine(List<string> disposalOrder) : IMediaEngine
    {
        public bool ThrowOnDispose { get; set; }
        public MediaEngineState State => MediaEngineState.Initial;
        public event EventHandler<MediaEngineState>? StateChanged { add { } remove { } }
        public void Open(string source) { }
        public void TogglePause() { }
        public void Seek(double normalizedPosition) { }
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
        public void Stop() { }
        public void Dispose()
        {
            disposalOrder.Add("player");
            if (ThrowOnDispose) throw new InvalidOperationException("shutdown failed");
        }
    }
}

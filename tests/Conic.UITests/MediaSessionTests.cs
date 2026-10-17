using Conic.Services;
using Conic.ViewModels;
using Player.Application;
using Player.Contracts;
using Xunit;

namespace Conic.UITests;

public sealed class MediaSessionTests
{
    private DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PlayingMediaPublishesItsTitleStatusAndTimeline()
    {
        var (vm, _, session, backend) = Create();
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.25, "Sintel"));
        MediaSessionState state = backend.States[^1];
        Assert.Equal(MediaSessionStatus.Playing, state.Status);
        Assert.Equal("Sintel", state.Title);
        Assert.Equal(TimeSpan.FromSeconds(100), state.Duration);
        Assert.Equal(TimeSpan.FromSeconds(25), state.Position);
        Assert.True(state.CanSeek);
        Assert.Same(state, session.Published);
    }

    [Fact]
    public void SteadyPlaybackPublishesOnlyEveryFewSeconds()
    {
        var (vm, _, _, backend) = Create();
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.10));
        int published = backend.States.Count;
        for (int second = 1; second < 5; second++)
        {
            _now = _now.AddSeconds(1);
            vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.10 + second / 100.0));
        }
        Assert.Equal(published, backend.States.Count);
        _now = _now.AddSeconds(1);
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.15));
        Assert.Equal(published + 1, backend.States.Count);
        Assert.Equal(TimeSpan.FromSeconds(15), backend.States[^1].Position);
    }

    [Fact]
    public void ASeekOrAPauseIsPublishedAtOnce()
    {
        var (vm, _, _, backend) = Create();
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.10));
        _now = _now.AddSeconds(1);
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.80));
        Assert.Equal(TimeSpan.FromSeconds(80), backend.States[^1].Position);
        vm.ApplyEngineState(State(PlaybackStatus.Paused, 0.80));
        Assert.Equal(MediaSessionStatus.Paused, backend.States[^1].Status);
    }

    [Fact]
    public void NothingOpenIsClosed()
    {
        var (_, _, _, backend) = Create();
        Assert.Equal(MediaSessionState.Closed, backend.States[^1]);
        Assert.False(backend.States[^1].HasMedia);
    }

    [Fact]
    public void ButtonsRunThePlayerCommands()
    {
        var (vm, transport, session, _) = Create();
        vm.ApplyEngineState(State(PlaybackStatus.Paused, 0.25));
        session.Press(MediaSessionButton.Pause);
        Assert.Equal(0, transport.Toggles);
        session.Press(MediaSessionButton.Play);
        Assert.Equal(1, transport.Toggles);
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.25));
        session.Press(MediaSessionButton.Play);
        Assert.Equal(1, transport.Toggles);
        session.Press(MediaSessionButton.TogglePause);
        Assert.Equal(2, transport.Toggles);
        session.SeekTo(TimeSpan.FromSeconds(50));
        Assert.Equal([0.5], transport.Seeks);
        session.Press(MediaSessionButton.Stop);
        Assert.True(transport.Stopped);
    }

    [Fact]
    public void PreviousAndNextFollowTheQueue()
    {
        var (vm, transport, session, backend) = Create();
        QueueItem[] items = [new("a", "First", "a.mp4", 0, true), new("b", "Second", "b.mp4", 1, false)];
        transport.Publish(new PlaybackQueueState(1, 1, QueueLoadState.Ready, items, 0, QueueRepeatMode.None, QueueOrder.Normal));
        vm.ApplyEngineState(State(PlaybackStatus.Playing, 0.25));
        Assert.False(backend.States[^1].CanPrevious);
        Assert.True(backend.States[^1].CanNext);
        session.Press(MediaSessionButton.Previous);
        session.Press(MediaSessionButton.Next);
        Assert.Equal(["next"], transport.Calls);
    }

    private (PlayerPresentationViewModel, Transport, MediaSession, RecordingBackend) Create()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { });
        MediaSession session = new(vm, () => _now);
        RecordingBackend backend = new();
        session.Attach(backend);
        return (vm, transport, session, backend);
    }

    private static MediaEngineState State(PlaybackStatus status, double position, string title = "test") =>
        new(1, status, "test.mp4", position, TimeSpan.FromSeconds(position * 100), TimeSpan.FromSeconds(100), 0.5, true, null, Title: title);

    private sealed class RecordingBackend : IMediaSessionBackend
    {
        public List<MediaSessionState> States { get; } = [];
        public void Update(MediaSessionState state) => States.Add(state);
        public void Dispose() { }
    }

    private sealed class NoPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class Transport : IPlayerTransport, IQueueTransport
    {
        public int Toggles { get; private set; }
        public bool Stopped { get; private set; }
        public List<double> Seeks { get; } = [];
        public List<string> Calls { get; } = [];
        public PlaybackQueueState QueueState { get; private set; } = PlaybackQueueState.Initial;
        public event EventHandler<PlaybackQueueState>? QueueStateChanged;
        public void Publish(PlaybackQueueState state) { QueueState = state; QueueStateChanged?.Invoke(this, state); }
        public void Open(string source) { }
        public void TogglePause() => Toggles++;
        public void Stop() => Stopped = true;
        public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
        public void OpenQueue(IReadOnlyList<string> sources) { }
        public void Enqueue(IEnumerable<string> sources) { }
        public void SelectAndPlay(string token) { }
        public void Move(string token, int destinationIndex) { }
        public void Remove(string token) { }
        public void ClearQueue() { }
        public void Previous() => Calls.Add("previous");
        public void Next() => Calls.Add("next");
        public void SetRepeatMode(QueueRepeatMode mode) { }
        public void SetOrder(QueueOrder order) { }
    }
}

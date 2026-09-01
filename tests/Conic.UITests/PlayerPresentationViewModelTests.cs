using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

public sealed class PlayerPresentationViewModelTests
{
    [Fact]
    public void EnginePollingDoesNotOverwriteAnActiveSeekDrag()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel viewModel = CreateViewModel(transport);
        viewModel.ApplyEngineState(State(0.25, PlaybackStatus.Playing, canSeek: true));
        viewModel.BeginSeek();

        viewModel.Position = 0.75;
        viewModel.ApplyEngineState(State(0.30, PlaybackStatus.Playing, canSeek: true));

        Assert.Equal(0.75, viewModel.Position);
        Assert.Empty(transport.Seeks);

        viewModel.CommitSeek();

        Assert.Equal(new[] { 0.75 }, transport.Seeks);
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, false)]
    [InlineData(PlaybackStatus.Stopped, false)]
    public void EngineStateControlsThePlayPauseGlyphState(
        PlaybackStatus status,
        bool expectedIsPlaying)
    {
        PlayerPresentationViewModel viewModel = CreateViewModel(new RecordingTransport());

        viewModel.ApplyEngineState(State(0, status, canSeek: false));

        Assert.Equal(expectedIsPlaying, viewModel.IsPlaying);
    }

    [Fact]
    public void TransportCommandsReflectEngineStateAndDispatchSharedOperations()
    {
        RecordingTransport transport = new();
        PlayerPresentationViewModel viewModel = CreateViewModel(transport);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, canSeek: true) with
        {
            IsMuted = true, PlaybackRate = 1.5, Title = "movie"
        });

        viewModel.Registry.Execute(PlayerCommandId.SkipBackward);
        viewModel.Registry.Execute(PlayerCommandId.SkipForward);
        viewModel.Registry.Execute(PlayerCommandId.ToggleMute);
        viewModel.Registry.Execute(PlayerCommandId.SetPlaybackRate, 100d);

        Assert.Equal(new[] { TimeSpan.FromSeconds(-10), TimeSpan.FromSeconds(10) }, transport.Skips);
        Assert.False(transport.Muted);
        Assert.Equal(31.25, transport.Rate);
        Assert.Equal("movie", viewModel.MediaTitle);
    }

    [Fact]
    public void LiveSkipSettingsUpdateCommandsLabelsAndNotifications()
    {
        RecordingTransport transport = new();
        PlayerSettingsService settings = new(new SettingsStorage());
        PlayerPresentationViewModel viewModel = new(transport, new EmptyPicker(), () => { }, settings: settings);
        viewModel.ApplyEngineState(State(.5, PlaybackStatus.Playing, true));
        List<string?> changed = []; viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        settings.Update(s => s with { SkipBackwardSeconds = 4, SkipForwardSeconds = 17 });
        viewModel.Registry.Execute(PlayerCommandId.SkipBackward);
        viewModel.Registry.Execute(PlayerCommandId.SkipForward);
        Assert.Equal([TimeSpan.FromSeconds(-4), TimeSpan.FromSeconds(17)], transport.Skips);
        Assert.Equal("−4", viewModel.SkipBackwardLabel); Assert.Equal("+17", viewModel.SkipForwardLabel);
        Assert.Contains(nameof(PlayerPresentationViewModel.SkipBackwardLabel), changed);
    }

    private static PlayerPresentationViewModel CreateViewModel(
        RecordingTransport transport) => new(transport, new EmptyPicker(), () => { });

    private static MediaEngineState State(
        double position,
        PlaybackStatus status,
        bool canSeek) => new(
        1,
        status,
        "test.mp4",
        position,
        TimeSpan.FromSeconds(position * 100),
        TimeSpan.FromSeconds(100),
        0.5,
        canSeek,
        null);

    private sealed class RecordingTransport : IPlayerTransport
    {
        public List<double> Seeks { get; } = new();
        public List<TimeSpan> Skips { get; } = new();
        public bool Muted { get; private set; }
        public double Rate { get; private set; }
        public void Open(string source) { }
        public void TogglePause() { }
        public void Stop() { }
        public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) => Muted = muted;
        public void Skip(TimeSpan offset) => Skips.Add(offset);
        public void SetPlaybackRate(double playbackRate) => Rate = playbackRate;
    }

    private sealed class EmptyPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SettingsStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }
}

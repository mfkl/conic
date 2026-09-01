using Player.Contracts;
using Player.Domain;
using Xunit;

namespace Player.Engine.ContractTests;

public sealed class MediaEngineStateStoreTests
{
    [Fact]
    public void OpenStartsNewGenerationAndResetsMediaState()
    {
        MediaEngineStateStore store = new();

        long first = store.BeginOpen("first.mp4");
        store.Update(
            first,
            PlaybackStatus.Playing,
            0.75,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(40),
            0.5,
            true);
        long second = store.BeginOpen("second.mp4");

        Assert.Equal(first + 1, second);
        Assert.Equal(PlaybackStatus.Opening, store.State.Status);
        Assert.Equal("second.mp4", store.State.Source);
        Assert.Equal(0, store.State.Position);
        Assert.False(store.State.CanSeek);
    }

    [Fact]
    public void LateUpdatesFromPreviousMediaAreDiscarded()
    {
        MediaEngineStateStore store = new();
        long oldGeneration = store.BeginOpen("old.mp4");
        long currentGeneration = store.BeginOpen("current.mp4");

        store.Fault(oldGeneration, "late failure");

        Assert.Equal(currentGeneration, store.State.Generation);
        Assert.Equal(PlaybackStatus.Opening, store.State.Status);
        Assert.Null(store.State.Error);
    }

    [Fact]
    public void NativeValuesAreNormalizedBeforePublication()
    {
        MediaEngineStateStore store = new();
        long generation = store.BeginOpen("movie.mp4");

        store.Update(
            generation,
            PlaybackStatus.Playing,
            2,
            TimeSpan.FromSeconds(-1),
            TimeSpan.FromSeconds(-2),
            9,
            true);

        Assert.Equal(1, store.State.Position);
        Assert.Equal(TimeSpan.Zero, store.State.Elapsed);
        Assert.Equal(TimeSpan.Zero, store.State.Duration);
        Assert.Equal(1.25, store.State.Volume);
    }

    [Fact]
    public void FaultTransitionsCurrentGenerationAndDisablesSeeking()
    {
        MediaEngineStateStore store = new();
        long generation = store.BeginOpen("movie.mp4");
        store.Update(
            generation,
            PlaybackStatus.Playing,
            0.25,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(40),
            0.75,
            true);

        store.Fault(generation, "decoder failed");

        Assert.Equal(PlaybackStatus.Faulted, store.State.Status);
        Assert.Equal("decoder failed", store.State.Error);
        Assert.False(store.State.CanSeek);
    }

    [Fact]
    public void StopTransitionsCurrentGenerationAndClearsFault()
    {
        MediaEngineStateStore store = new();
        long generation = store.BeginOpen("movie.mp4");
        store.Fault(generation, "decoder failed");

        store.Stop(generation);

        Assert.Equal(PlaybackStatus.Stopped, store.State.Status);
        Assert.Null(store.State.Error);
        Assert.False(store.State.CanSeek);
    }

    [Fact]
    public void ReopenAfterStopStartsCleanGeneration()
    {
        MediaEngineStateStore store = new();
        long firstGeneration = store.BeginOpen("first.mp4");
        store.Update(
            firstGeneration,
            PlaybackStatus.Playing,
            0.5,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            0.6,
            true);
        store.Stop(firstGeneration);

        long reopenedGeneration = store.BeginOpen("first.mp4");

        Assert.Equal(firstGeneration + 1, reopenedGeneration);
        Assert.Equal(PlaybackStatus.Opening, store.State.Status);
        Assert.Equal("first.mp4", store.State.Source);
        Assert.Equal(0, store.State.Position);
        Assert.Equal(TimeSpan.Zero, store.State.Elapsed);
        Assert.Equal(TimeSpan.Zero, store.State.Duration);
        Assert.Equal(0.6, store.State.Volume);
        Assert.False(store.State.CanSeek);
        Assert.Null(store.State.Error);
    }
}

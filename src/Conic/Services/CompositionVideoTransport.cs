using Player.Application;
using Conic.Controls;

namespace Conic.Services;

internal sealed class CompositionVideoTransport : IPlayerTransport, IQueueTransport
{
    private readonly CompositionVideoSurface _surface;
    public CompositionVideoTransport(CompositionVideoSurface surface)
    {
        _surface = surface;
        _surface.QueueStateChanged += (_, state) => QueueStateChanged?.Invoke(this, state);
    }
    public PlaybackQueueState QueueState => _surface.QueueState;
    public event EventHandler<PlaybackQueueState>? QueueStateChanged;
    public void Open(string source) => _surface.MediaPath = source;

    public void TogglePause() => _surface.TogglePause();

    public void Stop() => _surface.Stop();

    public void Seek(double normalizedPosition) => _surface.Seek(normalizedPosition);

    public void SetVolume(double normalizedVolume) => _surface.SetVolume(normalizedVolume);

    public void SetMuted(bool muted) => _surface.SetMuted(muted);

    public void Skip(TimeSpan offset) => _surface.Skip(offset);

    public void SetPlaybackRate(double playbackRate) => _surface.SetPlaybackRate(playbackRate);
    public void OpenQueue(IReadOnlyList<string> sources) => _surface.OpenQueue(sources);
    public void Enqueue(IEnumerable<string> sources) => _surface.Enqueue(sources);
    public void SelectAndPlay(string token) => _surface.SelectAndPlay(token);
    public void Move(string token, int destinationIndex) => _surface.MoveQueueItem(token, destinationIndex);
    public void Remove(string token) => _surface.RemoveQueueItem(token);
    public void ClearQueue() => _surface.ClearQueue();
    public void Previous() => _surface.PreviousQueueItem();
    public void Next() => _surface.NextQueueItem();
    public void SetRepeatMode(QueueRepeatMode mode) => _surface.SetQueueRepeat(mode);
    public void SetOrder(QueueOrder order) => _surface.SetQueueOrder(order);
}

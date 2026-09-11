using Player.Application;
using VLCAvalonia.Controls;

namespace VLCAvalonia.Services;

internal sealed class CompositionVideoTransport : IPlayerTransport, IQueueTransport, IMediaCapabilitiesTransport, IMediaInformationTransport, IVideoAdjustmentTransport
{
    private readonly CompositionVideoSurface _surface;
    public CompositionVideoTransport(CompositionVideoSurface surface)
    {
        _surface = surface;
        _surface.QueueStateChanged += (_, state) => QueueStateChanged?.Invoke(this, state);
        _surface.MediaCapabilitiesChanged += (_, state) => MediaCapabilitiesChanged?.Invoke(this, state);
        _surface.MediaInformationChanged += (_, state) => MediaInformationChanged?.Invoke(this, state);
        _surface.VideoAdjustmentsChanged += (_, state) => VideoAdjustmentsChanged?.Invoke(this, state);
    }
    public PlaybackQueueState QueueState => _surface.QueueState;
    public event EventHandler<PlaybackQueueState>? QueueStateChanged;
    public MediaCapabilitiesState MediaCapabilities => _surface.MediaCapabilities;
    public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged;
    public MediaInformationState MediaInformation => _surface.MediaInformation;
    public VideoAdjustmentState VideoAdjustments => _surface.VideoAdjustments;
    public event EventHandler<VideoAdjustmentState>? VideoAdjustmentsChanged;
    public void SetVideoAdjustment(VideoAdjustmentState expected, string setting, string value) => _surface.SetVideoAdjustment(expected, setting, value);
    public event EventHandler<MediaInformationState>? MediaInformationChanged;
    public void RefreshMediaInformation() => _surface.RefreshMediaInformation();
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
    public void SelectTrack(MediaTrackKind kind, string id) => _surface.SelectTrack(kind, id);
    public void SelectTitle(string id) => _surface.SelectTitle(id);
    public void SelectChapter(string id) => _surface.SelectChapter(id);
    public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) => _surface.SetDelayMilliseconds(kind, milliseconds);
    public void ResetDelay(MediaTrackKind kind) => _surface.ResetDelay(kind);
    public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) => _surface.AttachExternalTrack(kind, path);
}

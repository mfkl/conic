namespace Player.Application;

/// <summary>Copied, engine-neutral media capabilities. Requests are advisory until a later readback changes this state.</summary>
public enum MediaCapabilityLoadState { Unavailable, Loading, Ready, Empty, Failed }
public enum MediaTrackKind { Audio, Subtitle }

/// <summary>IsDisabled identifies VLC's selectable "Disable" choice, not an unavailable command.</summary>
public sealed record MediaTrack(string Id, string Label, bool IsDisabled, bool IsSelected);
public sealed record MediaTitle(string Id, string Label, bool IsSelected);
public sealed record MediaChapter(string Id, string Label, bool IsSelected);
public sealed record MediaDelays(int AudioMilliseconds, int SubtitleMilliseconds);

public sealed record MediaCapabilitiesState(
    long QueueGeneration,
    long MediaGeneration,
    MediaCapabilityLoadState Status,
    IReadOnlyList<MediaTrack> AudioTracks,
    IReadOnlyList<MediaTrack> SubtitleTracks,
    IReadOnlyList<MediaTitle> Titles,
    IReadOnlyList<MediaChapter> Chapters,
    MediaDelays Delays,
    string? Error = null)
{
    public static MediaCapabilitiesState Initial { get; } = new(0, 0, MediaCapabilityLoadState.Unavailable, [], [], [], [], new(0, 0));

    // Lists compare by content: each poll builds new arrays, and an unchanged poll must not rebuild the views.
    public bool Equals(MediaCapabilitiesState? other) =>
        other is not null && QueueGeneration == other.QueueGeneration && MediaGeneration == other.MediaGeneration && Status == other.Status &&
        AudioTracks.SequenceEqual(other.AudioTracks) && SubtitleTracks.SequenceEqual(other.SubtitleTracks) &&
        Titles.SequenceEqual(other.Titles) && Chapters.SequenceEqual(other.Chapters) && Delays == other.Delays && Error == other.Error;

    public override int GetHashCode() => HashCode.Combine(QueueGeneration, MediaGeneration, Status, AudioTracks.Count, SubtitleTracks.Count, Titles.Count, Chapters.Count, Delays);
}

/// <summary>Result for external tracks. An accepted attachment is pending until a subsequent open; it is never a live-track claim.</summary>
public sealed record ExternalTrackAttachmentState(bool AcceptedIntoItem, bool PendingUntilNextOpen, bool ObservedLive, string? Error = null);

public interface IMediaCapabilitiesTransport
{
    MediaCapabilitiesState MediaCapabilities { get; }
    event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged;
    void SelectTrack(MediaTrackKind kind, string id);
    void SelectTitle(string id);
    void SelectChapter(string id);
    void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds);
    void ResetDelay(MediaTrackKind kind);
    ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path);
}

public static class MediaDelayPolicy
{
    /// <summary>Daily-use product limit, deliberately narrower than VLCLR's one-day safety ceiling.</summary>
    public const int MaximumMilliseconds = 10_000;
    public const int StepMilliseconds = 100;
    public static int Clamp(int milliseconds) => Math.Clamp(milliseconds, -MaximumMilliseconds, MaximumMilliseconds);
}

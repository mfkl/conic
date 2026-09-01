namespace Player.Application;

/// <summary>View-facing queue state. It intentionally contains no native VLC identity.</summary>
public enum QueueLoadState { Unavailable, Loading, Ready, Empty, Failed }
public enum QueueRepeatMode { None, CurrentItem, All, Inconsistent }
public enum QueueOrder { Normal, Random }

public sealed record QueueItem(string Token, string Name, string Source, int Index, bool IsCurrent);
public sealed record QueueMove(string Token, int DestinationIndex);

public sealed record PlaybackQueueState(
    long Generation,
    long Version,
    QueueLoadState Status,
    IReadOnlyList<QueueItem> Items,
    int CurrentIndex,
    QueueRepeatMode Repeat,
    QueueOrder Order,
    string? Error = null)
{
    public static PlaybackQueueState Initial { get; } = new(0, 0, QueueLoadState.Unavailable, [], -1, QueueRepeatMode.None, QueueOrder.Normal);
    public bool CanPrevious => Items.Count > 1 && CurrentIndex >= 0 && (Order == QueueOrder.Random || CurrentIndex > 0 || Repeat == QueueRepeatMode.All);
    public bool CanNext => Items.Count > 1 && CurrentIndex >= 0 && (Order == QueueOrder.Random || CurrentIndex < Items.Count - 1 || Repeat == QueueRepeatMode.All);
}

/// <summary>Optional queue capability for transports that have a live native playlist.</summary>
public interface IQueueTransport
{
    PlaybackQueueState QueueState { get; }
    event EventHandler<PlaybackQueueState>? QueueStateChanged;
    void OpenQueue(IReadOnlyList<string> sources);
    void Enqueue(IEnumerable<string> sources);
    void SelectAndPlay(string token);
    void Move(string token, int destinationIndex);
    void Remove(string token);
    void ClearQueue();
    void Previous();
    void Next();
    void SetRepeatMode(QueueRepeatMode mode);
    void SetOrder(QueueOrder order);
}

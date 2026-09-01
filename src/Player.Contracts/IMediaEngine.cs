namespace Player.Contracts;

public enum PlaybackStatus
{
    Closed,
    Opening,
    Playing,
    Paused,
    Stopped,
    Faulted,
}

public sealed record MediaEngineState(
    long Generation,
    PlaybackStatus Status,
    string? Source,
    double Position,
    TimeSpan Elapsed,
    TimeSpan Duration,
    double Volume,
    bool CanSeek,
    string? Error)
{
    public static MediaEngineState Initial { get; } = new(
        0,
        PlaybackStatus.Closed,
        null,
        0,
        TimeSpan.Zero,
        TimeSpan.Zero,
        1,
        false,
        null);
}

public interface IMediaEngine : IDisposable
{
    MediaEngineState State { get; }

    event EventHandler<MediaEngineState>? StateChanged;

    void Open(string source);

    void TogglePause();

    void Seek(double normalizedPosition);

    void SetVolume(double normalizedVolume);

    void Stop();
}

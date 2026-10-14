namespace Player.Application;

/// <summary>Engine-neutral operations required by the player shell.</summary>
public interface IPlayerTransport
{
    void Open(string source);

    void TogglePause();

    void Stop();

    void Seek(double normalizedPosition);

    void SetVolume(double normalizedVolume);

    void SetMuted(bool muted);

    void Skip(TimeSpan offset);

    void SetPlaybackRate(double playbackRate);
}

/// <summary>VLC 3's Next Frame and chapter steps, done by the engine itself.</summary>
public interface IPlaybackNavigationTransport
{
    /// <summary>Shows the next frame, pausing first if the media plays.</summary>
    void NextFrame();

    void NextChapter();

    /// <summary>Goes to the previous chapter, or to the start of this one once it has played a moment.</summary>
    void PreviousChapter();
}

/// <summary>VLC 3's Take Snapshot: the engine writes the picture to a file.</summary>
public interface IVideoSnapshotTransport
{
    /// <summary>False when nothing can take one now.</summary>
    bool RequestSnapshot();
}

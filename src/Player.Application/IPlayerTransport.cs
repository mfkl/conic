namespace Player.Application;

/// <summary>Engine-neutral operations required by the player shell.</summary>
public interface IPlayerTransport
{
    void Open(string source);

    void TogglePause();

    void Stop();

    void Seek(double normalizedPosition);

    void SetVolume(double normalizedVolume);
}

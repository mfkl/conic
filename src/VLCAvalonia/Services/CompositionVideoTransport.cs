using Player.Application;
using VLCAvalonia.Controls;

namespace VLCAvalonia.Services;

internal sealed class CompositionVideoTransport(CompositionVideoSurface surface) : IPlayerTransport
{
    public void Open(string source) => surface.MediaPath = source;

    public void TogglePause() => surface.TogglePause();

    public void Stop() => surface.Stop();

    public void Seek(double normalizedPosition) => surface.Seek(normalizedPosition);

    public void SetVolume(double normalizedVolume) => surface.SetVolume(normalizedVolume);

    public void SetMuted(bool muted) => surface.SetMuted(muted);

    public void Skip(TimeSpan offset) => surface.Skip(offset);

    public void SetPlaybackRate(double playbackRate) => surface.SetPlaybackRate(playbackRate);
}

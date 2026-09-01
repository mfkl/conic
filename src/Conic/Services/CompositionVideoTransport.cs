using Player.Application;
using Conic.Controls;

namespace Conic.Services;

internal sealed class CompositionVideoTransport(CompositionVideoSurface surface) : IPlayerTransport
{
    public void Open(string source) => surface.MediaPath = source;

    public void TogglePause() => surface.TogglePause();

    public void Stop() => surface.Stop();

    public void Seek(double normalizedPosition) => surface.Seek(normalizedPosition);

    public void SetVolume(double normalizedVolume) => surface.SetVolume(normalizedVolume);
}

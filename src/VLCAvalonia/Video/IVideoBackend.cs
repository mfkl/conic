using Avalonia;
using Avalonia.Media;
using Player.Contracts;
using VLCAvalonia.Interop;

namespace VLCAvalonia.Video;

/// <summary>
/// One attachment's native video transport and compositor resources. Calls and
/// notifications run on the UI thread. Native handles stay inside the backend.
/// InitializeAsync must finish device selection before CreatePlayer is called.
/// </summary>
internal interface IVideoBackend : IDisposable
{
    event Action<string>? StatusChanged;
    event Action<ulong, ulong, string>? GpuFramePresented;
    VideoGeometry Geometry { get; }
    Size VideoSize { get; }
    bool HasCpuFrame { get; }
    bool HasGpuFrame { get; }
    Task InitializeAsync();
    IMediaEngine CreatePlayer(IReadOnlyList<string> audioStartupArguments);
    Task PresentLatestFrameAsync();
    void UpdateLayout();
    void Render(DrawingContext context);
}

internal readonly record struct VideoBackendOptions(
    bool ForceUnavailableAdapter = false,
    bool ProbeSharedTexture = false);

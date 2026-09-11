using Player.Contracts;

namespace VLCAvalonia.Video;

/// <summary>
/// Owns one attachment. Device selection precedes player creation; disposal
/// during asynchronous initialization prevents a detached surface starting VLC.
/// Like the control, this owner is used on the UI thread.
/// </summary>
internal sealed class VideoBackendSession(IVideoBackend backend) : IDisposable
{
    private bool _disposed;
    private Task<IMediaEngine?>? _initialization;
    private IMediaEngine? _player;
    public IVideoBackend Backend { get; } = backend;

    public void PostNotification(Action<Action> post, Action notification) =>
        post(() => { if (!_disposed) notification(); });

    public Task<IMediaEngine?> InitializeAsync(IReadOnlyList<string> audioStartupArguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _initialization ??= InitializeCoreAsync(audioStartupArguments.ToArray());
    }

    private async Task<IMediaEngine?> InitializeCoreAsync(IReadOnlyList<string> audioStartupArguments)
    {
        try
        {
            await Backend.InitializeAsync();
            if (_disposed) return null;
            return _player = Backend.CreatePlayer(audioStartupArguments);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _player?.Dispose(); }
        finally
        {
            _player = null;
            Backend.Dispose();
        }
    }
}

using Player.Contracts;

namespace Player.Domain;

public sealed class MediaEngineStateStore
{
    private readonly object _gate = new();
    private MediaEngineState _state = MediaEngineState.Initial;

    public MediaEngineState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public event EventHandler<MediaEngineState>? Changed;

    public long BeginOpen(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        MediaEngineState next;
        lock (_gate)
        {
            next = _state with
            {
                Generation = _state.Generation + 1,
                Status = PlaybackStatus.Opening,
                Source = source,
                Position = 0,
                Elapsed = TimeSpan.Zero,
                Duration = TimeSpan.Zero,
                CanSeek = false,
                Error = null,
                Title = Path.GetFileNameWithoutExtension(source),
            };
            _state = next;
        }
        Changed?.Invoke(this, next);
        return next.Generation;
    }

    public long BeginEmpty()
    {
        MediaEngineState next;
        lock (_gate)
        {
            next = _state with
            {
                Generation = _state.Generation + 1,
                Status = PlaybackStatus.Stopped,
                Source = null,
                Position = 0,
                Elapsed = TimeSpan.Zero,
                Duration = TimeSpan.Zero,
                CanSeek = false,
                Error = null,
                Title = null,
            };
            _state = next;
        }
        Changed?.Invoke(this, next);
        return next.Generation;
    }

    public void Update(
        long generation,
        PlaybackStatus status,
        double position,
        TimeSpan elapsed,
        TimeSpan duration,
        double volume,
        bool canSeek,
        string? error = null,
        bool isMuted = false,
        double playbackRate = 1,
        string? title = null)
    {
        MediaEngineState? next = null;
        lock (_gate)
        {
            if (_state.Generation != generation)
                return;

            next = _state with
            {
                Status = status,
                Position = Math.Clamp(position, 0, 1),
                Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
                Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
                Volume = Math.Clamp(volume, 0, 2),
                CanSeek = canSeek,
                Error = error,
                IsMuted = isMuted,
                PlaybackRate = double.IsFinite(playbackRate)
                    ? Math.Clamp(playbackRate, 1d / 32, 31.25)
                    : _state.PlaybackRate,
                Title = title ?? _state.Title,
            };
            _state = next;
        }
        Changed?.Invoke(this, next);
    }

    public void Stop(long generation)
    {
        MediaEngineState current = State;
        Update(
            generation,
            PlaybackStatus.Stopped,
            current.Position,
            current.Elapsed,
            current.Duration,
            current.Volume,
            false);
    }

    public void Fault(long generation, string error)
    {
        MediaEngineState current = State;
        Update(
            generation,
            PlaybackStatus.Faulted,
            current.Position,
            current.Elapsed,
            current.Duration,
            current.Volume,
            false,
            error);
    }
}

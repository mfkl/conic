using System.ComponentModel;
using Player.Application;
using Player.Contracts;
using Conic.ViewModels;

namespace Conic.Services;

internal enum MediaSessionStatus { Closed, Opening, Playing, Paused, Stopped }

internal enum MediaSessionButton { Play, Pause, TogglePause, Stop, Previous, Next }

internal sealed record MediaSessionState(
    MediaSessionStatus Status,
    string Title,
    string Subtitle,
    TimeSpan Duration,
    TimeSpan Position,
    double Rate,
    bool CanSeek,
    bool CanPrevious,
    bool CanNext)
{
    public static MediaSessionState Closed { get; } = new(MediaSessionStatus.Closed, string.Empty, string.Empty, TimeSpan.Zero, TimeSpan.Zero, 1, false, false, false);

    public bool HasMedia => Status != MediaSessionStatus.Closed;
}

internal interface IMediaSessionBackend : IDisposable
{
    void Update(MediaSessionState state);
}

internal sealed class MediaSession : IDisposable
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Drift = TimeSpan.FromSeconds(2);
    private static readonly HashSet<string> Watched =
    [
        nameof(PlayerPresentationViewModel.IsPlaying),
        nameof(PlayerPresentationViewModel.PlaybackStatus),
        nameof(PlayerPresentationViewModel.MediaTitle),
        nameof(PlayerPresentationViewModel.NowPlayingFolder),
        nameof(PlayerPresentationViewModel.Position),
        nameof(PlayerPresentationViewModel.DurationText),
        nameof(PlayerPresentationViewModel.PlaybackRate),
        nameof(PlayerPresentationViewModel.CanSeek),
        nameof(PlayerPresentationViewModel.QueueState),
    ];

    private readonly PlayerPresentationViewModel _presentation;
    private readonly Func<DateTime> _clock;
    private readonly List<IMediaSessionBackend> _backends = [];
    private DateTime _publishedAt;

    public MediaSession(PlayerPresentationViewModel presentation, Func<DateTime>? clock = null)
    {
        _presentation = presentation;
        _clock = clock ?? (() => DateTime.UtcNow);
        Published = Capture();
        _publishedAt = _clock();
        _presentation.PropertyChanged += OnPresentationChanged;
    }

    public MediaSessionState Published { get; private set; }

    public void Attach(IMediaSessionBackend? backend)
    {
        if (backend is null) return;
        _backends.Add(backend);
        backend.Update(Published);
    }

    public MediaSessionState Capture()
    {
        MediaSessionStatus status = _presentation.PlaybackStatus switch
        {
            PlaybackStatus.Opening => MediaSessionStatus.Opening,
            PlaybackStatus.Playing => MediaSessionStatus.Playing,
            PlaybackStatus.Paused => MediaSessionStatus.Paused,
            PlaybackStatus.Stopped when _presentation.HasPlayableMedia => MediaSessionStatus.Stopped,
            _ => MediaSessionStatus.Closed,
        };
        if (status == MediaSessionStatus.Closed) return MediaSessionState.Closed;
        TimeSpan duration = _presentation.Duration > TimeSpan.Zero ? _presentation.Duration : TimeSpan.Zero;
        IPlayerCommandRegistry registry = _presentation.Registry;
        return new(
            status,
            _presentation.MediaTitle,
            _presentation.NowPlayingFolder,
            duration,
            duration * Math.Clamp(_presentation.Position, 0, 1),
            _presentation.PlaybackRate > 0 ? _presentation.PlaybackRate : 1,
            _presentation.CanSeek && duration > TimeSpan.Zero,
            registry.CanExecute(PlayerCommandId.QueuePrevious),
            registry.CanExecute(PlayerCommandId.QueueNext));
    }

    public void Publish(bool force = false)
    {
        MediaSessionState state = Capture();
        DateTime now = _clock();
        if (!force && !NeedsPublish(state, now)) return;
        Published = state;
        _publishedAt = now;
        foreach (IMediaSessionBackend backend in _backends) backend.Update(state);
    }

    public void Press(MediaSessionButton button)
    {
        IPlayerCommandRegistry registry = _presentation.Registry;
        PlayerCommandId? command = button switch
        {
            MediaSessionButton.Play when !_presentation.IsPlaying => PlayerCommandId.TogglePause,
            MediaSessionButton.Pause when _presentation.IsPlaying => PlayerCommandId.TogglePause,
            MediaSessionButton.TogglePause => PlayerCommandId.TogglePause,
            MediaSessionButton.Stop => PlayerCommandId.Stop,
            MediaSessionButton.Previous => PlayerCommandId.QueuePrevious,
            MediaSessionButton.Next => PlayerCommandId.QueueNext,
            _ => null,
        };
        if (command is { } id && registry.CanExecute(id)) registry.Execute(id);
    }

    public void SeekTo(TimeSpan position)
    {
        TimeSpan duration = _presentation.Duration;
        if (!_presentation.CanSeek || duration <= TimeSpan.Zero) return;
        double fraction = Math.Clamp(position / duration, 0, 1);
        if (_presentation.Registry.CanExecute(PlayerCommandId.Seek, fraction)) _presentation.Registry.Execute(PlayerCommandId.Seek, fraction);
    }

    public void Dispose()
    {
        _presentation.PropertyChanged -= OnPresentationChanged;
        foreach (IMediaSessionBackend backend in _backends) backend.Dispose();
        _backends.Clear();
    }

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && Watched.Contains(name)) Publish();
    }

    private bool NeedsPublish(MediaSessionState state, DateTime now)
    {
        MediaSessionState published = Published;
        if (state with { Position = TimeSpan.Zero } != published with { Position = TimeSpan.Zero }) return true;
        if (state.Status != MediaSessionStatus.Playing) return state.Position != published.Position;
        TimeSpan expected = published.Position + (now - _publishedAt) * published.Rate;
        return (state.Position - expected).Duration() > Drift || now - _publishedAt >= Refresh;
    }
}

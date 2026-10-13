using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Interop;
using VLCAvalonia.Video;

namespace VLCAvalonia.Controls;

public sealed class CompositionVideoSurface : Control, IDisposable
{
    public static readonly StyledProperty<string?> MediaPathProperty =
        AvaloniaProperty.Register<CompositionVideoSurface, string?>(
            nameof(MediaPath));

    private readonly DispatcherTimer _frameTimer;
    private VideoBackendSession? _session;
    private IVideoBackend? Backend => _session?.Backend;
    private IMediaEngine? _player;
    private Action? _unsubscribePlayerEvents;
    internal IMediaEngine? Player => _player;
    internal VideoGeometry GeometryForSmoke => Backend?.Geometry ?? default;
    internal bool HasCpuFrameForSmoke => Backend?.HasCpuFrame == true;
    internal bool HasGpuFrameForSmoke => Backend?.HasGpuFrame == true;
    internal VideoSubtitle? Subtitle => Backend?.Geometry.Active != 0 ? Backend?.Subtitle : null;
    /// <summary>Raised when the subtitle overlay or the video placement changes.</summary>
    internal event Action? PresentationChanged;
    internal VideoSurfaceLayout CurrentVideoLayout => VideoSurfaceLayout.Calculate(
        Bounds.Size, Backend?.VideoSize ?? default, GeometryForSmoke,
        TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    private bool _attached;
    private IReadOnlyList<string>? _pendingQueueSources;
    private IReadOnlyList<string> _audioStartupArguments = [];
    private double _openPlaybackRate = 1;

    /// <summary>Default speed for the next open (<see cref="VlcCorePlayer.OpenPlaybackRate"/>).</summary>
    internal double OpenPlaybackRate
    {
        get => _openPlaybackRate;
        set
        {
            _openPlaybackRate = value;
            if (_player is VlcCorePlayer core) core.OpenPlaybackRate = value;
        }
    }
    internal IReadOnlyList<string> AudioStartupArguments
    {
        get => _audioStartupArguments;
        set => _audioStartupArguments = Array.AsReadOnly((value ?? []).ToArray());
    }

    internal bool ForceUnavailableAdapterForSmoke { get; set; }

    internal bool ProbeSharedTextureForSmoke { get; set; }

    public event Action<string>? StatusChanged;
    public event EventHandler<MediaEngineState>? EngineStateChanged;
    public event EventHandler<PlaybackQueueState>? QueueStateChanged;
    public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged;
    public event EventHandler<MediaInformationState>? MediaInformationChanged;
    public event EventHandler<VideoAdjustmentState>? VideoAdjustmentsChanged;
    public VideoAdjustmentState VideoAdjustments => (_player as IVideoAdjustmentTransport)?.VideoAdjustments ?? VideoAdjustmentState.Unavailable;
    public void SetVideoAdjustment(VideoAdjustmentState expected, string setting, string value) => (_player as IVideoAdjustmentTransport)?.SetVideoAdjustment(expected, setting, value);
    public MediaInformationState MediaInformation => (_player as IMediaInformationTransport)?.MediaInformation ?? MediaInformationState.Unavailable;
    public void RefreshMediaInformation() => (_player as IMediaInformationTransport)?.RefreshMediaInformation();
    internal event Action<ulong, ulong, string>? GPUFramePresentedForSmoke;

    public string? MediaPath
    {
        get => GetValue(MediaPathProperty);
        set => SetValue(MediaPathProperty, value);
    }

    public CompositionVideoSurface()
    {
        ClipToBounds = true;
        MediaPathProperty.Changed.AddClassHandler<CompositionVideoSurface>(
            static (surface, args) =>
                surface.OnMediaPathChanged(
                    ((AvaloniaPropertyChangedEventArgs<string?>)args)
                    .NewValue.Value));

        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _frameTimer.Tick += async (_, _) =>
            await PresentLatestFrameAsync();
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (!NativePlaybackSupport.IsAvailable)
        {
            ReportStatus(NativePlaybackSupport.CurrentUnavailableReason!);
            return;
        }
        try
        {
            _session = new VideoBackendSession(VideoBackendFactory.Create(this, new VideoBackendOptions(
                ForceUnavailableAdapterForSmoke, ProbeSharedTextureForSmoke)));
            _session.Backend.StatusChanged += ReportStatus;
            _session.Backend.GpuFramePresented += OnGpuFramePresented;
            _session.Backend.PresentationChanged += OnPresentationChanged;
            _ = InitializeBackendAsync(_session);
            _frameTimer.Start();
        }
        catch (Exception exception)
        {
            ReportStatus(exception.Message);
        }
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameTimer.Stop();
        DisposePipeline();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMediaPathChanged(string? path)
    {
        if (_attached && _player is not null &&
            !string.IsNullOrWhiteSpace(path))
        {
            if (_pendingQueueSources is { } sources && _player is IQueueTransport queue)
            {
                _pendingQueueSources = null;
                queue.OpenQueue(sources);
            }
            else _player.Open(path);
        }
    }

    public void TogglePause() => _player?.TogglePause();

    public void Seek(double normalizedPosition) =>
        _player?.Seek(normalizedPosition);

    public void SetVolume(double normalizedVolume) =>
        _player?.SetVolume(normalizedVolume);

    public void SetMuted(bool muted) => _player?.SetMuted(muted);

    public void Skip(TimeSpan offset) => _player?.Skip(offset);

    public void SetPlaybackRate(double playbackRate) => _player?.SetPlaybackRate(playbackRate);

    public void Stop() => _player?.Stop();

    public PlaybackQueueState QueueState => (_player as IQueueTransport)?.QueueState ?? PlaybackQueueState.Initial;
    public MediaCapabilitiesState MediaCapabilities => (_player as IMediaCapabilitiesTransport)?.MediaCapabilities ?? MediaCapabilitiesState.Initial;
    public void OpenQueue(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0) return;
        if (_player is IQueueTransport queue) queue.OpenQueue(sources);
        else { _pendingQueueSources = sources.ToArray(); MediaPath = sources[0]; }
    }
    public void Enqueue(IEnumerable<string> sources) => (_player as IQueueTransport)?.Enqueue(sources);
    public void SelectAndPlay(string token) => (_player as IQueueTransport)?.SelectAndPlay(token);
    public void MoveQueueItem(string token, int destination) => (_player as IQueueTransport)?.Move(token, destination);
    public void RemoveQueueItem(string token) => (_player as IQueueTransport)?.Remove(token);
    public void ClearQueue() => (_player as IQueueTransport)?.ClearQueue();
    public void PreviousQueueItem() => (_player as IQueueTransport)?.Previous();
    public void NextQueueItem() => (_player as IQueueTransport)?.Next();
    public void SetQueueRepeat(QueueRepeatMode mode) => (_player as IQueueTransport)?.SetRepeatMode(mode);
    public void SetQueueOrder(QueueOrder order) => (_player as IQueueTransport)?.SetOrder(order);
    public void SelectTrack(MediaTrackKind kind, string id) => (_player as IMediaCapabilitiesTransport)?.SelectTrack(kind, id);
    public void SelectTitle(string id) => (_player as IMediaCapabilitiesTransport)?.SelectTitle(id);
    public void SelectChapter(string id) => (_player as IMediaCapabilitiesTransport)?.SelectChapter(id);
    public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) => (_player as IMediaCapabilitiesTransport)?.SetDelayMilliseconds(kind, milliseconds);
    public void ResetDelay(MediaTrackKind kind) => (_player as IMediaCapabilitiesTransport)?.ResetDelay(kind);
    public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) => (_player as IMediaCapabilitiesTransport)?.AttachExternalTrack(kind, path) ?? new(false, false, false, "Media controls are unavailable.");

    internal void ReopenForSmoke()
    {
        if (_player is null || string.IsNullOrWhiteSpace(MediaPath))
            throw new InvalidOperationException(
                "The smoke player has no media to reopen.");
        _player.Open(MediaPath);
    }

    private void PublishEngineState(MediaEngineState state)
    {
        EngineStateChanged?.Invoke(this, state);
        string status = state.Status switch
        {
            PlaybackStatus.Faulted => state.Error ?? "Playback failed",
            PlaybackStatus.Opening => "Opening media",
            PlaybackStatus.Playing => "Playing",
            PlaybackStatus.Paused => "Paused",
            PlaybackStatus.Stopped => "Stopped",
            _ => "Ready",
        };
        StatusChanged?.Invoke(status);
    }

    private async Task InitializeBackendAsync(VideoBackendSession session)
    {
        try
        {
            IMediaEngine? player = await session.InitializeAsync(_audioStartupArguments);
            if (_attached && ReferenceEquals(_session, session) && player is not null)
                ConnectPlayer(player, session);
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_session, session))
                ReportStatus($"Video backend initialization failed: {exception.Message}");
        }
    }

    private Task PresentLatestFrameAsync() =>
        Backend?.PresentLatestFrameAsync() ?? Task.CompletedTask;

    private void OnPresentationChanged() => PresentationChanged?.Invoke();

    private void OnGpuFramePresented(ulong deviceGeneration, ulong resourceGeneration, string device) =>
        GPUFramePresentedForSmoke?.Invoke(deviceGeneration, resourceGeneration, device);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, Bounds);
        Backend?.Render(context);
    }

    private void ConnectPlayer(IMediaEngine player, VideoBackendSession session)
    {
        void Post(Action notification) => session.PostNotification(
            action => Dispatcher.UIThread.Post(action), notification);
        void StateChanged(object? sender, MediaEngineState state) => Post(() => PublishEngineState(state));
        void QueueChanged(object? sender, PlaybackQueueState state) => Post(() => QueueStateChanged?.Invoke(this, state));
        void CapabilitiesChanged(object? sender, MediaCapabilitiesState state) => Post(() => MediaCapabilitiesChanged?.Invoke(this, state));
        void InformationChanged(object? sender, MediaInformationState state) => Post(() => MediaInformationChanged?.Invoke(this, state));
        void VideoChanged(object? sender, VideoAdjustmentState state) => Post(() => VideoAdjustmentsChanged?.Invoke(this, state));

        // Capture the subscription's session. IMediaEngine implementations need
        // not use the engine itself as the event sender, and old callbacks may
        // already be queued when the control detaches.
        _unsubscribePlayerEvents = () =>
        {
            player.StateChanged -= StateChanged;
            if (player is IQueueTransport queue) queue.QueueStateChanged -= QueueChanged;
            if (player is IMediaCapabilitiesTransport capabilities) capabilities.MediaCapabilitiesChanged -= CapabilitiesChanged;
            if (player is IMediaInformationTransport information) information.MediaInformationChanged -= InformationChanged;
            if (player is IVideoAdjustmentTransport video) video.VideoAdjustmentsChanged -= VideoChanged;
        };
        try
        {
            _player = player;
            if (player is VlcCorePlayer core) core.OpenPlaybackRate = _openPlaybackRate;
            _player.StateChanged += StateChanged;
            if (_player is IQueueTransport queue)
                queue.QueueStateChanged += QueueChanged;
            if (_player is IMediaCapabilitiesTransport capabilities)
                capabilities.MediaCapabilitiesChanged += CapabilitiesChanged;
            if (_player is IMediaInformationTransport information)
                information.MediaInformationChanged += InformationChanged;
            if (_player is IVideoAdjustmentTransport video)
                video.VideoAdjustmentsChanged += VideoChanged;
            if (_pendingQueueSources is { } pending && _player is IQueueTransport pendingQueue)
            {
                _pendingQueueSources = null;
                pendingQueue.OpenQueue(pending);
            }
            else if (!string.IsNullOrWhiteSpace(MediaPath)) _player.Open(MediaPath);
        }
        catch (Exception exception)
        {
            ReportStatus($"VLC startup failed: {exception.Message}");
        }
    }

    private void ReportStatus(string status)
    {
        VideoBackendSession? session = _session;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_session, session)) StatusChanged?.Invoke(status);
        });
    }

    private void DisposePipeline()
    {
        _unsubscribePlayerEvents?.Invoke();
        _unsubscribePlayerEvents = null;
        _player = null;
        if (_session is not null)
        {
            VideoBackendSession session = _session;
            _session = null;
            session.Backend.StatusChanged -= ReportStatus;
            session.Backend.GpuFramePresented -= OnGpuFramePresented;
            session.Backend.PresentationChanged -= OnPresentationChanged;
            session.Dispose();
            PresentationChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _attached = false;
        _frameTimer.Stop();
        DisposePipeline();
        GC.SuppressFinalize(this);
    }
}

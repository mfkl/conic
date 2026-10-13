using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Collections.Concurrent;
using Player.Contracts;
using Player.Domain;
using Player.Application;
using VLCLR;
using VLCLR.Native;

namespace VLCAvalonia.Interop;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class VlcCorePlayer : IMediaEngine, IQueueTransport, IMediaCapabilitiesTransport, IMediaInformationTransport, IVideoAdjustmentTransport, IVLCPlaylistQueueSmokeAdapter, IVLCMediaCapabilitySmokeAdapter
{
    /// <summary>The plugin's D3D11 composition video output.</summary>
    public const string DefaultVideoOutput = "vlc_avalonia";
    private const int PlaylistStopped = 0;
    private const int PlaylistRunning = 1;
    private const int PlaylistPaused = 2;
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly object _disposeGate = new();
    private readonly MediaEngineStateStore _states = new();
    private readonly VlcControlHost _controlHost;
    private readonly string _videoOutput;
    private readonly bool _hardwareDecoding;
    private readonly bool _verboseLogging;
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private nint _playlist;
    private VLCPlaylistQueue? _queue;
    private PlaybackQueueState _queueState = PlaybackQueueState.Initial;
    private MediaCapabilitiesState _mediaCapabilities = MediaCapabilitiesState.Initial;
    private readonly VLCMediaCapabilities _mediaControls;
    private readonly VLCPlayerCapabilities _playerCapabilities;
    private MediaInformationState _mediaInformation = MediaInformationState.Unavailable;
    private readonly ConcurrentQueue<Action> _mediaRequests = new();
    private QueueRepeatMode _requestedRepeat = QueueRepeatMode.None;
    private QueueOrder _requestedOrder = QueueOrder.Normal;
    private bool _tearingDown;
    private long _generation;
    private long _coreGeneration;
    private long _mediaGeneration;
    // This is an identity token only. It is never dereferenced or passed back to VLC after its hold is released.
    private nint _lastObservedInputIdentity;
    private int _disposed;

    /// <param name="videoOutput">VLC <c>--vout</c> module. Windows and macOS implement
    /// <see cref="DefaultVideoOutput"/>; headless engine checks pass <c>dummy</c>.</param>
    public VlcCorePlayer(bool hardwareDecoding = true, bool verboseLogging = false, string videoOutput = DefaultVideoOutput)
    {
        NativeEngineSupport.RequireAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(videoOutput);
        _hardwareDecoding = hardwareDecoding;
        _verboseLogging = verboseLogging;
        _videoOutput = videoOutput;
        _controlHost = new VlcControlHost();
        _controlHost.Connect();
        _mediaControls = new VLCMediaCapabilities(TryGetCurrentInputLease);
        _playerCapabilities = new VLCPlayerCapabilities(TryGetCurrentInputLease);
        _videoControls = new VLCVideoPresentation(TryGetCurrentInputLease);
        _states.Changed += (_, state) =>
        {
            if (state.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused))
            {
                var unavailable = VideoAdjustmentState.Unavailable with { QueueGeneration = state.Generation };
                lock (_gate) _videoAdjustments = unavailable;
                VideoAdjustmentsChanged?.Invoke(this, unavailable);
            }
        };
    }

    public MediaEngineState State => _states.State;

    /// <summary>
    /// The speed each media item starts at from the next open on (Settings ›
    /// Playback › Default speed). Every open starts a new core, and VLC's
    /// playlist gives the rate to each input it creates, so the queue moving
    /// on, an item starting over and Play after the end start at it too. The
    /// speed menu changes only the input that is playing.
    /// </summary>
    public double OpenPlaybackRate { get; set; } = 1;

    /// <summary>The core option for <see cref="OpenPlaybackRate"/>; none at 1× or out of VLC's range.</summary>
    internal static string[] RateArguments(double rate) =>
        double.IsFinite(rate) && Math.Abs(rate - 1) > .001 &&
        rate >= VLCTransportControl.MinimumPlaybackRate && rate <= VLCTransportControl.MaximumPlaybackRate
            ? [$"--rate={rate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"]
            : [];

    /// <summary>
    /// The settings' libVLC options for the next core (<see cref="PlayerEngineOptions"/>).
    /// They follow the built-in options, so one of them (such as
    /// <c>--avcodec-hw</c>) replaces the default the constructor chose.
    /// </summary>
    public IReadOnlyList<string> OpenArguments
    {
        get { lock (_gate) return _openArguments; }
        set { string[] copy = (value ?? []).ToArray(); lock (_gate) _openArguments = copy; }
    }
    private IReadOnlyList<string> _openArguments = [];

    /// <summary>
    /// The repeat and shuffle modes new queues start with (Settings ›
    /// Playback › Order). Each core takes them when it starts; a queue that
    /// is playing takes them at once.
    /// </summary>
    internal void RequestQueueModes(QueueRepeatMode repeat, QueueOrder order)
    {
        if (repeat == QueueRepeatMode.Inconsistent) return;
        long generation;
        bool live;
        lock (_gate)
        {
            if (_requestedRepeat == repeat && _requestedOrder == order) return;
            _requestedRepeat = repeat;
            _requestedOrder = order;
            generation = _generation;
            live = _queue is not null && !_tearingDown;
        }
        if (live) ApplyRequestedModes(generation);
    }

    public event EventHandler<MediaEngineState>? StateChanged
    {
        add => _states.Changed += value;
        remove => _states.Changed -= value;
    }

    public PlaybackQueueState QueueState
    {
        get { lock (_gate) return _queueState; }
    }

    public event EventHandler<PlaybackQueueState>? QueueStateChanged;

    public MediaCapabilitiesState MediaCapabilities { get { lock (_gate) return _mediaCapabilities; } }
    public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged;
    public MediaInformationState MediaInformation { get { lock (_gate) return _mediaInformation; } }
    public event EventHandler<MediaInformationState>? MediaInformationChanged;

    public void RefreshMediaInformation() => QueueMediaRequest(PublishMediaInformation);

    private void PublishMediaInformation()
    {
        var snapshot = _playerCapabilities.TryGetMediaInformation(out VLCPlayerCapabilityResult result);
        long queue = State.Generation;
        MediaInformationState next = snapshot is null
            ? new(queue, MediaInformation.MediaGeneration, result.Code == VLCPlayerCapabilityResultCode.StaleSnapshot ? MediaInformationLoadState.Stale : MediaInformationLoadState.Unavailable, "", new Dictionary<string, string>(), result.Code.ToString())
            : new(queue, snapshot.Generation.MediaGeneration, snapshot.Metadata.Count == 0 && snapshot.Streams.Count == 0 ? MediaInformationLoadState.Empty : MediaInformationLoadState.Ready, snapshot.Name, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(snapshot.Metadata)), null,
                Array.AsReadOnly(snapshot.Streams.Select(stream => new MediaCodecStream(stream.Index, stream.Fields)).ToArray()));
        lock (_gate)
        {
            if (next.QueueGeneration < _mediaInformation.QueueGeneration || (next.QueueGeneration == _mediaInformation.QueueGeneration && next.MediaGeneration < _mediaInformation.MediaGeneration)) return;
            _mediaInformation = next;
        }
        MediaInformationChanged?.Invoke(this, next);
    }

    public void Open(string source) => OpenQueue([source]);

    internal void OpenEmptyQueueSessionForSmoke() =>
        StartCore([], [], "queue-empty-session");

    public void OpenQueue(IReadOnlyList<string> sources)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("At least one non-empty source is required.", nameof(sources));
        string[] normalized = sources.Select(MediaEngineSourceNormalizer.Normalize).ToArray();
        StartCore(normalized, [], normalized[0]);
    }

    private void StartCore(
        IReadOnlyList<string> launchSources,
        IReadOnlyList<string> initialEnqueueSources,
        string stateSource)
    {
        Stop();
        lock (_gate)
        {
            if (_thread is not null)
                throw new InvalidOperationException(
                    "The previous VLC playback thread did not stop.");
        }

        long generation = _states.BeginOpen(stateSource);
        string[] openArguments = [.. RateArguments(OpenPlaybackRate), .. OpenArguments];
        LiveAudioSettings? startupAudio = LiveAudio;
        LiveSubtitleSettings? startupSubtitles = LiveSubtitles;
        PublishQueue(new PlaybackQueueState(generation, 0, QueueLoadState.Loading, [], -1, _requestedRepeat, _requestedOrder));
        PublishMediaCapabilities(new MediaCapabilitiesState(generation, 0, MediaCapabilityLoadState.Loading, [], [], [], [], new(0, 0)));
        ManualResetEventSlim stop = new(false);
        Thread thread = new(() => Run(launchSources, initialEnqueueSources, openArguments, startupAudio, startupSubtitles, generation, stop))
        {
            IsBackground = true,
            Name = "VLCLR composition playback",
        };

        lock (_gate)
        {
            _generation = generation;
            _tearingDown = false;
            _queue = null;
            _lastObservedInputIdentity = 0;
            _stop = stop;
            _thread = thread;
        }
        thread.Start();
    }

    public void TogglePause()
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            new VLCTransportControl(_playlist).TogglePause();
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void Seek(double normalizedPosition)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            if (!new VLCTransportControl(_playlist).TrySetNormalizedPosition(
                    Math.Clamp(normalizedPosition, 0, 1)))
                return;
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void SetVolume(double normalizedVolume)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            VLCCore.PlaylistVolumeSet(
                _playlist,
                (float)Math.Clamp(normalizedVolume, 0, AudioEngineOptions.MaximumVolumeLimit / 100d));
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void SetMuted(bool muted)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0) return;
            VLCCore.PlaylistMuteSet(_playlist, muted);
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void Skip(TimeSpan offset)
    {
        ThrowIfDisposed();
        MediaEngineState state = State;
        if (!state.CanSeek || state.Duration <= TimeSpan.Zero) return;
        Seek(Math.Clamp((state.Elapsed + offset).TotalSeconds / state.Duration.TotalSeconds, 0, 1));
    }

    public void SetPlaybackRate(double playbackRate)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0) return;
            if (!new VLCTransportControl(_playlist).TrySetPlaybackRate(playbackRate)) return;
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void SelectTrack(MediaTrackKind kind, string id)
    {
        if (!long.TryParse(id, out long value)) return;
        QueueMediaRequest(() =>
        {
            MediaCapabilitiesState snapshot = MediaCapabilities;
            _ = _mediaControls.SelectTrack(ToNativeTracks(snapshot), kind == MediaTrackKind.Audio ? VLCTrackKind.Audio : VLCTrackKind.Subtitle, value);
            PublishMediaCapabilitiesSnapshot(snapshot.QueueGeneration);
        });
    }

    public void SelectTitle(string id)
    {
        if (!long.TryParse(id, out long value)) return;
        QueueMediaRequest(() => { MediaCapabilitiesState snapshot = MediaCapabilities; _ = _mediaControls.SelectTitle(ToNativeTitles(snapshot), value); PublishMediaCapabilitiesSnapshot(snapshot.QueueGeneration); });
    }

    public void SelectChapter(string id)
    {
        if (!long.TryParse(id, out long value)) return;
        QueueMediaRequest(() => { MediaCapabilitiesState snapshot = MediaCapabilities; _ = _mediaControls.SelectChapter(ToNativeTitles(snapshot), value); PublishMediaCapabilitiesSnapshot(snapshot.QueueGeneration); });
    }

    public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds)
    {
        QueueMediaRequest(() => { _ = _mediaControls.SetDelay(kind == MediaTrackKind.Audio ? VLCMediaDelayKind.Audio : VLCMediaDelayKind.Subtitle, TimeSpan.FromMilliseconds(MediaDelayPolicy.Clamp(milliseconds))); PublishMediaCapabilitiesSnapshot(State.Generation); });
    }

    public void ResetDelay(MediaTrackKind kind)
    {
        QueueMediaRequest(() => { _ = _mediaControls.ResetDelay(kind == MediaTrackKind.Audio ? VLCMediaDelayKind.Audio : VLCMediaDelayKind.Subtitle); PublishMediaCapabilitiesSnapshot(State.Generation); });
    }

    public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path)
    {
        if (!QueueMediaRequest(() =>
        {
            VLCExternalTrackAttachmentResult result = _mediaControls.AttachExternalTrack(path, kind == MediaTrackKind.Audio ? VLCExternalTrackKind.Audio : VLCExternalTrackKind.Subtitle);
            PublishMediaCapabilitiesSnapshot(State.Generation);
            if (result.AcceptedIntoItem && result.PendingUntilNextOpen)
                PublishMediaCapabilities(MediaCapabilities with { Error = "External track added for the next open; it is not a current live track." });
        }))
            return new(false, false, false, "Media controls are unavailable while VLC is opening or closing.");
        return new(true, true, false, null);
    }

    private bool QueueMediaRequest(Action request)
    {
        lock (_gate)
        {
            if (_tearingDown || _thread is null || _playlist == 0) return false;
            _mediaRequests.Enqueue(request);
            return true;
        }
    }

    internal T InvokeMediaCapabilityForSmoke<T>(Func<IVLCMediaCapabilitySmokeAdapter, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using ManualResetEventSlim completed = new(false);
        T result = default!;
        Exception? failure = null;
        if (!QueueMediaRequest(() =>
        {
            try { result = action(_mediaControls); }
            catch (Exception exception) { failure = exception; }
            finally { completed.Set(); }
        }))
            throw new InvalidOperationException("The VLC media-capability dispatcher is unavailable.");
        if (!completed.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The VLC media-capability dispatcher did not respond.");
        if (failure is not null) throw new InvalidOperationException("The VLC media-capability dispatcher failed.", failure);
        return result;
    }

    internal void PollMediaCapabilitiesForSmoke() =>
        _ = InvokeMediaCapabilityForSmoke(adapter => adapter.TryGetTracks(out _));

    /// <summary>Runs capability discovery on the VLC owner dispatcher. Video rendering is verified by --video-smoke.</summary>
    internal VLCPlayerSmokeReport RunPlayerCapabilitiesForSmoke() => InvokeMediaCapabilityForSmoke(adapter =>
    {
        var report = VLCPlayerCapabilitiesSmoke.Run(_playerCapabilities);
        var video = _videoControls.Read(out var result);
        return new VLCPlayerSmokeReport(report.Steps.Select(step => step.Phase == VLCPlayerSmokePhase.VideoPresentation
            ? new VLCPlayerSmokeStep(step.Phase, video is null ? VLCPlayerSmokeDisposition.Skipped : VLCPlayerSmokeDisposition.Passed, result,
                video is null ? $"No single active video output ({result.Code}, native error {result.NativeError})." : "Held VLC 3 output exposes aspect/crop/zoom choices and readback; rendering qualification uses --video-smoke.")
            : step).ToArray());
    });

    public void Enqueue(IEnumerable<string> sources)
    {
        ThrowIfDisposed();
        string[] values = sources?
            .Select(MediaEngineSourceNormalizer.Normalize)
            .Select(ToPlaylistUri)
            .ToArray() ?? [];
        if (values.Length == 0) { PublishQueueFailure("Choose one or more media items to enqueue."); return; }
        VLCPlaylistResult result;
        long generation;
        bool startEmptyCore;
        lock (_gate)
        {
            generation = _generation;
            startEmptyCore = _queue is null && _thread is null;
            result = startEmptyCore ? VLCPlaylistResult.Success : _queue?.Add(values) ?? new(VLCPlaylistResultCode.CoreUnavailable);
        }
        if (startEmptyCore)
        {
            // An enqueue after product Clear starts an empty core then adds with
            // playNow=false; it deliberately does not turn enqueue into play.
            StartCore([], values, values[0]);
            return;
        }
        if (!result.Succeeded) PublishQueueFailure(Describe(result, values.Length));
        // Never infer success from a dispatched request; publish only a fresh poll.
        PublishQueueSnapshot(generation);
    }

    public void SelectAndPlay(string token) => MutateItem(token, static (q, s, i) => q.SelectAndPlay(s, i));
    public void Remove(string token) => MutateItem(token, static (q, s, i) => q.Remove(s, i));

    public void Move(string token, int destinationIndex)
    {
        if (destinationIndex < 0) { PublishQueueFailure("The requested queue position is invalid."); return; }
        MutateItem(token, (q, s, i) => q.Move(s, i, destinationIndex));
    }

    public void ClearQueue()
    {
        ThrowIfDisposed();
        // Do not call VLCPlaylistQueue.Clear on a live VLC 3 core: after a
        // current-item deletion, playlist_Clear can make InternalDestroy stall.
        Stop();
        long generation = _states.BeginEmpty();
        lock (_gate)
        {
            _generation = generation;
            _tearingDown = false;
            _playlist = 0;
            _queue = null;
        }
        PublishQueue(new PlaybackQueueState(generation, 0, QueueLoadState.Empty, [], -1, _requestedRepeat, _requestedOrder));
    }

    public void Previous() => Navigate(previous: true);
    public void Next() => Navigate(previous: false);

    public void SetRepeatMode(QueueRepeatMode mode)
    {
        if (mode == QueueRepeatMode.Inconsistent) return;
        ThrowIfDisposed(); VLCPlaylistResult result; long generation;
        lock (_gate)
        {
            generation = _generation;
            result = _queue?.SetRepeatMode(ToNative(mode)) ?? new(VLCPlaylistResultCode.CoreUnavailable);
            if (result.Succeeded) _requestedRepeat = mode;
        }
        if (!result.Succeeded) PublishQueueFailure(Describe(result));
        PublishQueueSnapshot(generation);
    }

    public void SetOrder(QueueOrder order)
    {
        ThrowIfDisposed(); VLCPlaylistResult result; long generation;
        lock (_gate)
        {
            generation = _generation;
            result = _queue?.SetOrder(order == QueueOrder.Random ? VLCPlaylistOrder.Random : VLCPlaylistOrder.Normal) ?? new(VLCPlaylistResultCode.CoreUnavailable);
            if (result.Succeeded) _requestedOrder = order;
        }
        if (!result.Succeeded) PublishQueueFailure(Describe(result));
        PublishQueueSnapshot(generation);
    }

    private void Navigate(bool previous)
    {
        ThrowIfDisposed(); VLCPlaylistResult result; long generation;
        lock (_gate)
        {
            generation = _generation;
            PlaybackQueueState state = _queueState;
            bool valid = previous ? state.CanPrevious : state.CanNext;
            result = !valid ? new(VLCPlaylistResultCode.InvalidArgument) : previous ? _queue?.Previous() ?? new(VLCPlaylistResultCode.CoreUnavailable) : _queue?.Next() ?? new(VLCPlaylistResultCode.CoreUnavailable);
        }
        if (!result.Accepted) PublishQueueFailure(Describe(result));
        PublishQueueSnapshot(generation);
    }

    private void MutateItem(string token, Func<VLCPlaylistQueue, VLCPlaylistSnapshot, VLCPlaylistItem, VLCPlaylistResult> mutation)
    {
        ThrowIfDisposed(); VLCPlaylistResult result; long generation;
        lock (_gate)
        {
            generation = _generation;
            if (_queue is null) result = new(VLCPlaylistResultCode.CoreUnavailable);
            else
            {
                using VLCPlaylistSnapshot? snapshot = _queue.TryGetSnapshot(out result);
                VLCPlaylistItem? item = snapshot?.Items.FirstOrDefault(x => Token(generation, x) == token);
                result = item is null ? new(VLCPlaylistResultCode.StaleSnapshot) : mutation(_queue, snapshot!, item);
            }
        }
        if (!result.Accepted) PublishQueueFailure(Describe(result));
        PublishQueueSnapshot(generation);
    }

    private void ApplyRequestedModes(long generation)
    {
        lock (_gate)
        {
            if (_generation != generation || _queue is null) return;
            VLCPlaylistResult repeat = _queue.SetRepeatMode(ToNative(_requestedRepeat));
            VLCPlaylistResult order = _queue.SetOrder(_requestedOrder == QueueOrder.Random ? VLCPlaylistOrder.Random : VLCPlaylistOrder.Normal);
            if (!repeat.Succeeded || !order.Succeeded) PublishQueueFailure("Playback queue modes could not be restored.");
        }
        PublishQueueSnapshot(generation);
    }

    private VLCPlaylistLease? TryGetPlaylistLease()
    {
        lock (_gate)
            return !_tearingDown && _playlist != 0 ? new VLCPlaylistLease(_playlist, _generation) : null;
    }

    /// <summary>Returns a short-lived capability lease only while the input is live.
    /// The held pointer is released immediately after comparison; its numeric value is retained solely as an identity token.</summary>
    private VLCCurrentInputLease? TryGetCurrentInputLease()
    {
        nint playlist;
        long coreGeneration;
        lock (_gate)
        {
            if (_tearingDown || _playlist == 0)
                return null;
            playlist = _playlist;
            coreGeneration = _coreGeneration;
        }

        nint heldInput = VLCCore.PlaylistCurrentInput(playlist);
        if (heldInput == 0)
            return null;
        try
        {
            lock (_gate)
            {
                if (_tearingDown || _playlist != playlist || _coreGeneration != coreGeneration)
                    return null;
                if (_lastObservedInputIdentity != heldInput)
                {
                    _lastObservedInputIdentity = heldInput;
                    _mediaGeneration++;
                }
                return new VLCCurrentInputLease(playlist, _coreGeneration, _mediaGeneration);
            }
        }
        finally
        {
            VLCCore.ObjectRelease(heldInput);
        }
    }

    private static VLCTrackSnapshot ToNativeTracks(MediaCapabilitiesState state) => new(
        new VLCInputGeneration(state.QueueGeneration, state.MediaGeneration),
        state.AudioTracks.Select(x => new VLCTrackChoice(long.Parse(x.Id), x.Label, x.IsDisabled)).ToArray(),
        state.SubtitleTracks.Select(x => new VLCTrackChoice(long.Parse(x.Id), x.Label, x.IsDisabled)).ToArray(),
        state.AudioTracks.FirstOrDefault(x => x.IsSelected) is { } audio ? long.Parse(audio.Id) : null,
        state.SubtitleTracks.FirstOrDefault(x => x.IsSelected) is { } subtitle ? long.Parse(subtitle.Id) : null);

    private static VLCTitleChapterSnapshot ToNativeTitles(MediaCapabilitiesState state) => new(
        new VLCInputGeneration(state.QueueGeneration, state.MediaGeneration),
        state.Titles.Select(x => new VLCTitleChoice(long.Parse(x.Id), x.Label)).ToArray(),
        state.Chapters.Select(x => new VLCChapterChoice(long.Parse(x.Id), x.Label)).ToArray(),
        state.Titles.FirstOrDefault(x => x.IsSelected) is { } title ? long.Parse(title.Id) : null,
        state.Chapters.FirstOrDefault(x => x.IsSelected) is { } chapter ? long.Parse(chapter.Id) : null);

    private static string Describe(VLCMediaResult result) => result.Code switch
    {
        VLCMediaResultCode.InputUnavailable => "Media controls are unavailable while VLC is opening or closing.",
        VLCMediaResultCode.StaleSnapshot => "The media changed before the action could be applied. It has been refreshed.",
        VLCMediaResultCode.OutOfRange => $"Delay must be between −{MediaDelayPolicy.MaximumMilliseconds:N0} and +{MediaDelayPolicy.MaximumMilliseconds:N0} ms.",
        VLCMediaResultCode.InvalidArgument => "That media control is no longer available.",
        _ => "VLC could not apply that media control.",
    };

    private void PublishMediaCapabilitiesSnapshot(long queueGeneration)
    {
        if (queueGeneration <= 0) return;
        VLCTrackSnapshot? tracks = _mediaControls.TryGetTracks(out VLCMediaResult tracksResult);
        VLCTitleChapterSnapshot? titles = _mediaControls.TryGetTitlesAndChapters(out VLCMediaResult titlesResult);
        VLCMediaDelaySnapshot? delays = _mediaControls.TryGetDelays(out VLCMediaResult delaysResult);
        if (tracks is null || titles is null || delays is null)
        {
            VLCMediaResult error = !tracksResult.Succeeded ? tracksResult : !titlesResult.Succeeded ? titlesResult : delaysResult;
            if (error.Code == VLCMediaResultCode.StaleSnapshot) return;
            PublishMediaCapabilities(new(queueGeneration, MediaCapabilities.MediaGeneration, error.Code == VLCMediaResultCode.InputUnavailable ? MediaCapabilityLoadState.Loading : MediaCapabilityLoadState.Failed, [], [], [], [], MediaCapabilities.Delays, Describe(error)));
            return;
        }
        if (tracks.Generation.CoreGeneration != titles.Generation.CoreGeneration || tracks.Generation.MediaGeneration != titles.Generation.MediaGeneration || tracks.Generation.CoreGeneration != delays.Value.Generation.CoreGeneration || tracks.Generation.MediaGeneration != delays.Value.Generation.MediaGeneration)
            return;
        long mediaGeneration = tracks.Generation.MediaGeneration;
        MediaCapabilitiesState state = new(queueGeneration, mediaGeneration,
            tracks.AudioTracks.Count == 0 && tracks.SubtitleTracks.Count == 0 && titles.Titles.Count == 0 && titles.Chapters.Count == 0 ? MediaCapabilityLoadState.Empty : MediaCapabilityLoadState.Ready,
            tracks.AudioTracks.Select(x => new MediaTrack(x.Id.ToString(), string.IsNullOrWhiteSpace(x.Label) ? "Audio track" : x.Label, x.IsDisabled, x.Id == tracks.SelectedAudioId)).ToArray(),
            tracks.SubtitleTracks.Select(x => new MediaTrack(x.Id.ToString(), string.IsNullOrWhiteSpace(x.Label) ? "Subtitle track" : x.Label, x.IsDisabled, x.Id == tracks.SelectedSubtitleId)).ToArray(),
            titles.Titles.Select(x => new MediaTitle(x.Id.ToString(), string.IsNullOrWhiteSpace(x.Label) ? "Title" : x.Label, x.Id == titles.SelectedTitleId)).ToArray(),
            titles.Chapters.Select(x => new MediaChapter(x.Id.ToString(), string.IsNullOrWhiteSpace(x.Label) ? "Chapter" : x.Label, x.Id == titles.SelectedChapterId)).ToArray(),
            new((int)Math.Clamp(delays.Value.Audio.TotalMilliseconds, int.MinValue, int.MaxValue), (int)Math.Clamp(delays.Value.Subtitle.TotalMilliseconds, int.MinValue, int.MaxValue)));
        PublishMediaCapabilities(state);
    }

    private void PublishMediaCapabilities(MediaCapabilitiesState state)
    {
        bool changed;
        lock (_gate) { changed = !Equals(_mediaCapabilities, state); _mediaCapabilities = state; }
        if (changed) MediaCapabilitiesChanged?.Invoke(this, state);
    }

    private void PublishQueueSnapshot(long generation)
    {
        PlaybackQueueState? next = null;
        lock (_gate)
        {
            if (_generation != generation || _queue is null || _tearingDown) return;
            using VLCPlaylistSnapshot? snapshot = _queue.TryGetSnapshot(out VLCPlaylistResult result);
            if (snapshot is null) { next = new(generation, 0, QueueLoadState.Failed, [], -1, _requestedRepeat, _requestedOrder, Describe(result)); }
            else
            {
                VLCPlaylistModeSnapshot? modes = _queue.TryGetModes(out VLCPlaylistResult modeResult);
                QueueRepeatMode repeat = modes is null ? _requestedRepeat : FromNative(modes.Value.Repeat);
                QueueOrder order = modes is null ? _requestedOrder : modes.Value.Order == VLCPlaylistOrder.Random ? QueueOrder.Random : QueueOrder.Normal;
                IReadOnlyList<QueueItem> items = snapshot.Items.Select((item, index) => new QueueItem(Token(generation, item), item.Name, item.Uri, index, item.IsCurrent)).ToArray();
                next = new(generation, snapshot.Version, items.Count == 0 ? QueueLoadState.Empty : QueueLoadState.Ready, items, snapshot.CurrentIndex, repeat, order, modes is null ? Describe(modeResult) : null);
            }
        }
        if (next is not null) PublishQueue(next);
    }

    private void PublishQueueFailure(string message)
    {
        PlaybackQueueState old = QueueState;
        PublishQueue(old with { Status = QueueLoadState.Failed, Error = message });
    }

    private void PublishQueue(PlaybackQueueState state)
    {
        bool changed;
        lock (_gate) { changed = !Equals(_queueState, state); _queueState = state; }
        if (changed) QueueStateChanged?.Invoke(this, state);
    }

    private static string Token(long generation, VLCPlaylistItem item) => $"{generation}:{item.Id}:{item.Uri}";
    private static string ToPlaylistUri(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile
            ? source
            : new Uri(source).AbsoluteUri;
    private static VLCPlaylistRepeatMode ToNative(QueueRepeatMode mode) => mode switch { QueueRepeatMode.CurrentItem => VLCPlaylistRepeatMode.CurrentItem, QueueRepeatMode.All => VLCPlaylistRepeatMode.All, _ => VLCPlaylistRepeatMode.None };
    private static QueueRepeatMode FromNative(VLCPlaylistRepeatMode mode) => mode switch { VLCPlaylistRepeatMode.CurrentItem => QueueRepeatMode.CurrentItem, VLCPlaylistRepeatMode.All => QueueRepeatMode.All, VLCPlaylistRepeatMode.Inconsistent => QueueRepeatMode.Inconsistent, _ => QueueRepeatMode.None };
    private static string Describe(VLCPlaylistResult result, int requested = 0) => result.Code switch
    {
        VLCPlaylistResultCode.CoreUnavailable => "The playback queue is unavailable while VLC is opening or closing.",
        VLCPlaylistResultCode.StaleSnapshot => "The queue changed before the action could be applied. It has been refreshed.",
        VLCPlaylistResultCode.NestedQueueUnsupported => "This playlist contains nested entries and cannot be loaded safely.",
        VLCPlaylistResultCode.InvalidArgument => "That queue action is not valid for the current item.",
        _ when requested > 0 && result.CompletedCount > 0 => $"Added {result.CompletedCount} of {requested} media items; the rest could not be queued.",
        _ => "The playback queue could not be updated.",
    };

    private void Run(
        IReadOnlyList<string> sources,
        IReadOnlyList<string> initialEnqueueSources,
        IReadOnlyList<string> openArguments,
        LiveAudioSettings? startupAudio,
        LiveSubtitleSettings? startupSubtitles,
        long generation,
        ManualResetEventSlim stop)
    {
        string[] arguments =
        [
            "vlc-avalonia",
            "--intf=vlc_avalonia_control",
            "--no-video-title-show",
            $"--vout={_videoOutput}",
            .. (_verboseLogging ? new[] { "-vvv" } : []),
            _hardwareDecoding ? "--avcodec-hw=any" : "--avcodec-hw=none",
            // VideoToolbox is a separate decoder module on macOS, ahead of avcodec.
            .. (!_hardwareDecoding && OperatingSystem.IsMacOS() ? new[] { "--no-videotoolbox" } : []),
            .. openArguments,
            .. sources,
        ];

        nint[] strings = new nint[arguments.Length];
        nint argv = 0;
        nint core = 0;
        nint playlist = 0;
        bool initialized = false;
        bool faulted = false;
        try
        {
            _controlHost.Reset();
            for (int index = 0; index < arguments.Length; index++)
                strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);

            argv = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            for (int index = 0; index < strings.Length; index++)
                Marshal.WriteIntPtr(argv, index * IntPtr.Size, strings[index]);

            core = VLCCore.libvlc_InternalCreate();
            if (core == 0)
                throw new InvalidOperationException(
                    "libvlc_InternalCreate failed.");

            int result = VLCCore.libvlc_InternalInit(
                core,
                arguments.Length,
                argv);
            if (result != 0)
                throw new InvalidOperationException(
                    $"libvlc_InternalInit failed with {result}.");
            initialized = true;

            result = VlcCoreApi.AddInterface(
                core,
                "vlc_avalonia_control");
            if (result != 0)
                throw new InvalidOperationException(
                    "libvlc_InternalAddIntf could not start interface " +
                    $"vlc_avalonia_control (result {result}).");

            playlist = _controlHost.WaitForPlaylist(
                PlaylistTimeout,
                stop);

            lock (_gate)
            {
                if (_generation == generation)
                {
                    _playlist = playlist;
                    _coreGeneration++;
                    _mediaGeneration = 0;
                    _lastObservedInputIdentity = 0;
                    _queue = new VLCPlaylistQueue(TryGetPlaylistLease);
                    // The core started with the audio and subtitle options of these settings.
                    ResetAppliedAudio(startupAudio);
                    ResetAppliedSubtitles(startupSubtitles);
                }
            }

            ApplyRequestedModes(generation);
            ApplyLiveAudio();
            ApplyLiveSubtitles();

            VLCCore.libvlc_InternalPlay(core);
            if (initialEnqueueSources.Count != 0)
            {
                VLCPlaylistResult enqueue;
                lock (_gate)
                    enqueue = _queue?.Add(initialEnqueueSources) ?? new(VLCPlaylistResultCode.CoreUnavailable);
                if (!enqueue.Succeeded)
                    PublishQueueFailure(Describe(enqueue, initialEnqueueSources.Count));
            }
            PublishSnapshot(generation);
            while (!stop.Wait(TimeSpan.FromMilliseconds(200)))
            {
                while (_mediaRequests.TryDequeue(out Action? request)) request();
                PublishSnapshot(generation);
            }

        }
        catch (Exception exception)
        {
            faulted = true;
            _states.Fault(generation, exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                _tearingDown = true;
                if (_generation == generation && _playlist == playlist)
                {
                    _playlist = 0;
                    _queue = null;
                }
            }
            // The interface callback lends the playlist for the interface/core
            // lifetime. It must be forgotten before core cleanup and is never
            // released independently.
            _controlHost.Reset();
            playlist = 0;
            if (initialized)
            {
                VLCCore.libvlc_InternalCleanup(core);
            }
            if (core != 0)
            {
                VLCCore.libvlc_InternalDestroy(core);
            }
            if (argv != 0)
                Marshal.FreeHGlobal(argv);
            foreach (nint value in strings)
            {
                if (value != 0)
                    Marshal.FreeCoTaskMem(value);
            }
            if (!faulted)
            {
                _states.Stop(generation);
            }
            if (_generation == generation)
            {
                PublishQueue(new PlaybackQueueState(generation, 0, QueueLoadState.Unavailable, [], -1, _requestedRepeat, _requestedOrder));
                PublishMediaCapabilities(new MediaCapabilitiesState(generation, _mediaGeneration, MediaCapabilityLoadState.Unavailable, [], [], [], [], new(0, 0)));
            }
        }
    }

    private void PublishSnapshot(long generation)
    {
        PlaybackStatus status;
        double position;
        long elapsed;
        long duration;
        bool canSeek;
        double volume;
        bool muted = State.IsMuted;
        double playbackRate = State.PlaybackRate;
        lock (_gate)
        {
            if (_playlist == 0 || _generation != generation)
                return;

            status = VLCCore.PlaylistStatus(_playlist) switch
            {
                PlaylistRunning => PlaybackStatus.Playing,
                PlaylistPaused => PlaybackStatus.Paused,
                PlaylistStopped => PlaybackStatus.Stopped,
                _ => PlaybackStatus.Opening,
            };

            position = 0;
            elapsed = 0;
            duration = 0;
            canSeek = false;
            nint input = VLCCore.PlaylistCurrentInput(_playlist);
            if (input != 0)
            {
                try
                {
                    VLCVariable variables = new(input);
                    position = variables.GetFloat("position");
                    elapsed = variables.GetInteger("time");
                    duration = variables.GetInteger("length");
                    canSeek = variables.GetBool("can-seek");
                }
                finally
                {
                    VLCCore.ObjectRelease(input);
                }
            }
            volume = VLCCore.PlaylistVolumeGet(_playlist);
            int muteResult = VLCCore.PlaylistMuteGet(_playlist);
            if (muteResult is 0 or 1)
                muted = muteResult != 0;
            if (new VLCTransportControl(_playlist).TryGetPlaybackRate(out double rate) &&
                double.IsFinite(rate) && rate >= VLCTransportControl.MinimumPlaybackRate &&
                rate <= VLCTransportControl.MaximumPlaybackRate)
                playbackRate = rate;
        }

        _states.Update(
            generation,
            status,
            position,
            TimeSpan.FromMicroseconds(Math.Max(0, elapsed)),
            TimeSpan.FromMicroseconds(Math.Max(0, duration)),
            volume,
            canSeek,
            isMuted: muted,
            playbackRate: playbackRate);
        PublishQueueSnapshot(generation);
        PublishMediaCapabilitiesSnapshot(generation);
        PublishVideoAdjustments();
    }

    VLCPlaylistSnapshot? IVLCPlaylistQueueSmokeAdapter.TryGetSnapshot(out VLCPlaylistResult result)
    {
        lock (_gate) return _queue?.TryGetSnapshot(out result) ?? Unavailable(out result);
    }
    bool IVLCPlaylistQueueSmokeAdapter.TryGetActiveCurrentItemId(out int? itemId, out VLCPlaylistResult result)
    {
        lock (_gate) return _queue?.TryGetActiveCurrentItemId(out itemId, out result) ?? UnavailableActiveCurrent(out itemId, out result);
    }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Add(IEnumerable<string> uris, bool playFirst) { lock (_gate) return _queue?.Add(uris, playFirst) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Clear(VLCPlaylistSnapshot snapshot) { lock (_gate) return _queue?.Clear(snapshot) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Move(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item, int destinationIndex) { lock (_gate) return _queue?.Move(snapshot, item, destinationIndex) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.SelectAndPlay(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) { lock (_gate) return _queue?.SelectAndPlay(snapshot, item) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Remove(VLCPlaylistSnapshot snapshot, VLCPlaylistItem item) { lock (_gate) return _queue?.Remove(snapshot, item) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Next() { lock (_gate) return _queue?.Next() ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.Previous() { lock (_gate) return _queue?.Previous() ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistModeSnapshot? IVLCPlaylistQueueSmokeAdapter.TryGetModes(out VLCPlaylistResult result) { lock (_gate) return _queue?.TryGetModes(out result) ?? UnavailableModes(out result); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.SetRepeatMode(VLCPlaylistRepeatMode mode) { lock (_gate) return _queue?.SetRepeatMode(mode) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    VLCPlaylistResult IVLCPlaylistQueueSmokeAdapter.SetOrder(VLCPlaylistOrder order) { lock (_gate) return _queue?.SetOrder(order) ?? new(VLCPlaylistResultCode.CoreUnavailable); }
    private static VLCPlaylistSnapshot? Unavailable(out VLCPlaylistResult result) { result = new(VLCPlaylistResultCode.CoreUnavailable); return null; }
    private static bool UnavailableActiveCurrent(out int? itemId, out VLCPlaylistResult result) { itemId = null; result = new(VLCPlaylistResultCode.CoreUnavailable); return false; }
    private static VLCPlaylistModeSnapshot? UnavailableModes(out VLCPlaylistResult result) { result = new(VLCPlaylistResultCode.CoreUnavailable); return null; }

    VLCTrackSnapshot? IVLCMediaCapabilitySmokeAdapter.TryGetTracks(out VLCMediaResult result) => _mediaControls.TryGetTracks(out result);
    VLCTitleChapterSnapshot? IVLCMediaCapabilitySmokeAdapter.TryGetTitlesAndChapters(out VLCMediaResult result) => _mediaControls.TryGetTitlesAndChapters(out result);
    VLCMediaDelaySnapshot? IVLCMediaCapabilitySmokeAdapter.TryGetDelays(out VLCMediaResult result) => _mediaControls.TryGetDelays(out result);
    VLCMediaResult IVLCMediaCapabilitySmokeAdapter.SelectTrack(VLCTrackSnapshot snapshot, VLCTrackKind kind, long id) => _mediaControls.SelectTrack(snapshot, kind, id);
    VLCMediaResult IVLCMediaCapabilitySmokeAdapter.SelectTitle(VLCTitleChapterSnapshot snapshot, long id) => _mediaControls.SelectTitle(snapshot, id);
    VLCMediaResult IVLCMediaCapabilitySmokeAdapter.SelectChapter(VLCTitleChapterSnapshot snapshot, long id) => _mediaControls.SelectChapter(snapshot, id);
    VLCMediaResult IVLCMediaCapabilitySmokeAdapter.SetDelay(VLCMediaDelayKind kind, TimeSpan delay) => _mediaControls.SetDelay(kind, delay);
    VLCMediaResult IVLCMediaCapabilitySmokeAdapter.ResetDelay(VLCMediaDelayKind kind) => _mediaControls.ResetDelay(kind);
    VLCExternalTrackAttachmentResult IVLCMediaCapabilitySmokeAdapter.AttachExternalTrack(string path, VLCExternalTrackKind kind) => _mediaControls.AttachExternalTrack(path, kind);

    public void Stop()
    {
        Thread? thread;
        ManualResetEventSlim? stop;
        long generation;
        lock (_gate)
        {
            thread = _thread;
            stop = _stop;
            generation = _generation;
            _tearingDown = true;
        }
        stop?.Set();

        if (thread is not null && thread != Thread.CurrentThread &&
            !thread.Join(ShutdownTimeout))
        {
            string message =
                $"VLC playback did not stop within {ShutdownTimeout.TotalSeconds:0} seconds.";
            if (generation != 0)
                _states.Fault(generation, message);
            throw new TimeoutException(message);
        }

        lock (_gate)
        {
            if (_thread == thread)
            {
                _thread = null;
                _stop = null;
            }
        }
        if (thread != Thread.CurrentThread)
            stop?.Dispose();
        if (generation != 0)
            _states.Stop(generation);
        if (generation != 0)
            PublishQueue(new PlaybackQueueState(generation, 0, QueueLoadState.Unavailable, [], -1, _requestedRepeat, _requestedOrder));
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            // Stop can time out while libvlc is still releasing an input. Keep
            // the bridge alive and leave this instance retryable in that case;
            // a later Dispose can join the thread and complete native cleanup.
            Stop();
            _controlHost.Dispose();
            Volatile.Write(ref _disposed, 1);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

}

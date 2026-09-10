using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed partial class PlayerPresentationViewModel : INotifyPropertyChanged
{
    private readonly IPlayerTransport _transport;
    private readonly IMediaSourcePicker _mediaSourcePicker;
    private readonly Action _openTestMedia;
    private readonly PlayerSettingsService? _settings;
    private readonly MediaSourceValidator _sourceValidator;
    private readonly MediaHistoryService? _history;
    private bool _isUpdatingFromEngine;
    private bool _isSeeking;
    private TimeSpan _duration;
    private double _position;
    private double _volume = 50;
    private string _elapsedText = "00:00";
    private string _durationText = "00:00";
    private bool _isPlaying;
    private PlaybackStatus _playbackStatus = PlaybackStatus.Closed;
    private bool _isAlwaysOnTop;
    private bool _isMuted;
    private double _playbackRate = 1;
    private string _mediaTitle = "VLC Avalonia";
    private bool _isFullscreen;
    private MediaSource? _currentSource;
    private string? _stoppedSource;
    private ResumeEntry? _pendingResume;
    private string _openError = string.Empty;
    private string _urlText = string.Empty;
    private long _recordedOpenGeneration = -1;
    private long _activeGeneration = -1;
    private long _lastObservedGeneration;
    private long _generationFloor;
    private bool _resumeReady;
    private MediaEngineState? _lastState;
    private DateTimeOffset _lastCheckpointUtc;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _diagnostic;
    private readonly IQueueTransport? _queue;
    private readonly IMediaCapabilitiesTransport? _mediaControls;
    private readonly IMediaInformationTransport? _mediaInformationTransport;
    private readonly IClipboardService? _clipboard;
    private readonly MediaSourceExpander _sourceExpander;
    private PlaybackQueueState _queueState = PlaybackQueueState.Initial;
    private bool _isQueueOpen;
    private bool _isLibraryOpen;
    private string _libraryTab = "Recent";
    private MediaCapabilitiesState _mediaCapabilities = MediaCapabilitiesState.Initial;
    private bool _isUpdatingCapabilities;
    private string _drawerTab = "Queue";
    private string _mediaControlsNotice = string.Empty;
    private MediaInformationState _mediaInformation = MediaInformationState.Unavailable;
    private string _copyStatus = string.Empty;

    public PlayerPresentationViewModel(
        IPlayerTransport transport,
        IMediaSourcePicker mediaSourcePicker,
        Action openTestMedia,
        Action? toggleFullscreen = null,
        Action? openSettings = null,
        PlayerSettingsService? settings = null,
        MediaSourceValidator? sourceValidator = null,
        MediaHistoryService? history = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? diagnostic = null,
        Action? showUrlPanel = null, IClipboardService? clipboard = null)
    {
        _transport = transport;
        _mediaSourcePicker = mediaSourcePicker;
        _openTestMedia = openTestMedia;
        _settings = settings;
        _sourceValidator = sourceValidator ?? new MediaSourceValidator(new SystemMediaFileSystem());
        _history = history;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _diagnostic = diagnostic;
        _clipboard = clipboard;
        _queue = transport as IQueueTransport;
        _mediaControls = transport as IMediaCapabilitiesTransport;
        _mediaInformationTransport = transport as IMediaInformationTransport;
        VideoAdjustments = new(transport as IVideoAdjustmentTransport);
        _sourceExpander = new MediaSourceExpander(_sourceValidator);
        if (_queue is not null) { _queueState = _queue.QueueState; _queue.QueueStateChanged += (_, state) => ApplyQueueState(state); }
        if (_mediaControls is not null) { _mediaCapabilities = _mediaControls.MediaCapabilities; _mediaControls.MediaCapabilitiesChanged += (_, state) => ApplyMediaCapabilities(state); }
        if (_mediaInformationTransport is not null) { _mediaInformation = _mediaInformationTransport.MediaInformation; _mediaInformationTransport.MediaInformationChanged += (_, state) => { if (state.QueueGeneration > _mediaInformation.QueueGeneration || (state.QueueGeneration == _mediaInformation.QueueGeneration && state.MediaGeneration >= _mediaInformation.MediaGeneration)) { MediaInformation = state; OnPropertyChanged(nameof(MediaInformation)); } }; }
        if (_settings is not null) _settings.Changed += (_, updated) =>
        {
            TryHistory(() => { if (!updated.RememberRecentMedia) _history?.ClearRecent(); if (!updated.RememberResumeHistory) _history?.ClearResume(); });
            NotifyLibrary();
            if (!updated.RememberResumeHistory) { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); }
            OnPropertyChanged(nameof(SkipBackwardLabel)); OnPropertyChanged(nameof(SkipForwardLabel)); OnPropertyChanged(nameof(RecentItems));
        };
        PlayerCommandRegistry registry = new();
        Registry = registry;
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => _ = OpenMediaAsync());
        registry.Register(new(PlayerCommandId.OpenFolder, "Open folder", "Media", "Ctrl+Shift+O"), _ => _ = OpenFolderAsync(), _ => _queue is not null);
        registry.Register(new(PlayerCommandId.OpenUrl, "Open network URL", "Media", "Ctrl+L"), _ => showUrlPanel?.Invoke(), _ => showUrlPanel is not null);
        registry.Register(new(PlayerCommandId.SubmitUrl, "Submit network URL", "Media"), _ => OpenUrl());
        registry.Register(new(PlayerCommandId.RemoveRecent, "Remove recent media", "Media"), p => { TryHistory(() => _history?.RemoveRecent(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.ClearRecent, "Clear recent media", "Media"), _ => { TryHistory(() => _history?.ClearRecent()); NotifyLibrary(); }, _ => HasRecentItems);
        registry.Register(new(PlayerCommandId.ClearResumeHistory, "Clear resume history", "Media"), _ => { if (TryHistory(() => _history?.ClearResume())) { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary(); } }, _ => HasResumeItems);
        registry.Register(new(PlayerCommandId.RemoveResume, PlayerText.CommandRemoveResume, "Library"), p => { TryHistory(() => _history?.RemoveResume(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.AddFavorite, PlayerText.CommandAddFavorite, "Library"), p => AddFavorite(p as string), p => _history is not null && (p is string || _currentSource is not null));
        registry.Register(new(PlayerCommandId.RemoveFavorite, PlayerText.CommandRemoveFavorite, "Library"), p => { TryHistory(() => _history?.RemoveFavorite(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.ClearFavorites, PlayerText.CommandClearFavorites, "Library"), _ => { TryHistory(() => _history?.ClearFavorites()); NotifyLibrary(); }, _ => HasFavoriteItems);
        registry.Register(new(PlayerCommandId.ClearLibrary, PlayerText.CommandClearLibrary, "Library"), _ => { if (TryHistory(() => _history?.ClearAll())) { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary(); } }, _ => HasLibraryItems);
        registry.Register(new(PlayerCommandId.OpenLibrary, PlayerText.LibraryMenu, "Library", "Ctrl+Shift+L"), _ => IsLibraryOpen = !IsLibraryOpen, _ => _history is not null);
        registry.Register(new(PlayerCommandId.OpenRecent, "Open recent media", "Media"), parameter => OpenSource(parameter as string, MediaSourceKind.Recent));
        registry.Register(new(PlayerCommandId.AcceptResume, "Resume playback", "Media"), _ => ApplyResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.DeclineResume, "Start from beginning", "Media"), _ => DeclineResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.OpenTestMedia, "Open test video", "Media"), _ => _openTestMedia());
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => TogglePlayback(), _ => CanTogglePlayback);
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ =>
        {
            CheckpointCurrent(true);
            _stoppedSource = QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source ?? _currentSource?.CanonicalSource ?? _lastState?.Source;
            _transport.Stop();
        }, _ => CanStop);
        registry.Register(new(PlayerCommandId.Seek, "Seek", "Playback"), parameter => _transport.Seek(ToDouble(parameter)));
        registry.Register(new(PlayerCommandId.SetVolume, "Set volume", "Audio"), parameter => _transport.SetVolume(Math.Clamp(ToDouble(parameter), 0, 200) / 100), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.ToggleMute, "Mute", "Audio", "M"), _ => _transport.SetMuted(!IsMuted), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.SkipBackward, "Skip backward", "Playback", "Alt+Left"), _ => _transport.Skip(TimeSpan.FromSeconds(-(_settings?.Current.SkipBackwardSeconds ?? 10))), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SkipForward, "Skip forward", "Playback", "Alt+Right"), _ => _transport.Skip(TimeSpan.FromSeconds(_settings?.Current.SkipForwardSeconds ?? 10)), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SetPlaybackRate, "Playback speed", "Playback"), parameter => _transport.SetPlaybackRate(Math.Clamp(ToDouble(parameter), 1d / 32, 31.25)));
        registry.Register(new(PlayerCommandId.ToggleFullscreen, "Fullscreen", "Window", "F11"), _ => toggleFullscreen?.Invoke(), _ => toggleFullscreen is not null);
        registry.Register(new(PlayerCommandId.SetAlwaysOnTop, "Always on top", "Window"), parameter => IsAlwaysOnTop = parameter is true);
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "Window", "Ctrl+,"), _ => openSettings?.Invoke(), _ => openSettings is not null);
        registry.Register(new(PlayerCommandId.ToggleQueue, "Playback queue", "Playback", "Ctrl+J"), p => { if (p is string tab && tab == "Queue" && DrawerTab != "Queue") { DrawerTab = tab; IsQueueOpen = true; } else IsQueueOpen = !IsQueueOpen; }, _ => _queue is not null);
        registry.Register(new(PlayerCommandId.QueuePrevious, "Previous item", "Playback"), _ => _queue?.Previous(), _ => QueueState.CanPrevious);
        registry.Register(new(PlayerCommandId.QueueNext, "Next item", "Playback"), _ => _queue?.Next(), _ => QueueState.CanNext);
        registry.Register(new(PlayerCommandId.QueueSelect, "Play queue item", "Playback"), p => _queue?.SelectAndPlay(p as string ?? string.Empty), p => p is string token && QueueItems.Any(x => x.Token == token));
        registry.Register(new(PlayerCommandId.QueueMove, "Move queue item", "Playback"), p => { if (p is QueueMove move) _queue?.Move(move.Token, move.DestinationIndex); }, p => p is QueueMove move && QueueItems.Any(x => x.Token == move.Token));
        registry.Register(new(PlayerCommandId.QueueRemove, "Remove queue item", "Playback"), p => _queue?.Remove(p as string ?? string.Empty), p => p is string token && QueueItems.Any(x => x.Token == token));
        registry.Register(new(PlayerCommandId.QueueClear, "Clear queue", "Playback"), _ => _queue?.ClearQueue(), _ => QueueItems.Count > 0);
        registry.Register(new(PlayerCommandId.QueueRepeat, "Repeat mode", "Playback"), _ => _queue?.SetRepeatMode(QueueState.Repeat switch { QueueRepeatMode.None => QueueRepeatMode.CurrentItem, QueueRepeatMode.CurrentItem => QueueRepeatMode.All, _ => QueueRepeatMode.None }), _ => _queue is not null);
        registry.Register(new(PlayerCommandId.QueueShuffle, "Shuffle queue", "Playback"), _ => _queue?.SetOrder(QueueState.Order == QueueOrder.Random ? QueueOrder.Normal : QueueOrder.Random), _ => _queue is not null);
        registry.Register(new(PlayerCommandId.LoadPlaylist, "Load playlist", "Media"), _ => _ = LoadPlaylistAsync(), _ => _mediaSourcePicker is IPlaylistDocumentPicker && _queue is not null);
        registry.Register(new(PlayerCommandId.SavePlaylist, "Save playlist", "Media"), _ => _ = SavePlaylistAsync(), _ => _mediaSourcePicker is IPlaylistDocumentPicker && QueueItems.Count > 0);
        registry.Register(new(PlayerCommandId.ToggleMediaControls, "Tracks and synchronization", "Playback"), p => { DrawerTab = p as string ?? "Audio"; IsQueueOpen = true; }, _ => _mediaControls is not null);
        registry.Register(new(PlayerCommandId.SelectAudioTrack, "Select audio track", "Audio"), p => _mediaControls?.SelectTrack(MediaTrackKind.Audio, p as string ?? string.Empty), p => CanAdjustDelays && p is string id && AudioTracks.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectSubtitleTrack, "Select subtitle track", "Subtitles"), p => _mediaControls?.SelectTrack(MediaTrackKind.Subtitle, p as string ?? string.Empty), p => CanAdjustDelays && p is string id && SubtitleTracks.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectTitle, "Select title", "Playback"), p => _mediaControls?.SelectTitle(p as string ?? string.Empty), p => p is string id && Titles.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectChapter, "Select chapter", "Playback"), p => _mediaControls?.SelectChapter(p as string ?? string.Empty), p => p is string id && Chapters.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.AdjustAudioDelay, "Adjust audio delay", "Audio"), p => _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Audio, MediaDelayPolicy.Clamp(AudioDelayMilliseconds + (int)ToDouble(p))), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.AdjustSubtitleDelay, "Adjust subtitle delay", "Subtitles"), p => _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Subtitle, MediaDelayPolicy.Clamp(SubtitleDelayMilliseconds + (int)ToDouble(p))), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.ResetAudioDelay, "Reset audio delay", "Audio"), _ => _mediaControls?.ResetDelay(MediaTrackKind.Audio), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.ResetSubtitleDelay, "Reset subtitle delay", "Subtitles"), _ => _mediaControls?.ResetDelay(MediaTrackKind.Subtitle), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.AttachExternalAudio, "Add external audio", "Audio"), _ => _ = AttachExternalTrackAsync(MediaTrackKind.Audio), _ => _mediaControls is not null && _mediaSourcePicker is IExternalTrackPicker);
        registry.Register(new(PlayerCommandId.AttachExternalSubtitle, "Add external subtitle", "Subtitles"), _ => _ = AttachExternalTrackAsync(MediaTrackKind.Subtitle), _ => _mediaControls is not null && _mediaSourcePicker is IExternalTrackPicker);
        registry.Register(new(PlayerCommandId.MediaInformation, "Media information", "Media", "Ctrl+I"), _ => { DrawerTab = "Information"; IsQueueOpen = true; _mediaInformationTransport?.RefreshMediaInformation(); }, _ => _mediaInformationTransport is not null);
        registry.Register(new(PlayerCommandId.Snapshot, "Take snapshot", "Video", "Shift+S"), _ => { MediaControlsNotice = "Snapshot unavailable: the internal engine has no safe fixed held-vout binding. No file was created."; OnPropertyChanged(nameof(HasMediaControlsNotice)); }, _ => _mediaControls is not null);

        OpenMediaCommand = new RegistryCommand(registry, PlayerCommandId.OpenMedia);
        OpenFolderCommand = new RegistryCommand(registry, PlayerCommandId.OpenFolder);
        OpenUrlCommand = new RegistryCommand(registry, PlayerCommandId.OpenUrl);
        SubmitUrlCommand = new RegistryCommand(registry, PlayerCommandId.SubmitUrl);
        RemoveRecentCommand = new RegistryCommand(registry, PlayerCommandId.RemoveRecent);
        ClearRecentCommand = new RegistryCommand(registry, PlayerCommandId.ClearRecent);
        ClearResumeHistoryCommand = new RegistryCommand(registry, PlayerCommandId.ClearResumeHistory);
        RemoveResumeCommand = new RegistryCommand(registry, PlayerCommandId.RemoveResume);
        AddFavoriteCommand = new RegistryCommand(registry, PlayerCommandId.AddFavorite);
        RemoveFavoriteCommand = new RegistryCommand(registry, PlayerCommandId.RemoveFavorite);
        ClearFavoritesCommand = new RegistryCommand(registry, PlayerCommandId.ClearFavorites);
        ClearLibraryCommand = new RegistryCommand(registry, PlayerCommandId.ClearLibrary);
        OpenLibraryCommand = new RegistryCommand(registry, PlayerCommandId.OpenLibrary);
        OpenRecentCommand = new RegistryCommand(registry, PlayerCommandId.OpenRecent);
        AcceptResumeCommand = new RegistryCommand(registry, PlayerCommandId.AcceptResume);
        DeclineResumeCommand = new RegistryCommand(registry, PlayerCommandId.DeclineResume);
        OpenTestMediaCommand = new RegistryCommand(registry, PlayerCommandId.OpenTestMedia);
        TogglePauseCommand = new RegistryCommand(registry, PlayerCommandId.TogglePause);
        StopCommand = new RegistryCommand(registry, PlayerCommandId.Stop);
        ToggleMuteCommand = new RegistryCommand(registry, PlayerCommandId.ToggleMute);
        SkipBackwardCommand = new RegistryCommand(registry, PlayerCommandId.SkipBackward);
        SkipForwardCommand = new RegistryCommand(registry, PlayerCommandId.SkipForward);
        SetPlaybackRateCommand = new RegistryCommand(registry, PlayerCommandId.SetPlaybackRate);
        ToggleFullscreenCommand = new RegistryCommand(registry, PlayerCommandId.ToggleFullscreen);
        SetAlwaysOnTopCommand = new RegistryCommand(registry, PlayerCommandId.SetAlwaysOnTop);
        OpenSettingsCommand = new RegistryCommand(registry, PlayerCommandId.OpenSettings);
        ToggleQueueCommand = new RegistryCommand(registry, PlayerCommandId.ToggleQueue);
        PreviousCommand = new RegistryCommand(registry, PlayerCommandId.QueuePrevious);
        NextCommand = new RegistryCommand(registry, PlayerCommandId.QueueNext);
        SelectQueueItemCommand = new RegistryCommand(registry, PlayerCommandId.QueueSelect);
        MoveQueueItemCommand = new RegistryCommand(registry, PlayerCommandId.QueueMove);
        RemoveQueueItemCommand = new RegistryCommand(registry, PlayerCommandId.QueueRemove);
        ClearQueueCommand = new RegistryCommand(registry, PlayerCommandId.QueueClear);
        CycleRepeatCommand = new RegistryCommand(registry, PlayerCommandId.QueueRepeat);
        ToggleShuffleCommand = new RegistryCommand(registry, PlayerCommandId.QueueShuffle);
        LoadPlaylistCommand = new RegistryCommand(registry, PlayerCommandId.LoadPlaylist);
        SavePlaylistCommand = new RegistryCommand(registry, PlayerCommandId.SavePlaylist);
        ToggleMediaControlsCommand = new RegistryCommand(registry, PlayerCommandId.ToggleMediaControls);
        SelectAudioTrackCommand = new RegistryCommand(registry, PlayerCommandId.SelectAudioTrack);
        SelectSubtitleTrackCommand = new RegistryCommand(registry, PlayerCommandId.SelectSubtitleTrack);
        SelectTitleCommand = new RegistryCommand(registry, PlayerCommandId.SelectTitle);
        SelectChapterCommand = new RegistryCommand(registry, PlayerCommandId.SelectChapter);
        AdjustAudioDelayCommand = new RegistryCommand(registry, PlayerCommandId.AdjustAudioDelay);
        AdjustSubtitleDelayCommand = new RegistryCommand(registry, PlayerCommandId.AdjustSubtitleDelay);
        ResetAudioDelayCommand = new RegistryCommand(registry, PlayerCommandId.ResetAudioDelay);
        ResetSubtitleDelayCommand = new RegistryCommand(registry, PlayerCommandId.ResetSubtitleDelay);
        AttachExternalAudioCommand = new RegistryCommand(registry, PlayerCommandId.AttachExternalAudio);
        AttachExternalSubtitleCommand = new RegistryCommand(registry, PlayerCommandId.AttachExternalSubtitle);
        MediaInformationCommand = new RegistryCommand(registry, PlayerCommandId.MediaInformation);
        CopyDiagnosticsCommand = new RelayCommand(_ => _ = CopyDiagnosticsAsync());
        SnapshotCommand = new RegistryCommand(registry, PlayerCommandId.Snapshot);
        InitializeHomeCommands(registry);
        InitializePictureInPictureCommands(registry);
    }

    public IPlayerCommandRegistry Registry { get; }

    private async Task CopyDiagnosticsAsync()
    {
        string? error = _clipboard is null ? "clipboard unavailable" : await _clipboard.SetTextAsync(MediaDiagnostics.Build(MediaInformation));
        CopyStatus = error is null ? "Diagnostics copied." : "Could not copy diagnostics: " + error;
        OnPropertyChanged(nameof(HasCopyStatus));
    }
    public ICommand OpenMediaCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand SubmitUrlCommand { get; }
    public ICommand RemoveRecentCommand { get; }
    public ICommand ClearRecentCommand { get; }
    public ICommand ClearResumeHistoryCommand { get; }
    public ICommand RemoveResumeCommand { get; }
    public ICommand AddFavoriteCommand { get; }
    public ICommand RemoveFavoriteCommand { get; }
    public ICommand ClearFavoritesCommand { get; }
    public ICommand ClearLibraryCommand { get; }
    public ICommand OpenLibraryCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand AcceptResumeCommand { get; }
    public ICommand DeclineResumeCommand { get; }
    public ICommand OpenTestMediaCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand SkipBackwardCommand { get; }
    public ICommand SkipForwardCommand { get; }
    public ICommand SetPlaybackRateCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand SetAlwaysOnTopCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleQueueCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SelectQueueItemCommand { get; }
    public ICommand MoveQueueItemCommand { get; }
    public ICommand RemoveQueueItemCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand LoadPlaylistCommand { get; }
    public ICommand SavePlaylistCommand { get; }
    public ICommand ToggleMediaControlsCommand { get; }
    public ICommand SelectAudioTrackCommand { get; }
    public ICommand SelectSubtitleTrackCommand { get; }
    public ICommand SelectTitleCommand { get; }
    public ICommand SelectChapterCommand { get; }
    public ICommand AdjustAudioDelayCommand { get; }
    public ICommand AdjustSubtitleDelayCommand { get; }
    public ICommand ResetAudioDelayCommand { get; }
    public ICommand ResetSubtitleDelayCommand { get; }
    public ICommand AttachExternalAudioCommand { get; }
    public ICommand AttachExternalSubtitleCommand { get; }
    public ICommand MediaInformationCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand SnapshotCommand { get; }
    public string SkipBackwardLabel => $"−{_settings?.Current.SkipBackwardSeconds ?? 10}";
    public string SkipForwardLabel => $"+{_settings?.Current.SkipForwardSeconds ?? 10}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Position
    {
        get => _position;
        set
        {
            if (!Set(ref _position, value) || _isUpdatingFromEngine)
                return;
            if (_isSeeking)
            {
                ElapsedText = FormatTime(_duration * value);
                OnPropertyChanged(nameof(SeekPreviewText));
            }
            else
                Registry.Execute(PlayerCommandId.Seek, value);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 200);
            if (!Set(ref _volume, value))
                return;
            OnPropertyChanged(nameof(IsVolumeAmplified));
            OnPropertyChanged(nameof(VolumeText));
            if (!_isUpdatingFromEngine) Registry.Execute(PlayerCommandId.SetVolume, value);
        }
    }

    public bool CanSeek { get; private set; }
    public bool IsSeeking => _isSeeking;
    public double SeekStep => _duration.TotalSeconds > 0 ? Math.Min(1, 5 / _duration.TotalSeconds) : .01;
    public string SeekPreviewText => FormatTime(_duration * Position);
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public PlaybackStatus PlaybackStatus
    {
        get => _playbackStatus;
        private set
        {
            if (!Set(ref _playbackStatus, value)) return;
            OnPropertyChanged(nameof(IsOpening));
            OnPropertyChanged(nameof(IsFaulted));
            OnPropertyChanged(nameof(IsNoMedia));
            OnPropertyChanged(nameof(HasPlayableMedia));
            OnPropertyChanged(nameof(ShowMiniPlayer));
            (ShowPlayerCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            (TogglePictureInPictureCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanTogglePlayback));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(PlayPauseName));
            OnPropertyChanged(nameof(PlayPauseToolTip));
            (TogglePauseCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            (ToggleMuteCommand as RegistryCommand)?.RaiseCanExecuteChanged();
        }
    }
    public bool IsOpening => PlaybackStatus == PlaybackStatus.Opening;
    public bool IsFaulted => PlaybackStatus == PlaybackStatus.Faulted;
    public bool IsNoMedia => PlaybackStatus == PlaybackStatus.Closed && _currentSource is null;
    public bool HasPlayableMedia => _currentSource is not null || PlaybackStatus is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Stopped;
    public bool CanTogglePlayback => !IsOpening && !IsFaulted && HasPlayableMedia;
    public bool CanStop => PlaybackStatus is PlaybackStatus.Opening or PlaybackStatus.Playing or PlaybackStatus.Paused;
    public string PlayPauseName => IsPlaying ? "Pause" : "Play";
    public string PlayPauseToolTip => IsPlaying ? "Pause (Space)" : "Play (Space)";
    public string MuteName => IsMuted ? "Unmute" : "Mute";
    public string FullscreenName => IsFullscreen ? "Exit fullscreen" : "Enter fullscreen";
    public string FavoriteName => IsCurrentFavorite ? "Remove from favorites" : "Add to favorites";
    public bool IsVolumeAmplified => Volume > 100;
    public string VolumeText => IsVolumeAmplified ? $"Volume {Volume:0}% · Amplified" : $"Volume {Volume:0}%";
    public bool IsAlwaysOnTop { get => _isAlwaysOnTop; set => Set(ref _isAlwaysOnTop, value); }
    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (Set(ref _isMuted, value)) OnPropertyChanged(nameof(MuteName));
        }
    }
    public double PlaybackRate { get => _playbackRate; private set => Set(ref _playbackRate, value); }
    public string MediaTitle { get => _mediaTitle; private set => Set(ref _mediaTitle, value); }
    public bool IsFullscreen { get => _isFullscreen; set { if (Set(ref _isFullscreen, value)) OnPropertyChanged(nameof(FullscreenName)); } }
    public string OpenError
    {
        get => _openError;
        private set
        {
            if (Set(ref _openError, value))
                OnPropertyChanged(nameof(HasOpenError));
        }
    }
    private string? _historyError;
    public string? HistoryError
    {
        get => _historyError;
        private set
        {
            if (Set(ref _historyError, value))
                OnPropertyChanged(nameof(HasHistoryError));
        }
    }
    public string UrlText { get => _urlText; set => Set(ref _urlText, value); }
    public bool HasPendingResume => _pendingResume is not null && _resumeReady;
    public string PendingResumeText => _pendingResume is null ? string.Empty : $"Resume from {FormatTime(_pendingResume.Position)}?";
    public IReadOnlyList<RecentMediaEntry> RecentItems => _history?.Recent ?? [];
    public bool HasRecentItems => RecentItems.Count > 0;
    public bool HasNoRecentItems => !HasRecentItems;
    public IReadOnlyList<ResumeEntry> ResumeItems => _history?.Resume ?? [];
    public bool HasResumeItems => ResumeItems.Count > 0;
    public IReadOnlyList<FavoriteMediaEntry> FavoriteItems => _history?.Favorites ?? [];
    public bool HasFavoriteItems => FavoriteItems.Count > 0;
    public bool HasLibraryItems => HasRecentItems || HasResumeItems || HasFavoriteItems;
    public bool IsCurrentFavorite => _currentSource is not null && (_history?.IsFavorite(_currentSource.CanonicalSource) ?? false);
    public bool IsLibraryOpen
    {
        get => _isLibraryOpen;
        set
        {
            if (Set(ref _isLibraryOpen, value) && value)
                IsQueueOpen = false;
        }
    }
    public string LibraryTab
    {
        get => _libraryTab;
        set
        {
            if (Set(ref _libraryTab, value))
            {
                OnPropertyChanged(nameof(IsLibraryRecentTab)); OnPropertyChanged(nameof(IsLibraryFavoritesTab)); OnPropertyChanged(nameof(IsLibraryResumeTab));
            }
        }
    }
    public bool IsLibraryRecentTab => LibraryTab == "Recent";
    public bool IsLibraryFavoritesTab => LibraryTab == "Favorites";
    public bool IsLibraryResumeTab => LibraryTab == "Resume";
    public string LibraryTitle => PlayerText.LibraryTitle;
    public string LibraryPanelAutomationName => PlayerText.LibraryPanelAutomationName;
    public string LibraryDescription => PlayerText.LibraryDescription;
    public string LibraryRecentTitle => PlayerText.LibraryRecent;
    public string LibraryFavoritesTitle => PlayerText.LibraryFavorites;
    public string LibraryResumeTitle => PlayerText.LibraryResume;
    public string LibraryNoRecent => PlayerText.LibraryNoRecent;
    public string LibraryNoFavorites => PlayerText.LibraryNoFavorites;
    public string LibraryNoResume => PlayerText.LibraryNoResume;
    public string LibraryPrivacy => PlayerText.LibraryPrivacy;
    public string LibraryMenu => PlayerText.LibraryMenu;
    public string LibraryButtonText => PlayerText.LibraryButton;
    public string LibraryButtonToolTip => PlayerText.LibraryButtonToolTip;
    public string LibraryButtonAutomationName => PlayerText.LibraryButtonAutomationName;
    public string LibraryCloseToolTip => PlayerText.LibraryCloseToolTip;
    public string LibraryCloseAutomationName => PlayerText.LibraryCloseAutomationName;
    public string LibraryRecentTabText => PlayerText.LibraryRecentTab;
    public string LibraryFavoritesTabText => PlayerText.LibraryFavoritesTab;
    public string LibraryResumeTabText => PlayerText.LibraryResumeTab;
    public string LibraryRecentTabAutomationName => PlayerText.LibraryRecentTabAutomationName;
    public string LibraryFavoritesTabAutomationName => PlayerText.LibraryFavoritesTabAutomationName;
    public string LibraryResumeTabAutomationName => PlayerText.LibraryResumeTabAutomationName;
    public string LibraryFavoriteCurrent => PlayerText.LibraryFavoriteCurrent;
    public string LibraryFavoriteCurrentToolTip => PlayerText.LibraryFavoriteCurrentToolTip;
    public string LibraryFavoriteCurrentAutomationName => PlayerText.LibraryFavoriteCurrentAutomationName;
    public string LibraryOpenRecentToolTip => PlayerText.LibraryOpenRecentToolTip;
    public string LibraryOpenRecentAutomationName => PlayerText.LibraryOpenRecentAutomationName;
    public string LibraryRemoveRecentAutomationName => PlayerText.LibraryRemoveRecentAutomationName;
    public string LibraryOpenFavoriteToolTip => PlayerText.LibraryOpenFavoriteToolTip;
    public string LibraryOpenFavoriteAutomationName => PlayerText.LibraryOpenFavoriteAutomationName;
    public string LibraryRemoveFavoriteAutomationName => PlayerText.LibraryRemoveFavoriteAutomationName;
    public string LibraryOpenResumeToolTip => PlayerText.LibraryOpenResumeToolTip;
    public string LibraryOpenResumeAutomationName => PlayerText.LibraryOpenResumeAutomationName;
    public string LibraryRemoveResumeAutomationName => PlayerText.LibraryRemoveResumeAutomationName;
    public string LibraryClearPrompt => PlayerText.LibraryClearPrompt;
    public string LibraryCancel => PlayerText.LibraryCancel;
    public string LibraryClearAll => PlayerText.LibraryClearAll;
    public string LibraryClearAllAutomationName => PlayerText.LibraryClearAllAutomationName;
    public string LibraryClear => PlayerText.LibraryClear;
    public string LibraryClearAutomationName => PlayerText.LibraryClearAutomationName;
    public bool HasOpenError => !string.IsNullOrWhiteSpace(OpenError);
    public bool HasHistoryError => !string.IsNullOrWhiteSpace(HistoryError);
    public PlaybackQueueState QueueState { get => _queueState; private set => Set(ref _queueState, value); }
    public IReadOnlyList<QueueItem> QueueItems => QueueState.Items;
    public bool IsQueueOpen
    {
        get => _isQueueOpen;
        set
        {
            if (Set(ref _isQueueOpen, value) && value)
                IsLibraryOpen = false;
        }
    }
    public bool HasQueueError => !string.IsNullOrWhiteSpace(QueueState.Error);
    public string QueueError => QueueState.Error ?? string.Empty;
    public bool IsQueueLoading => QueueState.Status == QueueLoadState.Loading;
    public bool HasQueueItems => QueueItems.Count > 0;
    public bool HasEmptyQueue => QueueState.Status == QueueLoadState.Empty;
    public string RepeatLabel => QueueState.Repeat switch { QueueRepeatMode.CurrentItem => "Repeat item", QueueRepeatMode.All => "Repeat all", QueueRepeatMode.Inconsistent => "Repeat unavailable", _ => "Repeat off" };
    public string ShuffleLabel => QueueState.Order == QueueOrder.Random ? "Shuffle on" : "Shuffle off";
    public MediaCapabilitiesState MediaCapabilities { get => _mediaCapabilities; private set => Set(ref _mediaCapabilities, value); }
    public IReadOnlyList<MediaTrack> AudioTracks => MediaCapabilities.AudioTracks;
    public IReadOnlyList<MediaTrack> SubtitleTracks => MediaCapabilities.SubtitleTracks;
    public IReadOnlyList<MediaTitle> Titles => MediaCapabilities.Titles;
    public IReadOnlyList<MediaChapter> Chapters => MediaCapabilities.Chapters;
    public bool IsMediaCapabilitiesLoading => MediaCapabilities.Status == MediaCapabilityLoadState.Loading;
    public bool HasMediaCapabilitiesError => MediaCapabilities.Status == MediaCapabilityLoadState.Failed || !string.IsNullOrWhiteSpace(MediaCapabilities.Error);
    public bool HasMediaCapabilitiesEmpty => MediaCapabilities.Status == MediaCapabilityLoadState.Empty;
    public bool AreMediaControlsUnavailable => MediaCapabilities.Status == MediaCapabilityLoadState.Unavailable;
    public string MediaCapabilitiesError => MediaCapabilities.Error ?? string.Empty;
    public string DrawerTab
    {
        get => _drawerTab;
        set
        {
            if (Set(ref _drawerTab, value))
            {
                OnPropertyChanged(nameof(IsQueueTab)); OnPropertyChanged(nameof(IsAudioTab));
                OnPropertyChanged(nameof(IsSubtitleTab)); OnPropertyChanged(nameof(IsChapterTab));
                OnPropertyChanged(nameof(IsInformationTab));
                OnPropertyChanged(nameof(IsVideoTab));
            }
        }
    }
    public bool IsQueueTab => DrawerTab == "Queue";
    public bool IsAudioTab => DrawerTab == "Audio";
    public bool IsSubtitleTab => DrawerTab == "Subtitles";
    public bool IsChapterTab => DrawerTab == "Chapters";
    public bool IsInformationTab => DrawerTab == "Information";
    public bool IsVideoTab => DrawerTab == "Video";
    public VideoAdjustmentsViewModel VideoAdjustments { get; }
    public MediaInformationState MediaInformation { get => _mediaInformation; private set => Set(ref _mediaInformation, value); }
    public string CopyStatus { get => _copyStatus; private set => Set(ref _copyStatus, value); }
    public bool HasCopyStatus => !string.IsNullOrWhiteSpace(CopyStatus);
    public bool CanAdjustDelays => _mediaControls is not null && MediaCapabilities.Status is MediaCapabilityLoadState.Ready or MediaCapabilityLoadState.Empty;
    public int AudioDelayMilliseconds
    {
        get => MediaCapabilities.Delays.AudioMilliseconds;
        set
        {
            int bounded = MediaDelayPolicy.Clamp(value);
            if (_isUpdatingCapabilities || bounded == MediaCapabilities.Delays.AudioMilliseconds) return;
            _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Audio, bounded);
        }
    }
    public int SubtitleDelayMilliseconds
    {
        get => MediaCapabilities.Delays.SubtitleMilliseconds;
        set
        {
            int bounded = MediaDelayPolicy.Clamp(value);
            if (_isUpdatingCapabilities || bounded == MediaCapabilities.Delays.SubtitleMilliseconds) return;
            _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Subtitle, bounded);
        }
    }
    public string MediaControlsNotice { get => _mediaControlsNotice; private set => Set(ref _mediaControlsNotice, value); }
    public bool HasMediaControlsNotice => !string.IsNullOrWhiteSpace(MediaControlsNotice);

    public void BeginSeek()
    {
        _isSeeking = CanSeek;
        OnPropertyChanged(nameof(IsSeeking));
        OnPropertyChanged(nameof(SeekPreviewText));
    }

    public void CommitSeek()
    {
        if (!_isSeeking)
            return;
        _isSeeking = false;
        OnPropertyChanged(nameof(IsSeeking));
        Registry.Execute(PlayerCommandId.Seek, Position);
    }

    public void ApplyEngineState(MediaEngineState state)
    {
        _lastObservedGeneration = Math.Max(_lastObservedGeneration, state.Generation);
        if (_currentSource is not null && (state.Source is null || !string.Equals(MediaHistoryService.KeyFromCanonicalSource(state.Source), MediaHistoryService.KeyFromCanonicalSource(_currentSource.CanonicalSource), StringComparison.Ordinal)))
            return;
        if (_currentSource is not null && state.Generation <= _generationFloor) return;
        if (_activeGeneration < 0 && _currentSource is not null) _activeGeneration = state.Generation;
        if (_activeGeneration >= 0 && state.Generation != _activeGeneration) return;
        _isUpdatingFromEngine = true;
        try
        {
            _lastState = state;
            if (state.Generation > _mediaInformation.QueueGeneration)
                MediaInformation = new(state.Generation, 0, MediaInformationLoadState.Loading, "", MediaInformationState.Unavailable.Metadata);
            if (state.Status == PlaybackStatus.Opening && state.Generation != _homeMediaGeneration)
            {
                _homeMediaGeneration = state.Generation;
                IsHomeOpen = false;
            }
            IsPlaying = state.Status == PlaybackStatus.Playing;
            PlaybackStatus = state.Status;
            OnPropertyChanged(nameof(PlayPauseName));
            OnPropertyChanged(nameof(PlayPauseToolTip));
            if (state.Status == PlaybackStatus.Faulted) { OpenError = "Media could not be opened."; _diagnostic?.Invoke(state.Error ?? "Media engine fault."); return; }
            _duration = state.Duration;
            OnPropertyChanged(nameof(SeekStep));
            if (!_isSeeking)
                Position = state.Position;
            CanSeek = state.CanSeek;
            OnPropertyChanged(nameof(CanSeek));
            (SkipBackwardCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            (SkipForwardCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            Volume = state.Volume * 100;
            IsMuted = state.IsMuted;
            PlaybackRate = state.PlaybackRate;
            MediaTitle = string.IsNullOrWhiteSpace(state.Title) ? "VLC Avalonia" : state.Title;
            if (!_isSeeking)
                ElapsedText = FormatTime(state.Elapsed);
            DurationText = FormatTime(state.Duration);
            if (_currentSource is not null && state.Generation > 0 && state.Generation != _recordedOpenGeneration && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
            {
                if (TryHistory(() => _history?.RecordOpened(_currentSource, _settings?.Current.RememberRecentMedia ?? true))) { _recordedOpenGeneration = state.Generation; NotifyLibrary(); }
                if (_pendingResume is not null && state.Generation == _activeGeneration && state.CanSeek && state.Duration > TimeSpan.Zero)
                { _resumeReady = true; NotifyPendingResume(); }
            }
            if (_currentSource is not null && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused && _clock() - _lastCheckpointUtc >= TimeSpan.FromSeconds(10))
                CheckpointCurrent(false);
        }
        finally { _isUpdatingFromEngine = false; }
    }

    private async Task OpenMediaAsync()
    {
        RequestMainLayout();
        string? source = await _mediaSourcePicker.PickMediaAsync();
        if (!string.IsNullOrWhiteSpace(source)) OpenSource(source, MediaSourceKind.File);
    }

    private async Task OpenFolderAsync()
    {
        RequestMainLayout();
        string? source = await _mediaSourcePicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(source)) await OpenFolderSourcesAsync(source);
    }

    public void OpenUrl() => OpenSource(UrlText, MediaSourceKind.NetworkUrl);

    private void TogglePlayback()
    {
        if (PlaybackStatus != PlaybackStatus.Stopped) { _transport.TogglePause(); return; }
        // At EOF VLC still owns the queue. Restart its selected item without replacing it.
        if (_queue is not null && QueueState.Status == QueueLoadState.Ready && QueueItems.Count > 0)
        {
            var item = QueueItems.FirstOrDefault(item => item.IsCurrent) ?? QueueItems[0];
            try { _queue.SelectAndPlay(item.Token); OpenError = string.Empty; }
            catch (Exception error) { _diagnostic?.Invoke(error.ToString()); OpenError = "Media could not be restarted."; }
            return;
        }
        // Stop tears down the core. Reopen through the normal generation-safe path,
        // from the beginning, without offering the resume position saved by Stop.
        string? source = _stoppedSource ?? _currentSource?.CanonicalSource ?? _lastState?.Source;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile) source = uri.LocalPath;
        OpenSource(source, MediaSourceKind.File, offerResume: false);
    }

    public void OpenSource(string? rawSource, MediaSourceKind kind) => OpenSource(rawSource, kind, offerResume: true);

    private void OpenSource(string? rawSource, MediaSourceKind kind, bool offerResume)
    {
        RequestMainLayout();
        MediaOpenResult result = _sourceValidator.Validate(rawSource, kind);
        if (!result.Succeeded || result.Source is null) { if (kind == MediaSourceKind.Recent && !string.IsNullOrWhiteSpace(rawSource)) TryHistory(() => _history?.RemoveRecent(rawSource)); _diagnostic?.Invoke(result.Diagnostic ?? "Media source validation failed."); OpenError = result.UserMessage ?? "Media could not be opened."; NotifyLibrary(); return; }
        CheckpointCurrent(true);
        _stoppedSource = null;
        _currentSource = result.Source;
        IsHomeOpen = false;
        PlaybackStatus = PlaybackStatus.Opening;
        _generationFloor = _lastObservedGeneration; _activeGeneration = -1; _resumeReady = false; _lastState = null; _recordedOpenGeneration = -1;
        _pendingResume = offerResume && (_settings?.Current.ResumePlayback ?? true) && (_settings?.Current.RememberResumeHistory ?? true) ? TryFindResume(_currentSource) : null;
        NotifyPendingResume();
        OpenError = string.Empty;
        try { if (_queue is not null) _queue.OpenQueue([_currentSource.CanonicalSource]); else _transport.Open(_currentSource.CanonicalSource); }
        catch (Exception e) { _diagnostic?.Invoke(e.ToString()); _currentSource = null; _pendingResume = null; PlaybackStatus = PlaybackStatus.Faulted; OpenError = "Media could not be opened."; }
        NotifyLibrary();
        OnPropertyChanged(nameof(HasRecentItems));
    }

    public void OpenDroppedSources(IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count == 0) { OpenError = "Drop one local media file to open it."; return; }
        if (sources.Count == 1) { OpenSource(sources[0], MediaSourceKind.DragDrop); return; }
        _ = OpenMultipleDroppedAsync(sources);
    }

    private async Task OpenFolderSourcesAsync(string folder)
    {
        MediaSourceExpansion expansion = await _sourceExpander.ExpandAsync(folder, MediaSourceKind.Folder);
        OpenExpandedSources(expansion, replace: true);
    }

    private async Task OpenMultipleDroppedAsync(IReadOnlyList<string> sources)
    {
        RequestMainLayout();
        await Task.Yield();
        List<MediaSource> accepted = []; List<string> failures = [];
        foreach (string source in sources)
        {
            MediaOpenResult result = _sourceValidator.Validate(source, MediaSourceKind.DragDrop);
            if (result.Succeeded && result.Source is not null) accepted.Add(result.Source); else failures.Add(result.UserMessage ?? source);
        }
        OpenExpandedSources(new(accepted, failures, false), replace: true);
    }

    private void OpenExpandedSources(MediaSourceExpansion expansion, bool replace)
    {
        if (expansion.Sources.Count == 0) { OpenError = expansion.Failures.FirstOrDefault() ?? "No usable media files were found."; return; }
        if (_queue is null) { OpenError = "The playback queue is not ready yet."; return; }
        _stoppedSource = null;
        _currentSource = expansion.Sources[0]; _generationFloor = _lastObservedGeneration; _activeGeneration = -1; _resumeReady = false; _pendingResume = null; NotifyPendingResume();
        try { if (replace) _queue.OpenQueue(expansion.Sources.Select(x => x.CanonicalSource).ToArray()); else _queue.Enqueue(expansion.Sources.Select(x => x.CanonicalSource)); }
        catch (Exception exception) { _diagnostic?.Invoke(exception.ToString()); OpenError = "Media could not be queued."; return; }
        OpenError = expansion.Failures.Count > 0 || expansion.WasBounded ? $"Queued {expansion.Sources.Count} item(s). {expansion.Failures.Count} item(s) could not be read{(expansion.WasBounded ? "; folder limit reached" : string.Empty)}." : string.Empty;
    }

    private async Task LoadPlaylistAsync()
    {
        RequestMainLayout();
        if (_mediaSourcePicker is not IPlaylistDocumentPicker picker) return;
        string? path = await picker.PickPlaylistToOpenAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string content = await File.ReadAllTextAsync(path);
            List<MediaSource> sources = []; List<string> failures = [];
            string baseDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            foreach (string entry in M3uPlaylistDocument.Parse(content))
            {
                string candidate = Uri.TryCreate(entry, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? entry : Path.IsPathRooted(entry) ? entry : Path.Combine(baseDirectory, entry);
                MediaOpenResult result = _sourceValidator.Validate(candidate, MediaSourceKind.File);
                if (result.Succeeded && result.Source is not null) sources.Add(result.Source); else failures.Add(result.UserMessage ?? entry);
            }
            OpenExpandedSources(new(sources, failures, false), replace: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        { _diagnostic?.Invoke(exception.ToString()); OpenError = "That playlist could not be loaded safely."; }
    }

    private async Task SavePlaylistAsync()
    {
        if (_mediaSourcePicker is not IPlaylistDocumentPicker picker) return;
        string? path = await picker.PickPlaylistToSaveAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await File.WriteAllTextAsync(path, M3uPlaylistDocument.Write(QueueItems.Select(x => x.Source))); OpenError = string.Empty; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        { _diagnostic?.Invoke(exception.ToString()); OpenError = "That playlist could not be saved."; }
    }

    internal void ApplyQueueState(PlaybackQueueState state)
    {
        if (state.Generation < QueueState.Generation || (_activeGeneration >= 0 && state.Generation > 0 && state.Generation != _activeGeneration)) return;
        QueueState = state;
        OnPropertyChanged(nameof(QueueItems)); OnPropertyChanged(nameof(HasQueueError)); OnPropertyChanged(nameof(QueueError)); OnPropertyChanged(nameof(IsQueueLoading)); OnPropertyChanged(nameof(HasQueueItems)); OnPropertyChanged(nameof(HasEmptyQueue)); OnPropertyChanged(nameof(RepeatLabel)); OnPropertyChanged(nameof(ShuffleLabel));
        foreach (RegistryCommand? command in new[] { PreviousCommand, NextCommand, SelectQueueItemCommand, MoveQueueItemCommand, RemoveQueueItemCommand, ClearQueueCommand, CycleRepeatCommand, ToggleShuffleCommand }.OfType<RegistryCommand>()) command.RaiseCanExecuteChanged();
        (SavePlaylistCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }

    internal void ApplyMediaCapabilities(MediaCapabilitiesState state)
    {
        if (state.QueueGeneration < MediaCapabilities.QueueGeneration ||
            (_activeGeneration >= 0 && state.QueueGeneration > 0 && state.QueueGeneration != _activeGeneration)) return;
        if (state.QueueGeneration == MediaCapabilities.QueueGeneration && state.MediaGeneration < MediaCapabilities.MediaGeneration) return;
        if (state.QueueGeneration > MediaInformation.QueueGeneration ||
            (state.QueueGeneration == MediaInformation.QueueGeneration && state.MediaGeneration > MediaInformation.MediaGeneration))
            MediaInformation = new(state.QueueGeneration, state.MediaGeneration, MediaInformationLoadState.Stale, "", MediaInformationState.Unavailable.Metadata);
        _isUpdatingCapabilities = true;
        try { MediaCapabilities = state; }
        finally { _isUpdatingCapabilities = false; }
        foreach (string property in new[] { nameof(AudioTracks), nameof(SubtitleTracks), nameof(Titles), nameof(Chapters), nameof(IsMediaCapabilitiesLoading), nameof(HasMediaCapabilitiesError), nameof(HasMediaCapabilitiesEmpty), nameof(AreMediaControlsUnavailable), nameof(MediaCapabilitiesError), nameof(AudioDelayMilliseconds), nameof(SubtitleDelayMilliseconds), nameof(CanAdjustDelays) }) OnPropertyChanged(property);
        foreach (RegistryCommand? command in new[] { SelectAudioTrackCommand, SelectSubtitleTrackCommand, SelectTitleCommand, SelectChapterCommand, AdjustAudioDelayCommand, AdjustSubtitleDelayCommand, ResetAudioDelayCommand, ResetSubtitleDelayCommand, AttachExternalAudioCommand, AttachExternalSubtitleCommand }.OfType<RegistryCommand>()) command.RaiseCanExecuteChanged();
    }

    private async Task AttachExternalTrackAsync(MediaTrackKind kind)
    {
        if (_mediaSourcePicker is not IExternalTrackPicker picker || _mediaControls is null) return;
        string? path = await picker.PickExternalTrackAsync(kind);
        if (string.IsNullOrWhiteSpace(path)) return;
        string extension = Path.GetExtension(path);
        bool valid = kind == MediaTrackKind.Subtitle
            ? extension is ".srt" or ".ass" or ".ssa" or ".vtt" or ".sub"
            : extension is ".mp3" or ".m4a" or ".aac" or ".flac" or ".wav" or ".ogg";
        if (!valid) { MediaControlsNotice = kind == MediaTrackKind.Subtitle ? "Choose a supported subtitle file." : "Choose a supported audio file."; OnPropertyChanged(nameof(HasMediaControlsNotice)); return; }
        ExternalTrackAttachmentState result = _mediaControls.AttachExternalTrack(kind, path);
        MediaControlsNotice = result.AcceptedIntoItem && result.PendingUntilNextOpen
            ? "Added for the next media open. It is not a current live track."
            : result.Error ?? "The external track could not be added.";
        OnPropertyChanged(nameof(HasMediaControlsNotice));
    }
    private void NotifyRecent()
    {
        OnPropertyChanged(nameof(RecentItems)); OnPropertyChanged(nameof(HasRecentItems)); OnPropertyChanged(nameof(HasNoRecentItems)); (ClearRecentCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }
    private void NotifyResume()
    {
        OnPropertyChanged(nameof(ResumeItems)); OnPropertyChanged(nameof(HasResumeItems));
        (ClearResumeHistoryCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }
    private void NotifyLibrary()
    {
        NotifyHome();
        NotifyRecent(); NotifyResume();
        OnPropertyChanged(nameof(FavoriteItems)); OnPropertyChanged(nameof(HasFavoriteItems)); OnPropertyChanged(nameof(HasLibraryItems)); OnPropertyChanged(nameof(IsCurrentFavorite)); OnPropertyChanged(nameof(FavoriteName));
        (ClearFavoritesCommand as RegistryCommand)?.RaiseCanExecuteChanged(); (ClearLibraryCommand as RegistryCommand)?.RaiseCanExecuteChanged(); (AddFavoriteCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }

    private void AddFavorite(string? source)
    {
        MediaSource? favorite = _currentSource;
        if (!string.IsNullOrWhiteSpace(source)) favorite = _sourceValidator.Validate(source, MediaSourceKind.Recent).Source;
        if (favorite is null) return;
        if (string.IsNullOrWhiteSpace(source) && IsCurrentFavorite)
        {
            TryHistory(() => _history?.RemoveFavorite(favorite.CanonicalSource));
            NotifyLibrary();
            return;
        }
        TryHistory(() => _history?.AddFavorite(favorite)); NotifyLibrary();
    }
    private void NotifyPendingResume()
    {
        OnPropertyChanged(nameof(HasPendingResume));
        OnPropertyChanged(nameof(PendingResumeText));
        (AcceptResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged();
        (DeclineResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }

    private void ApplyResume()
    {
        if (_pendingResume is not { } resume) return;
        double duration = Math.Max(_duration.TotalSeconds, 1);
        bool ok = true; TryHistory(() => _history?.RemoveResume(resume.Source), () => ok = false); if (!ok) return;
        _transport.Seek(Math.Clamp(resume.Position.TotalSeconds / duration, 0, 1));
        _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary();
    }

    public void CheckpointCurrent(bool force)
    {
        if (_currentSource is null || _lastState is not { } state) return;
        DateTimeOffset now = _clock();
        if (_activeGeneration < 0 || state.Generation != _activeGeneration || state.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused) || !state.CanSeek || state.Duration <= TimeSpan.Zero || !(_settings?.Current.RememberResumeHistory ?? true) || (!force && now - _lastCheckpointUtc < TimeSpan.FromSeconds(10))) return;
        if (TryHistory(() => _history?.Checkpoint(_currentSource, state.Elapsed, state.Duration, true, _settings?.Current.ResumeMinimumElapsedSeconds ?? 30, _settings?.Current.ResumeMaximumRemainingSeconds ?? 30, true))) { _lastCheckpointUtc = now; NotifyLibrary(); }
    }
    public void OnClosing() => CheckpointCurrent(true);
    private void DeclineResume()
    {
        bool ok = true; if (_pendingResume is { } resume) TryHistory(() => _history?.RemoveResume(resume.Source), () => ok = false); if (!ok) return;
        _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary();
    }

    private ResumeEntry? TryFindResume(MediaSource source)
    {
        ResumeEntry? result = null; TryHistory(() => result = _history?.FindEligibleResume(source, _settings?.Current.ResumeMinimumElapsedSeconds ?? 30, _settings?.Current.ResumeMaximumRemainingSeconds ?? 30)); return result;
    }
    private bool TryHistory(Action action, Action? failed = null)
    {
        try { action(); HistoryError = null; return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { HistoryError = "Media history could not be saved."; _diagnostic?.Invoke(e.ToString()); failed?.Invoke(); return false; }
    }

    private static double ToDouble(object? parameter) => parameter switch
    {
        double value => value,
        float value => value,
        int value => value,
        string value when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => 0,
    };

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

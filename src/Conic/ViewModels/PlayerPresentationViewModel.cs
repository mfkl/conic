using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using Conic.Commands;
using Conic.Services;

namespace Conic.ViewModels;

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
    private string _mediaTitle = "Conic";
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
    private const int MaximumVolumeAttempts = 5;
    private readonly IVolumeMemory? _volumeMemory;
    private RememberedVolume _wantedVolume = RememberedVolume.Default;
    private RememberedVolume? _savedVolume;
    private long _volumeSyncedGeneration = -1;
    private long _volumeAttemptGeneration = -1;
    private int _volumeAttempts;

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
        Action? showUrlPanel = null, IClipboardService? clipboard = null,
        IVolumeMemory? volumeMemory = null)
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
        _volumeMemory = volumeMemory;
        if (volumeMemory is not null)
        {
            // Remember volume: the last session's volume and mute, else the
            // startup volume (Settings › Audio), unmuted; never above the maximum.
            _savedVolume = (settings?.Current.RememberVolume ?? false) ? volumeMemory.Load() : null;
            _wantedVolume = _savedVolume ?? (settings is null ? RememberedVolume.Default : new(settings.Current.StartupVolume, false));
            _wantedVolume = _wantedVolume with { Volume = Math.Min(_wantedVolume.Volume, MaximumVolume) };
            _volume = _wantedVolume.Volume;
            _isMuted = _wantedVolume.Muted;
        }
        // Settings › Interface › Keep window above other windows: the More
        // menu's Always on Top, remembered across runs.
        _isAlwaysOnTop = settings?.Current.KeepOnTop ?? false;
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
            OnPropertyChanged(nameof(SkipBackwardLabel)); OnPropertyChanged(nameof(SkipForwardLabel)); OnPropertyChanged(nameof(SkipBackwardMenuText)); OnPropertyChanged(nameof(SkipForwardMenuText)); OnPropertyChanged(nameof(RecentItems));
            ApplyVolumeSettings();
            Set(ref _isAlwaysOnTop, updated.KeepOnTop, nameof(IsAlwaysOnTop));
            OnPropertyChanged(nameof(TitleBarText));
        };
        PlayerCommandRegistry registry = new();
        Registry = registry;
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => _ = OpenMediaAsync());
        registry.Register(new(PlayerCommandId.OpenFolder, "Open folder", "Media", "Ctrl+Shift+O"), _ => _ = OpenFolderAsync(), _ => _queue is not null);
        registry.Register(new(PlayerCommandId.OpenUrl, "Open network URL", "Media", "Ctrl+L"), _ => showUrlPanel?.Invoke(), _ => showUrlPanel is not null);
        registry.Register(new(PlayerCommandId.SubmitUrl, "Submit network URL", "Media", TakesValue: true), _ => OpenUrl());
        registry.Register(new(PlayerCommandId.RemoveRecent, "Remove recent media", "Media", TakesValue: true), p => { TryHistory(() => _history?.RemoveRecent(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.ClearRecent, "Clear recent media", "Media"), _ => { TryHistory(() => _history?.ClearRecent()); NotifyLibrary(); }, _ => HasRecentItems);
        registry.Register(new(PlayerCommandId.ClearResumeHistory, "Clear resume history", "Media"), _ => { if (TryHistory(() => _history?.ClearResume())) { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary(); } }, _ => HasResumeItems);
        registry.Register(new(PlayerCommandId.RemoveResume, PlayerText.CommandRemoveResume, "Library", TakesValue: true), p => { TryHistory(() => _history?.RemoveResume(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.AddFavorite, PlayerText.CommandAddFavorite, "Library"), p => AddFavorite(p as string), p => _history is not null && (p is string || _currentSource is not null));
        registry.Register(new(PlayerCommandId.RemoveFavorite, PlayerText.CommandRemoveFavorite, "Library", TakesValue: true), p => { TryHistory(() => _history?.RemoveFavorite(p as string ?? string.Empty)); NotifyLibrary(); }, p => !string.IsNullOrWhiteSpace(p as string));
        registry.Register(new(PlayerCommandId.ClearFavorites, PlayerText.CommandClearFavorites, "Library"), _ => { TryHistory(() => _history?.ClearFavorites()); NotifyLibrary(); }, _ => HasFavoriteItems);
        registry.Register(new(PlayerCommandId.ClearLibrary, PlayerText.CommandClearLibrary, "Library"), _ => { if (TryHistory(() => _history?.ClearAll())) { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary(); } }, _ => HasLibraryItems);
        registry.Register(new(PlayerCommandId.OpenLibrary, PlayerText.LibraryMenu, "Library", "Ctrl+Shift+L"), _ => IsLibraryOpen = !IsLibraryOpen, _ => _history is not null);
        registry.Register(new(PlayerCommandId.OpenRecent, "Open recent media", "Media", TakesValue: true), parameter => OpenSource(parameter as string, MediaSourceKind.Recent));
        registry.Register(new(PlayerCommandId.AcceptResume, "Resume playback", "Media"), _ => ApplyResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.DeclineResume, "Start from beginning", "Media"), _ => DeclineResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.DismissResume, "Dismiss resume prompt", "Media"), _ => DismissResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.OpenTestMedia, "Open test video", "Media", TakesValue: true), _ => _openTestMedia());
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => TogglePlayback(), _ => CanTogglePlayback);
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback", "S"), _ =>
        {
            CheckpointCurrent(true);
            _stoppedSource = QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source ?? _currentSource?.CanonicalSource ?? _lastState?.Source;
            _stopRequested = true;
            _transport.Stop();
        }, _ => CanStop);
        registry.Register(new(PlayerCommandId.Seek, "Seek", "Playback", TakesValue: true), parameter => _transport.Seek(ToDouble(parameter)));
        registry.Register(new(PlayerCommandId.SetVolume, "Set volume", "Audio", TakesValue: true), parameter =>
        {
            double volume = Math.Clamp(ToDouble(parameter), 0, MaximumVolume);
            _wantedVolume = _wantedVolume with { Volume = volume };
            _transport.SetVolume(volume / 100);
        }, _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.ToggleMute, "Mute", "Audio", "M"), _ =>
        {
            _wantedVolume = _wantedVolume with { Muted = !IsMuted };
            _transport.SetMuted(!IsMuted);
        }, _ => HasPlayableMedia);
        // Settings › Controls › Skip Amounts, with VLC's keys: Shift for the
        // very short skip, Alt for the short one, Ctrl for medium, Ctrl+Alt for long.
        foreach ((PlayerCommandId id, string name, string gesture) in new[]
        {
            (PlayerCommandId.SkipBackward, "Skip backward", "Alt+Left"), (PlayerCommandId.SkipForward, "Skip forward", "Alt+Right"),
            (PlayerCommandId.SkipBackwardVeryShort, "Very short skip backward", "Shift+Left"), (PlayerCommandId.SkipForwardVeryShort, "Very short skip forward", "Shift+Right"),
            (PlayerCommandId.SkipBackwardMedium, "Medium skip backward", "Ctrl+Left"), (PlayerCommandId.SkipForwardMedium, "Medium skip forward", "Ctrl+Right"),
            (PlayerCommandId.SkipBackwardLong, "Long skip backward", "Ctrl+Alt+Left"), (PlayerCommandId.SkipForwardLong, "Long skip forward", "Ctrl+Alt+Right"),
        })
            registry.Register(new(id, name, "Playback", gesture), _ => _transport.Skip(TimeSpan.FromSeconds(SkipSeconds(id))), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SetPlaybackRate, "Playback speed", "Playback", TakesValue: true), parameter => _transport.SetPlaybackRate(Math.Clamp(ToDouble(parameter), 1d / 32, 31.25)));
        registry.Register(new(PlayerCommandId.ToggleFullscreen, "Fullscreen", "Window", "F11"), _ => toggleFullscreen?.Invoke(), _ => toggleFullscreen is not null);
        registry.Register(new(PlayerCommandId.SetAlwaysOnTop, "Always on top", "Window", TakesValue: true), parameter => IsAlwaysOnTop = parameter is true);
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "Window", "Ctrl+,"), _ => openSettings?.Invoke(), _ => openSettings is not null);
        registry.Register(new(PlayerCommandId.ToggleQueue, "Playback queue", "Playback", "Ctrl+J"), _ => { if (IsQueueOpen && IsQueuePanel) IsQueueOpen = false; else { if (!IsQueuePanel) DrawerTab = "Queue"; IsQueueOpen = true; } }, _ => _queue is not null);
        registry.Register(new(PlayerCommandId.QueuePrevious, "Previous item", "Playback", "P"), _ => _queue?.Previous(), _ => QueueState.CanPrevious);
        registry.Register(new(PlayerCommandId.QueueNext, "Next item", "Playback", "N"), _ => _queue?.Next(), _ => QueueState.CanNext);
        registry.Register(new(PlayerCommandId.QueueSelect, "Play queue item", "Playback", TakesValue: true), p => _queue?.SelectAndPlay(p as string ?? string.Empty), p => p is string token && QueueItems.Any(x => x.Token == token));
        registry.Register(new(PlayerCommandId.QueueMove, "Move queue item", "Playback", TakesValue: true), p => { if (p is QueueMove move) _queue?.Move(move.Token, move.DestinationIndex); }, p => p is QueueMove move && QueueItems.Any(x => x.Token == move.Token));
        registry.Register(new(PlayerCommandId.QueueRemove, "Remove queue item", "Playback", TakesValue: true), p => _queue?.Remove(p as string ?? string.Empty), p => p is string token && QueueItems.Any(x => x.Token == token));
        registry.Register(new(PlayerCommandId.QueueClear, "Clear queue", "Playback"), _ => { _stopRequested = true; _queue?.ClearQueue(); }, _ => QueueItems.Count > 0);
        // Repeat and shuffle are remembered (Settings › Playback › Order): the
        // next queue, and the next run, start in the mode chosen last.
        registry.Register(new(PlayerCommandId.QueueRepeat, "Repeat mode", "Playback"), _ =>
        {
            QueueRepeatMode next = QueueState.Repeat switch { QueueRepeatMode.None => QueueRepeatMode.CurrentItem, QueueRepeatMode.CurrentItem => QueueRepeatMode.All, _ => QueueRepeatMode.None };
            _queue?.SetRepeatMode(next);
            RememberSetting(s => s with { QueueRepeat = QueueModeSettings.Setting(next) });
        }, _ => _queue is not null);
        registry.Register(new(PlayerCommandId.QueueShuffle, "Shuffle queue", "Playback"), _ =>
        {
            bool shuffle = QueueState.Order != QueueOrder.Random;
            _queue?.SetOrder(QueueModeSettings.Order(shuffle));
            RememberSetting(s => s with { QueueShuffle = shuffle });
        }, _ => _queue is not null);
        registry.Register(new(PlayerCommandId.LoadPlaylist, "Load playlist", "Media"), _ => _ = LoadPlaylistAsync(), _ => _mediaSourcePicker is IPlaylistDocumentPicker && _queue is not null);
        registry.Register(new(PlayerCommandId.SavePlaylist, "Save playlist", "Media"), _ => _ = SavePlaylistAsync(), _ => _mediaSourcePicker is IPlaylistDocumentPicker && QueueItems.Count > 0);
        registry.Register(new(PlayerCommandId.ToggleMediaControls, "Tracks and synchronization", "Playback"), p => { DrawerTab = p as string ?? "Audio"; IsQueueOpen = true; }, _ => _mediaControls is not null);
        registry.Register(new(PlayerCommandId.SelectAudioTrack, "Select audio track", "Audio", TakesValue: true), p => _mediaControls?.SelectTrack(MediaTrackKind.Audio, p as string ?? string.Empty), p => CanAdjustDelays && p is string id && AudioTracks.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectSubtitleTrack, "Select subtitle track", "Subtitles", TakesValue: true), p => _mediaControls?.SelectTrack(MediaTrackKind.Subtitle, p as string ?? string.Empty), p => CanAdjustDelays && p is string id && SubtitleTracks.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectTitle, "Select title", "Playback", TakesValue: true), p => _mediaControls?.SelectTitle(p as string ?? string.Empty), p => p is string id && Titles.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.SelectChapter, "Select chapter", "Playback", TakesValue: true), p => _mediaControls?.SelectChapter(p as string ?? string.Empty), p => p is string id && Chapters.Any(x => x.Id == id));
        registry.Register(new(PlayerCommandId.AdjustAudioDelay, "Adjust audio delay", "Audio", TakesValue: true), p => _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Audio, MediaDelayPolicy.Clamp(AudioDelayMilliseconds + (int)ToDouble(p))), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.AdjustSubtitleDelay, "Adjust subtitle delay", "Subtitles", TakesValue: true), p => _mediaControls?.SetDelayMilliseconds(MediaTrackKind.Subtitle, MediaDelayPolicy.Clamp(SubtitleDelayMilliseconds + (int)ToDouble(p))), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.ResetAudioDelay, "Reset audio delay", "Audio"), _ => _mediaControls?.ResetDelay(MediaTrackKind.Audio), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.ResetSubtitleDelay, "Reset subtitle delay", "Subtitles"), _ => _mediaControls?.ResetDelay(MediaTrackKind.Subtitle), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.AttachExternalAudio, "Add external audio", "Audio"), _ => _ = AttachExternalTrackAsync(MediaTrackKind.Audio), _ => _mediaControls is not null && _mediaSourcePicker is IExternalTrackPicker);
        registry.Register(new(PlayerCommandId.AttachExternalSubtitle, "Add external subtitle", "Subtitles"), _ => _ = AttachExternalTrackAsync(MediaTrackKind.Subtitle), _ => _mediaControls is not null && _mediaSourcePicker is IExternalTrackPicker);
        registry.Register(new(PlayerCommandId.MediaInformation, "Media information", "Media", "Ctrl+I"), _ => { DrawerTab = "Information"; IsQueueOpen = true; _mediaInformationTransport?.RefreshMediaInformation(); }, _ => _mediaInformationTransport is not null);
        // VLC writes the file (Settings › Video › Snapshots) and shows its name over the video.
        registry.Register(new(PlayerCommandId.Snapshot, "Take snapshot", "Video", "Shift+S"), _ =>
        {
            bool requested = _transport is IVideoSnapshotTransport snapshots && snapshots.RequestSnapshot();
            string? folder = _settings?.Current.SnapshotFolder;
            MediaControlsNotice = requested ? $"Snapshot saved to {(string.IsNullOrEmpty(folder) ? "your Pictures folder" : folder)}." : "No video to take a snapshot of.";
            OnPropertyChanged(nameof(HasMediaControlsNotice));
        }, _ => _transport is IVideoSnapshotTransport && PlaybackStatus is PlaybackStatus.Playing or PlaybackStatus.Paused);

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
        InitializeMessagesCommand(registry);
        InitializeHotkeyCommands(registry);
    }

    public IPlayerCommandRegistry Registry { get; }

    /// <summary>Raised when playback stops by itself at the end of the queue (or of an item, with Stop after each item).</summary>
    public event EventHandler? PlaybackFinished;
    private bool _stopRequested;

    private void RememberSetting(Func<PlayerSettings, PlayerSettings> update)
    {
        try { _settings?.Update(update); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _diagnostic?.Invoke(exception.ToString()); }
    }

    /// <summary>How far a skip command moves, in seconds: negative is backward.</summary>
    internal int SkipSeconds(PlayerCommandId command)
    {
        PlayerSettings settings = _settings?.Current ?? PlayerSettings.Default;
        return command switch
        {
            PlayerCommandId.SkipBackward => -settings.SkipBackwardSeconds,
            PlayerCommandId.SkipForward => settings.SkipForwardSeconds,
            PlayerCommandId.SkipBackwardVeryShort => -settings.VeryShortSkipSeconds,
            PlayerCommandId.SkipForwardVeryShort => settings.VeryShortSkipSeconds,
            PlayerCommandId.SkipBackwardMedium => -settings.MediumSkipSeconds,
            PlayerCommandId.SkipForwardMedium => settings.MediumSkipSeconds,
            PlayerCommandId.SkipBackwardLong => -settings.LongSkipSeconds,
            PlayerCommandId.SkipForwardLong => settings.LongSkipSeconds,
            _ => 0,
        };
    }

    /// <summary>
    /// Settings › Subtitles › Show feedback for shortcuts: what a shortcut
    /// is about to do, said before it runs (its result reaches the view
    /// model later, from the engine). Null for commands that show their own
    /// result, such as a panel or fullscreen.
    /// </summary>
    internal string? ShortcutFeedbackText(PlayerCommandId command) => command switch
    {
        PlayerCommandId.TogglePause => IsPlaying ? "Pause" : "Play",
        PlayerCommandId.Stop => "Stop",
        PlayerCommandId.ToggleMute => IsMuted ? $"Volume {Volume:0}%" : "Muted",
        PlayerCommandId.QueuePrevious => "Previous",
        PlayerCommandId.QueueNext => "Next",
        PlayerCommandId.QueueRepeat => QueueState.Repeat switch { QueueRepeatMode.None => "Repeat item", QueueRepeatMode.CurrentItem => "Repeat all", _ => "Repeat off" },
        PlayerCommandId.QueueShuffle => QueueState.Order == QueueOrder.Random ? "Shuffle off" : "Shuffle on",
        _ when SkipSeconds(command) is int seconds and not 0 => SkipText(seconds),
        _ => HotkeyFeedbackText(command),
    };

    /// <summary>Settings › Controls › Mouse: a wheel move over the video. Returns what it did, for the feedback.</summary>
    internal string? ApplyWheel(WheelStep step)
    {
        switch (step.Target)
        {
            case WheelTarget.Volume when HasPlayableMedia:
                double volume = Math.Clamp(Math.Round(Volume + step.Amount), 0, MaximumVolume);
                Registry.Execute(PlayerCommandId.SetVolume, volume);
                return $"Volume {volume:0}%";
            case WheelTarget.Seek when CanSeek:
                _transport.Skip(TimeSpan.FromSeconds(step.Amount));
                return SkipText(step.Amount);
            default:
                return null;
        }
    }

    private static string SkipText(double seconds) => (seconds < 0 ? "−" : "+") + Math.Abs(seconds).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " s";

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
    // Right-click menu labels (design: Title Case options).
    public string SkipBackwardMenuText => $"Skip Back {_settings?.Current.SkipBackwardSeconds ?? 10} Seconds";
    public string SkipForwardMenuText => $"Skip Forward {_settings?.Current.SkipForwardSeconds ?? 10} Seconds";

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
            // The engine may report more than the maximum (another app, an
            // earlier session); the slider and the commands stay within it.
            value = Math.Clamp(value, 0, _isUpdatingFromEngine ? AudioEngineOptions.MaximumVolumeLimit : MaximumVolume);
            if (!Set(ref _volume, value))
                return;
            OnPropertyChanged(nameof(IsVolumeAmplified));
            OnPropertyChanged(nameof(VolumeText));
            OnPropertyChanged(nameof(VolumeLevel));
            if (!_isUpdatingFromEngine) Registry.Execute(PlayerCommandId.SetVolume, value);
        }
    }

    public bool CanSeek { get; private set; }
    public bool IsSeeking => _isSeeking;
    public double SeekStep => _duration.TotalSeconds > 0 ? Math.Min(1, 5 / _duration.TotalSeconds) : .01;
    public string SeekPreviewText => FormatTime(_duration * Position);
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    public TimeSpan Duration => _duration;
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
    /// <summary>Settings › Audio › Maximum volume, in percent (100 to 300).</summary>
    public double MaximumVolume => _settings?.Current.MaximumVolume ?? PlayerSettings.Default.MaximumVolume;
    /// <summary>Settings › Audio › Volume step: the arrow keys' step on the volume slider.</summary>
    public double VolumeStep => _settings?.Current.VolumeStep ?? PlayerSettings.Default.VolumeStep;
    public string VolumeHelpText => $"0 to {MaximumVolume:0} percent. Values above 100 percent are amplified.";
    public string VolumeText => IsVolumeAmplified ? $"Volume {Volume:0}% · Amplified" : $"Volume {Volume:0}%";
    public VolumeLevel VolumeLevel => VolumeLevels.For(IsMuted, Volume);
    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set { if (Set(ref _isAlwaysOnTop, value) && _settings is not null && _settings.Current.KeepOnTop != value) RememberSetting(s => s with { KeepOnTop = value }); }
    }
    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (!Set(ref _isMuted, value)) return;
            OnPropertyChanged(nameof(MuteName));
            OnPropertyChanged(nameof(VolumeLevel));
        }
    }
    public double PlaybackRate
    {
        get => _playbackRate;
        private set
        {
            if (!Set(ref _playbackRate, value)) return;
            OnPropertyChanged(nameof(PlaybackSpeed));
            OnPropertyChanged(nameof(PlaybackRateText));
        }
    }
    public string MediaTitle { get => _mediaTitle; private set { if (Set(ref _mediaTitle, value)) OnPropertyChanged(nameof(TitleBarText)); } }
    /// <summary>The title bar's text: the media's name, or the app's with
    /// Settings › Interface › Show the file name in the title bar off.</summary>
    public string TitleBarText => _settings?.Current.ShowFileNameInTitle == false ? AppName : MediaTitle;
    private const string AppName = "Conic";
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
            if (!Set(ref _isQueueOpen, value)) return;
            if (value) IsLibraryOpen = false;
            NotifyDrawerPanel();
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
                NotifyDrawerPanel();
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
        if (_currentSource is not null && (state.Source is null || !string.Equals(MediaHistoryService.KeyFromCanonicalSource(state.Source), MediaHistoryService.KeyFromCanonicalSource(_currentSource.CanonicalSource), StringComparison.Ordinal)) &&
            !FollowQueueItem(state))
            return;
        if (_currentSource is not null && state.Generation <= _generationFloor) return;
        if (_activeGeneration < 0 && _currentSource is not null) _activeGeneration = state.Generation;
        if (_activeGeneration >= 0 && state.Generation != _activeGeneration) return;
        _isUpdatingFromEngine = true;
        try
        {
            // Another item: VLC's A-B loop belongs to the media it was set on.
            if (_lastState?.Source != state.Source) ClearABLoop();
            _lastState = state;
            UpdateNowPlaying();
            if (state.Generation > _mediaInformation.QueueGeneration)
                MediaInformation = new(state.Generation, 0, MediaInformationLoadState.Loading, "", MediaInformationState.Unavailable.Metadata);
            if (state.Status == PlaybackStatus.Opening && state.Generation != _homeMediaGeneration)
            {
                _homeMediaGeneration = state.Generation;
                IsHomeOpen = false;
            }
            IsPlaying = state.Status == PlaybackStatus.Playing;
            PlaybackStatus previousStatus = PlaybackStatus;
            PlaybackStatus = state.Status;
            // The queue reached its end (or its item, with Stop after each
            // item) by itself, not through Stop or Clear.
            if (state.Status == PlaybackStatus.Stopped && previousStatus is PlaybackStatus.Playing or PlaybackStatus.Paused && !_stopRequested && _currentSource is not null)
                PlaybackFinished?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(PlayPauseName));
            OnPropertyChanged(nameof(PlayPauseToolTip));
            if (state.Status == PlaybackStatus.Faulted) { OpenError = "Media could not be opened."; _diagnostic?.Invoke(state.Error ?? "Media engine fault."); return; }
            _duration = state.Duration;
            OnPropertyChanged(nameof(SeekStep));
            if (!_isSeeking)
                Position = state.Position;
            CanSeek = state.CanSeek;
            OnPropertyChanged(nameof(CanSeek));
            FollowABLoop(state);
            (SkipBackwardCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            (SkipForwardCommand as RegistryCommand)?.RaiseCanExecuteChanged();
            RaiseHotkeyCommands();
            Volume = state.Volume * 100;
            IsMuted = state.IsMuted;
            SyncVolume(state);
            PlaybackRate = state.PlaybackRate;
            MediaTitle = string.IsNullOrWhiteSpace(state.Title) ? "Conic" : state.Title;
            if (!_isSeeking)
                ElapsedText = FormatTime(state.Elapsed);
            DurationText = FormatTime(state.Duration);
            if (_currentSource is not null && state.Generation > 0 && state.Generation != _recordedOpenGeneration && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
            {
                if (TryHistory(() => _history?.RecordOpened(_currentSource, _settings?.Current.RememberRecentMedia ?? true))) { _recordedOpenGeneration = state.Generation; NotifyLibrary(); }
            }
            // VLC reports the playlist running before the demuxer knows the
            // length, so the resume waits for a state that can seek, not the first one.
            if (_pendingResume is not null && !_resumeReady && _currentSource is not null && state.Generation > 0 && state.Generation == _activeGeneration &&
                state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused && state.CanSeek && state.Duration > TimeSpan.Zero)
            {
                _resumeReady = true;
                // Continue where you left off: Always resumes without asking.
                if (_settings?.Current.ResumeAutomatically == true) ApplyResume();
                else NotifyPendingResume();
            }
            if (_currentSource is not null && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused && _clock() - _lastCheckpointUtc >= TimeSpan.FromSeconds(10))
                CheckpointCurrent(false);
        }
        finally { _isUpdatingFromEngine = false; }
    }

    /// <summary>
    /// The queue moved on within this open, by itself or through Next, Previous
    /// or a queue row: VLC plays another item, and Recent, Continue, the title
    /// and favorites follow it. The last state still describes the item that
    /// played, so its position is saved first.
    /// </summary>
    private bool FollowQueueItem(MediaEngineState state)
    {
        if (state.Source is null || _activeGeneration < 0 || state.Generation != _activeGeneration) return false;
        MediaOpenResult result = _sourceValidator.Validate(state.Source, MediaSourceKind.File);
        CheckpointCurrent(true);
        _currentSource = result.Succeeded && result.Source is not null ? result.Source : new(state.Source, MediaSourceKind.File, Path.GetFileName(state.Source));
        _recordedOpenGeneration = -1;
        _pendingResume = null; _resumeReady = false; NotifyPendingResume();
        OnPropertyChanged(nameof(IsCurrentFavorite));
        return true;
    }

    private async Task OpenMediaAsync()
    {
        RequestMainLayout();
        IReadOnlyList<string> sources = await _mediaSourcePicker.PickMediaFilesAsync();
        if (sources.Count == 1) OpenSource(sources[0], MediaSourceKind.File);
        else if (sources.Count > 1) await OpenMultipleAsync(sources, MediaSourceKind.File);
    }

    private async Task OpenFolderAsync()
    {
        RequestMainLayout();
        string? source = await _mediaSourcePicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(source)) await OpenFolderSourcesAsync(source);
    }

    /// <summary>Open Network Stream: one URL per line; several open as a queue, as dropped files do.</summary>
    public void OpenUrl()
    {
        string[] urls = (UrlText ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length <= 1) { OpenSource(urls.FirstOrDefault() ?? UrlText, MediaSourceKind.NetworkUrl); return; }
        List<MediaSource> sources = [];
        for (int line = 0; line < urls.Length; line++)
        {
            MediaOpenResult result = _sourceValidator.Validate(urls[line], MediaSourceKind.NetworkUrl);
            if (!result.Succeeded || result.Source is null) { _diagnostic?.Invoke(result.Diagnostic ?? "Media source validation failed."); OpenError = $"Line {line + 1}: {result.UserMessage}"; return; }
            sources.Add(result.Source);
        }
        RequestMainLayout();
        OpenError = string.Empty;
        OpenExpandedSources(new(sources, [], false), replace: !EnqueuesOnOpen(MediaSourceKind.NetworkUrl));
    }

    private void TogglePlayback()
    {
        if (PlaybackStatus != PlaybackStatus.Stopped) { _transport.TogglePause(); return; }
        // At EOF VLC still owns the queue. Restart its selected item without replacing it.
        if (_queue is not null && QueueState.Status == QueueLoadState.Ready && QueueItems.Count > 0)
        {
            var item = QueueItems.FirstOrDefault(item => item.IsCurrent) ?? QueueItems[0];
            _stopRequested = false;
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

    /// <summary>
    /// Settings › Library › Add to the queue instead of replacing it: media
    /// opened from a file, a folder, a URL or a drop joins the queue that is
    /// playing. Library, recent and playlist items still replace it, and so
    /// does anything opened after Stop, before the engine reports it stopped.
    /// </summary>
    private bool EnqueuesOnOpen(MediaSourceKind kind) =>
        (_settings?.Current.EnqueueOnOpen ?? false) && kind is MediaSourceKind.File or MediaSourceKind.Folder or MediaSourceKind.NetworkUrl or MediaSourceKind.DragDrop &&
        _queue is not null && QueueItems.Count > 0 && !_stopRequested && PlaybackStatus is PlaybackStatus.Playing or PlaybackStatus.Paused;

    private void OpenSource(string? rawSource, MediaSourceKind kind, bool offerResume)
    {
        RequestMainLayout();
        MediaOpenResult result = _sourceValidator.Validate(rawSource, kind);
        if (!result.Succeeded || result.Source is null) { if (kind == MediaSourceKind.Recent && !string.IsNullOrWhiteSpace(rawSource)) TryHistory(() => _history?.RemoveRecent(rawSource)); _diagnostic?.Invoke(result.Diagnostic ?? "Media source validation failed."); OpenError = result.UserMessage ?? "Media could not be opened."; NotifyLibrary(); return; }
        // A dropped folder is read as Open folder reads one (Settings ›
        // Library: subfolders, hidden files), not by VLC.
        if (kind == MediaSourceKind.DragDrop && result.Source.Kind == MediaSourceKind.Folder && _queue is not null)
        {
            _ = OpenFolderSourcesAsync(result.Source.CanonicalSource);
            return;
        }
        if (offerResume && EnqueuesOnOpen(kind))
        {
            OpenExpandedSources(new([result.Source], [], false), replace: false);
            return;
        }
        CheckpointCurrent(true);
        _stoppedSource = null;
        _stopRequested = false;
        _currentSource = result.Source;
        UpdateNowPlaying();
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
        // As in VLC, a subtitle file dropped on playing media is added to it.
        if (sources.Count == 1 && IsSubtitleFile(sources[0]) && _mediaControls is not null && PlaybackStatus is PlaybackStatus.Playing or PlaybackStatus.Paused)
        {
            ReportExternalTrack(_mediaControls.AttachExternalTrack(MediaTrackKind.Subtitle, sources[0]));
            return;
        }
        if (sources.Count == 1) { OpenSource(sources[0], MediaSourceKind.DragDrop); return; }
        _ = OpenMultipleAsync(sources, MediaSourceKind.DragDrop);
    }

    private async Task OpenFolderSourcesAsync(string folder)
    {
        MediaSourceExpansion expansion = await _sourceExpander.ExpandAsync(folder, MediaSourceKind.Folder, MediaFolderScan.For(_settings?.Current ?? PlayerSettings.Default));
        OpenExpandedSources(expansion, replace: !EnqueuesOnOpen(MediaSourceKind.Folder));
    }

    /// <summary>Several files (Open File, a drop, the command line) open as one queue, in order.</summary>
    private async Task OpenMultipleAsync(IReadOnlyList<string> sources, MediaSourceKind kind)
    {
        RequestMainLayout();
        await Task.Yield();
        List<MediaSource> accepted = []; List<string> failures = []; bool bounded = false;
        MediaFolderScan scan = MediaFolderScan.For(_settings?.Current ?? PlayerSettings.Default);
        foreach (string source in sources)
        {
            MediaOpenResult result = _sourceValidator.Validate(source, kind);
            if (!result.Succeeded || result.Source is null) { failures.Add(result.UserMessage ?? source); continue; }
            // Dropped folders are read as Open folder reads one, in drop order.
            if (result.Source.Kind != MediaSourceKind.Folder || _queue is null) { accepted.Add(result.Source); continue; }
            MediaSourceExpansion folder = await _sourceExpander.ExpandAsync(result.Source.CanonicalSource, MediaSourceKind.Folder, scan);
            accepted.AddRange(folder.Sources); failures.AddRange(folder.Failures); bounded |= folder.WasBounded;
        }
        if (accepted.Count > MediaSourceExpander.DefaultMaximumItems) { accepted.RemoveRange(MediaSourceExpander.DefaultMaximumItems, accepted.Count - MediaSourceExpander.DefaultMaximumItems); bounded = true; }
        OpenExpandedSources(new(accepted, failures, bounded), replace: !EnqueuesOnOpen(kind));
    }

    private void OpenExpandedSources(MediaSourceExpansion expansion, bool replace)
    {
        if (expansion.Sources.Count == 0) { OpenError = expansion.Failures.FirstOrDefault() ?? "No usable media files were found."; return; }
        if (_queue is null) { OpenError = "The playback queue is not ready yet."; return; }
        if (!replace)
        {
            // Added after the queue's items; what plays keeps playing.
            try { _queue.Enqueue(expansion.Sources.Select(x => x.CanonicalSource)); }
            catch (Exception exception) { _diagnostic?.Invoke(exception.ToString()); OpenError = "Media could not be queued."; return; }
            OpenError = expansion.Failures.Count > 0 || expansion.WasBounded ? $"Queued {expansion.Sources.Count} item(s). {expansion.Failures.Count} item(s) could not be read{(expansion.WasBounded ? "; folder limit reached" : string.Empty)}." : string.Empty;
            return;
        }
        _stoppedSource = null;
        _stopRequested = false;
        _currentSource = expansion.Sources[0]; UpdateNowPlaying(); _generationFloor = _lastObservedGeneration; _activeGeneration = -1; _resumeReady = false; _pendingResume = null; NotifyPendingResume();
        try { _queue.OpenQueue(expansion.Sources.Select(x => x.CanonicalSource).ToArray()); }
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
            foreach (string entry in PlaylistDocument.Parse(content, path))
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

    internal async Task SavePlaylistAsync()
    {
        if (_mediaSourcePicker is not IPlaylistDocumentPicker picker) return;
        string? path = await picker.PickPlaylistToSaveAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await File.WriteAllTextAsync(path, PlaylistDocument.Write(QueueItems.Select(x => x.Source), path)); OpenError = string.Empty; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        { _diagnostic?.Invoke(exception.ToString()); OpenError = "That playlist could not be saved."; }
    }

    internal void ApplyQueueState(PlaybackQueueState state)
    {
        if (state.Generation < QueueState.Generation || (_activeGeneration >= 0 && state.Generation > 0 && state.Generation != _activeGeneration)) return;
        QueueState = state;
        UpdateNowPlaying();
        OnPropertyChanged(nameof(QueueItems)); OnPropertyChanged(nameof(VisibleQueueItems)); OnPropertyChanged(nameof(CanSearchQueue)); OnPropertyChanged(nameof(HasNoQueueMatch)); OnPropertyChanged(nameof(HasQueueNavigation)); OnPropertyChanged(nameof(HasQueueError)); OnPropertyChanged(nameof(QueueError)); OnPropertyChanged(nameof(IsQueueLoading)); OnPropertyChanged(nameof(HasQueueItems)); OnPropertyChanged(nameof(HasEmptyQueue)); OnPropertyChanged(nameof(RepeatLabel)); OnPropertyChanged(nameof(ShuffleLabel));
        OnPropertyChanged(nameof(QueueSummaryText)); OnPropertyChanged(nameof(IsRepeatOn)); OnPropertyChanged(nameof(IsRepeatItem)); OnPropertyChanged(nameof(IsShuffleOn));
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
        foreach (string property in new[] { nameof(AudioTracks), nameof(SubtitleTracks), nameof(Titles), nameof(Chapters), nameof(IsMediaCapabilitiesLoading), nameof(HasMediaCapabilitiesError), nameof(HasMediaCapabilitiesEmpty), nameof(AreMediaControlsUnavailable), nameof(MediaCapabilitiesError), nameof(AudioDelayMilliseconds), nameof(SubtitleDelayMilliseconds), nameof(CanAdjustDelays), nameof(Programs), nameof(HasPrograms) }) OnPropertyChanged(property);
        foreach (RegistryCommand? command in new[] { SelectAudioTrackCommand, SelectSubtitleTrackCommand, SelectTitleCommand, SelectChapterCommand, AdjustAudioDelayCommand, AdjustSubtitleDelayCommand, ResetAudioDelayCommand, ResetSubtitleDelayCommand, AttachExternalAudioCommand, AttachExternalSubtitleCommand }.OfType<RegistryCommand>()) command.RaiseCanExecuteChanged();
        RaiseHotkeyCommands();
    }

    private async Task AttachExternalTrackAsync(MediaTrackKind kind)
    {
        if (_mediaSourcePicker is not IExternalTrackPicker picker || _mediaControls is null) return;
        string? path = await picker.PickExternalTrackAsync(kind);
        if (string.IsNullOrWhiteSpace(path)) return;
        string extension = Path.GetExtension(path);
        bool valid = kind == MediaTrackKind.Subtitle
            ? IsSubtitleFile(path)
            : extension is ".mp3" or ".m4a" or ".aac" or ".flac" or ".wav" or ".ogg";
        if (!valid) { MediaControlsNotice = kind == MediaTrackKind.Subtitle ? "Choose a supported subtitle file." : "Choose a supported audio file."; OnPropertyChanged(nameof(HasMediaControlsNotice)); return; }
        ReportExternalTrack(_mediaControls.AttachExternalTrack(kind, path));
    }

    private static bool IsSubtitleFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".srt" or ".ass" or ".ssa" or ".vtt" or ".sub";

    private void ReportExternalTrack(ExternalTrackAttachmentState result)
    {
        MediaControlsNotice = result.AcceptedIntoItem
            ? result.PendingUntilNextOpen ? "Added for the next media open. It is not a current live track." : "Added to the playing media."
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
    public void OnClosing() { CheckpointCurrent(true); SaveVolume(); }

    /// <summary>
    /// Remember volume. VLC reports the volume of the system's audio session,
    /// which Windows keeps from one run to the next, so playback can start
    /// anywhere. Until a new engine generation reports the wanted volume and
    /// mute (the remembered or default ones at start, later the ones last
    /// set), they are sent again; after that, the engine's values are the
    /// ones to remember. Opens within a session keep the volume either way.
    /// </summary>
    private void SyncVolume(MediaEngineState state)
    {
        if (_volumeMemory is null || state.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused)) return;
        if (state.Generation != _volumeSyncedGeneration)
        {
            if (state.Generation != _volumeAttemptGeneration) { _volumeAttemptGeneration = state.Generation; _volumeAttempts = 0; }
            bool reached = Math.Abs(state.Volume * 100 - _wantedVolume.Volume) < .5 && state.IsMuted == _wantedVolume.Muted;
            if (!reached && _volumeAttempts < MaximumVolumeAttempts)
            {
                _volumeAttempts++;
                _transport.SetVolume(_wantedVolume.Volume / 100);
                _transport.SetMuted(_wantedVolume.Muted);
                return;
            }
            _volumeSyncedGeneration = state.Generation;
        }
        _wantedVolume = new(Math.Round(state.Volume * 100, 1), state.IsMuted);
    }

    // Maximum volume and Volume step apply at once; a volume above a lowered
    // maximum comes down to it.
    private void ApplyVolumeSettings()
    {
        OnPropertyChanged(nameof(MaximumVolume));
        OnPropertyChanged(nameof(VolumeStep));
        OnPropertyChanged(nameof(VolumeHelpText));
        if (Volume > MaximumVolume) Volume = MaximumVolume;
    }

    /// <summary>Writes the volume to remember, when Remember volume is on and it changed since the last write.</summary>
    public void SaveVolume()
    {
        if (_volumeMemory is null || !(_settings?.Current.RememberVolume ?? false) || _wantedVolume == _savedVolume) return;
        _volumeMemory.Save(_wantedVolume);
        _savedVolume = _wantedVolume;
    }
    private void DeclineResume()
    {
        bool ok = true; if (_pendingResume is { } resume) TryHistory(() => _history?.RemoveResume(resume.Source), () => ok = false); if (!ok) return;
        _pendingResume = null; _resumeReady = false; NotifyPendingResume(); NotifyLibrary();
    }
    // Closes the prompt without touching history: the saved position survives until playback passes the resume minimum.
    private void DismissResume() { _pendingResume = null; _resumeReady = false; NotifyPendingResume(); }

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

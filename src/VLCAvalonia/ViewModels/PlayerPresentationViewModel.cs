using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed class PlayerPresentationViewModel : INotifyPropertyChanged
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
    private bool _isAlwaysOnTop;
    private bool _isMuted;
    private double _playbackRate = 1;
    private string _mediaTitle = "VLC Avalonia";
    private bool _isFullscreen;
    private MediaSource? _currentSource;
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
        Action<string>? diagnostic = null)
    {
        _transport = transport;
        _mediaSourcePicker = mediaSourcePicker;
        _openTestMedia = openTestMedia;
        _settings = settings;
        _sourceValidator = sourceValidator ?? new MediaSourceValidator(new SystemMediaFileSystem());
        _history = history;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _diagnostic = diagnostic;
        if (_settings is not null) _settings.Changed += (_, updated) =>
        {
            TryHistory(() => { if (!updated.RememberRecentMedia) _history?.ClearRecent(); if (!updated.RememberResumeHistory) _history?.ClearResume(); });
            OnPropertyChanged(nameof(SkipBackwardLabel)); OnPropertyChanged(nameof(SkipForwardLabel)); OnPropertyChanged(nameof(RecentItems));
        };
        PlayerCommandRegistry registry = new();
        Registry = registry;
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => _ = OpenMediaAsync());
        registry.Register(new(PlayerCommandId.OpenFolder, "Open folder", "Media", "Ctrl+Shift+O"), _ => _ = OpenFolderAsync());
        registry.Register(new(PlayerCommandId.OpenUrl, "Open network URL", "Media", "Ctrl+L"), _ => OpenUrl());
        registry.Register(new(PlayerCommandId.OpenRecent, "Open recent media", "Media"), parameter => OpenSource(parameter as string, MediaSourceKind.Recent));
        registry.Register(new(PlayerCommandId.AcceptResume, "Resume playback", "Media"), _ => ApplyResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.DeclineResume, "Start from beginning", "Media"), _ => DeclineResume(), _ => _pendingResume is not null && _resumeReady);
        registry.Register(new(PlayerCommandId.OpenTestMedia, "Open test video", "Media"), _ => _openTestMedia());
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => _transport.TogglePause());
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ => { CheckpointCurrent(true); _transport.Stop(); });
        registry.Register(new(PlayerCommandId.Seek, "Seek", "Playback"), parameter => _transport.Seek(ToDouble(parameter)));
        registry.Register(new(PlayerCommandId.SetVolume, "Set volume", "Audio"), parameter => _transport.SetVolume(ToDouble(parameter) / 100));
        registry.Register(new(PlayerCommandId.ToggleMute, "Mute", "Audio", "M"), _ => _transport.SetMuted(!IsMuted));
        registry.Register(new(PlayerCommandId.SkipBackward, "Skip backward", "Playback", "Alt+Left"), _ => _transport.Skip(TimeSpan.FromSeconds(-(_settings?.Current.SkipBackwardSeconds ?? 10))), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SkipForward, "Skip forward", "Playback", "Alt+Right"), _ => _transport.Skip(TimeSpan.FromSeconds(_settings?.Current.SkipForwardSeconds ?? 10)), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SetPlaybackRate, "Playback speed", "Playback"), parameter => _transport.SetPlaybackRate(Math.Clamp(ToDouble(parameter), 1d / 32, 31.25)));
        registry.Register(new(PlayerCommandId.ToggleFullscreen, "Fullscreen", "Window", "F11"), _ => toggleFullscreen?.Invoke(), _ => toggleFullscreen is not null);
        registry.Register(new(PlayerCommandId.SetAlwaysOnTop, "Always on top", "Window"), parameter => IsAlwaysOnTop = parameter is true);
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "Window", "Ctrl+,"), _ => openSettings?.Invoke(), _ => openSettings is not null);

        OpenMediaCommand = new RegistryCommand(registry, PlayerCommandId.OpenMedia);
        OpenFolderCommand = new RegistryCommand(registry, PlayerCommandId.OpenFolder);
        OpenUrlCommand = new RegistryCommand(registry, PlayerCommandId.OpenUrl);
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
    }

    public IPlayerCommandRegistry Registry { get; }
    public ICommand OpenMediaCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenUrlCommand { get; }
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
                ElapsedText = FormatTime(_duration * value);
            else
                Registry.Execute(PlayerCommandId.Seek, value);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value) || _isUpdatingFromEngine)
                return;
            Registry.Execute(PlayerCommandId.SetVolume, value);
        }
    }

    public bool CanSeek { get; private set; }
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }
    public bool IsAlwaysOnTop { get => _isAlwaysOnTop; set => Set(ref _isAlwaysOnTop, value); }
    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            Set(ref _isMuted, value);
        }
    }
    public double PlaybackRate { get => _playbackRate; private set => Set(ref _playbackRate, value); }
    public string MediaTitle { get => _mediaTitle; private set => Set(ref _mediaTitle, value); }
    public bool IsFullscreen { get => _isFullscreen; set => Set(ref _isFullscreen, value); }
    public string OpenError { get => _openError; private set => Set(ref _openError, value); }
    public string? HistoryError { get; private set; }
    public string UrlText { get => _urlText; set => Set(ref _urlText, value); }
    public bool HasPendingResume => _pendingResume is not null && _resumeReady;
    public string PendingResumeText => _pendingResume is null ? string.Empty : $"Resume from {FormatTime(_pendingResume.Position)}?";
    public IReadOnlyList<RecentMediaEntry> RecentItems => _history?.Recent ?? [];

    public void BeginSeek() => _isSeeking = CanSeek;

    public void CommitSeek()
    {
        if (!_isSeeking)
            return;
        _isSeeking = false;
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
            if (state.Status == PlaybackStatus.Faulted) { OpenError = "Media could not be opened."; _diagnostic?.Invoke(state.Error ?? "Media engine fault."); return; }
            _duration = state.Duration;
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
            IsPlaying = state.Status == PlaybackStatus.Playing;
            if (_currentSource is not null && state.Generation > 0 && state.Generation != _recordedOpenGeneration && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
            {
                TryHistory(() => _history?.RecordOpened(_currentSource, _settings?.Current.RememberRecentMedia ?? true));
                _recordedOpenGeneration = state.Generation;
                if (_pendingResume is not null && state.Generation == _activeGeneration && state.CanSeek && state.Duration > TimeSpan.Zero)
                { _resumeReady = true; OnPropertyChanged(nameof(HasPendingResume)); (AcceptResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged(); (DeclineResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged(); }
            }
            if (_currentSource is not null && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused && _clock() - _lastCheckpointUtc >= TimeSpan.FromSeconds(10))
                CheckpointCurrent(false);
        }
        finally { _isUpdatingFromEngine = false; }
    }

    private async Task OpenMediaAsync()
    {
        string? source = await _mediaSourcePicker.PickMediaAsync();
        if (!string.IsNullOrWhiteSpace(source)) OpenSource(source, MediaSourceKind.File);
    }

    private async Task OpenFolderAsync()
    {
        string? source = await _mediaSourcePicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(source)) OpenSource(source, MediaSourceKind.Folder);
    }

    public void OpenUrl() => OpenSource(UrlText, MediaSourceKind.NetworkUrl);

    public void OpenSource(string? rawSource, MediaSourceKind kind)
    {
        MediaOpenResult result = _sourceValidator.Validate(rawSource, kind);
        if (!result.Succeeded || result.Source is null) { _diagnostic?.Invoke(result.Diagnostic ?? "Media source validation failed."); OpenError = result.UserMessage ?? "Media could not be opened."; return; }
        CheckpointCurrent(true);
        _currentSource = result.Source;
        _generationFloor = _lastObservedGeneration; _activeGeneration = -1; _resumeReady = false; _lastState = null; _recordedOpenGeneration = -1;
        _pendingResume = (_settings?.Current.ResumePlayback ?? true) && (_settings?.Current.RememberResumeHistory ?? true) ? TryFindResume(_currentSource) : null;
        OnPropertyChanged(nameof(HasPendingResume)); OnPropertyChanged(nameof(PendingResumeText));
        OpenError = string.Empty;
        try { _transport.Open(_currentSource.CanonicalSource); }
        catch (Exception e) { _diagnostic?.Invoke(e.ToString()); _currentSource = null; _pendingResume = null; OpenError = "Media could not be opened."; }
        OnPropertyChanged(nameof(RecentItems));
    }

    private void ApplyResume()
    {
        if (_pendingResume is not { } resume) return;
        double duration = Math.Max(_duration.TotalSeconds, 1);
        bool ok = true; TryHistory(() => _history?.RemoveResume(resume.Source), () => ok = false); if (!ok) return;
        _transport.Seek(Math.Clamp(resume.Position.TotalSeconds / duration, 0, 1));
        _pendingResume = null; _resumeReady = false; OnPropertyChanged(nameof(HasPendingResume)); (AcceptResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged(); (DeclineResumeCommand as RegistryCommand)?.RaiseCanExecuteChanged();
    }

    public void CheckpointCurrent(bool force)
    {
        if (_currentSource is null || _lastState is not { } state) return;
        DateTimeOffset now = _clock();
        if (_activeGeneration < 0 || state.Generation != _activeGeneration || state.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused) || !state.CanSeek || state.Duration <= TimeSpan.Zero || !(_settings?.Current.RememberResumeHistory ?? true) || (!force && now - _lastCheckpointUtc < TimeSpan.FromSeconds(10))) return;
        if (TryHistory(() => _history?.Checkpoint(_currentSource, state.Elapsed, state.Duration, true, _settings?.Current.ResumeMinimumElapsedSeconds ?? 30, _settings?.Current.ResumeMaximumRemainingSeconds ?? 30, true))) _lastCheckpointUtc = now;
    }
    public void OnClosing() => CheckpointCurrent(true);
    private void DeclineResume()
    {
        bool ok = true; if (_pendingResume is { } resume) TryHistory(() => _history?.RemoveResume(resume.Source), () => ok = false); if (!ok) return;
        _pendingResume = null; _resumeReady = false; OnPropertyChanged(nameof(HasPendingResume));
    }

    private ResumeEntry? TryFindResume(MediaSource source)
    {
        ResumeEntry? result = null; TryHistory(() => result = _history?.FindEligibleResume(source, _settings?.Current.ResumeMinimumElapsedSeconds ?? 30, _settings?.Current.ResumeMaximumRemainingSeconds ?? 30)); return result;
    }
    private bool TryHistory(Action action, Action? failed = null)
    {
        try { action(); HistoryError = null; OnPropertyChanged(nameof(HistoryError)); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { HistoryError = "Media history could not be saved."; OnPropertyChanged(nameof(HistoryError)); _diagnostic?.Invoke(e.ToString()); failed?.Invoke(); return false; }
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

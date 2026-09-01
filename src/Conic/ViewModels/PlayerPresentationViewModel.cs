using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using Conic.Commands;

namespace Conic.ViewModels;

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
    private string _mediaTitle = "Conic";
    private bool _isFullscreen;
    private MediaSource? _currentSource;
    private ResumeEntry? _pendingResume;
    private string _openError = string.Empty;
    private string _urlText = string.Empty;
    private long _recordedOpenGeneration = -1;

    public PlayerPresentationViewModel(
        IPlayerTransport transport,
        IMediaSourcePicker mediaSourcePicker,
        Action openTestMedia,
        Action? toggleFullscreen = null,
        Action? openSettings = null,
        PlayerSettingsService? settings = null,
        MediaSourceValidator? sourceValidator = null,
        MediaHistoryService? history = null)
    {
        _transport = transport;
        _mediaSourcePicker = mediaSourcePicker;
        _openTestMedia = openTestMedia;
        _settings = settings;
        _sourceValidator = sourceValidator ?? new MediaSourceValidator(new SystemMediaFileSystem());
        _history = history;
        if (_settings is not null) _settings.Changed += (_, updated) =>
        {
            if (!updated.RememberRecentMedia) _history?.ClearRecent();
            if (!updated.RememberResumeHistory) _history?.ClearResume();
            OnPropertyChanged(nameof(SkipBackwardLabel)); OnPropertyChanged(nameof(SkipForwardLabel)); OnPropertyChanged(nameof(RecentItems));
        };
        PlayerCommandRegistry registry = new();
        Registry = registry;
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => _ = OpenMediaAsync());
        registry.Register(new(PlayerCommandId.OpenFolder, "Open folder", "Media", "Ctrl+Shift+O"), _ => _ = OpenFolderAsync());
        registry.Register(new(PlayerCommandId.OpenUrl, "Open network URL", "Media", "Ctrl+L"), _ => OpenUrl());
        registry.Register(new(PlayerCommandId.OpenRecent, "Open recent media", "Media"), parameter => OpenSource(parameter as string, MediaSourceKind.Recent));
        registry.Register(new(PlayerCommandId.AcceptResume, "Resume playback", "Media"), _ => ApplyResume(), _ => _pendingResume is not null);
        registry.Register(new(PlayerCommandId.DeclineResume, "Start from beginning", "Media"), _ => DeclineResume(), _ => _pendingResume is not null);
        registry.Register(new(PlayerCommandId.OpenTestMedia, "Open test video", "Media"), _ => _openTestMedia());
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => _transport.TogglePause());
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ => _transport.Stop());
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
    public string UrlText { get => _urlText; set => Set(ref _urlText, value); }
    public bool HasPendingResume => _pendingResume is not null;
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
        _isUpdatingFromEngine = true;
        try
        {
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
            MediaTitle = string.IsNullOrWhiteSpace(state.Title) ? "Conic" : state.Title;
            if (!_isSeeking)
                ElapsedText = FormatTime(state.Elapsed);
            DurationText = FormatTime(state.Duration);
            IsPlaying = state.Status == PlaybackStatus.Playing;
            if (_currentSource is not null && state.Generation > 0 && state.Generation != _recordedOpenGeneration && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
            {
                _history?.RecordOpened(_currentSource, _settings?.Current.RememberRecentMedia ?? true);
                _recordedOpenGeneration = state.Generation;
                if (_pendingResume is not null && state.CanSeek && state.Duration > TimeSpan.Zero)
                    OnPropertyChanged(nameof(HasPendingResume));
            }
            if (_currentSource is not null && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused)
                _history?.Checkpoint(_currentSource, state.Elapsed, state.Duration, _settings?.Current.RememberResumeHistory ?? true, _settings?.Current.ResumeMinimumElapsedSeconds ?? 30, _settings?.Current.ResumeMaximumRemainingSeconds ?? 30, state.CanSeek);
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
        if (!result.Succeeded || result.Source is null) { OpenError = result.UserMessage ?? "Media could not be opened."; return; }
        _currentSource = result.Source;
        _pendingResume = (_settings?.Current.ResumePlayback ?? true) && (_settings?.Current.RememberResumeHistory ?? true) ? _history?.FindResume(_currentSource) : null;
        OnPropertyChanged(nameof(HasPendingResume)); OnPropertyChanged(nameof(PendingResumeText));
        OpenError = string.Empty;
        _transport.Open(_currentSource.CanonicalSource);
        OnPropertyChanged(nameof(RecentItems));
    }

    private void ApplyResume()
    {
        if (_pendingResume is not { } resume) return;
        _transport.Seek(Math.Clamp(resume.Position.TotalSeconds / Math.Max(resume.Duration.TotalSeconds, 1), 0, 1));
        _history?.RemoveResume(resume.Source); _pendingResume = null; OnPropertyChanged(nameof(HasPendingResume));
    }
    private void DeclineResume()
    {
        if (_pendingResume is { } resume) _history?.RemoveResume(resume.Source);
        _pendingResume = null; OnPropertyChanged(nameof(HasPendingResume));
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

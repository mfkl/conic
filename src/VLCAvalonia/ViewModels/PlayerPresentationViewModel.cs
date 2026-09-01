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

    public PlayerPresentationViewModel(
        IPlayerTransport transport,
        IMediaSourcePicker mediaSourcePicker,
        Action openTestMedia,
        Action? toggleFullscreen = null)
    {
        _transport = transport;
        _mediaSourcePicker = mediaSourcePicker;
        _openTestMedia = openTestMedia;
        PlayerCommandRegistry registry = new();
        Registry = registry;
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => _ = OpenMediaAsync());
        registry.Register(new(PlayerCommandId.OpenTestMedia, "Open test video", "Media"), _ => _openTestMedia());
        registry.Register(new(PlayerCommandId.TogglePause, "Play or pause", "Playback", "Space"), _ => _transport.TogglePause());
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ => _transport.Stop());
        registry.Register(new(PlayerCommandId.Seek, "Seek", "Playback"), parameter => _transport.Seek(ToDouble(parameter)));
        registry.Register(new(PlayerCommandId.SetVolume, "Set volume", "Audio"), parameter => _transport.SetVolume(ToDouble(parameter) / 100));
        registry.Register(new(PlayerCommandId.ToggleMute, "Mute", "Audio", "M"), _ => _transport.SetMuted(!IsMuted));
        registry.Register(new(PlayerCommandId.SkipBackward, "Skip backward", "Playback", "Alt+Left"), _ => _transport.Skip(TimeSpan.FromSeconds(-10)), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SkipForward, "Skip forward", "Playback", "Alt+Right"), _ => _transport.Skip(TimeSpan.FromSeconds(10)), _ => CanSeek);
        registry.Register(new(PlayerCommandId.SetPlaybackRate, "Playback speed", "Playback"), parameter => _transport.SetPlaybackRate(Math.Clamp(ToDouble(parameter), 1d / 32, 31.25)));
        registry.Register(new(PlayerCommandId.ToggleFullscreen, "Fullscreen", "Window", "F11"), _ => toggleFullscreen?.Invoke(), _ => toggleFullscreen is not null);
        registry.Register(new(PlayerCommandId.SetAlwaysOnTop, "Always on top", "Window"), parameter => IsAlwaysOnTop = parameter is true);

        OpenMediaCommand = new RegistryCommand(registry, PlayerCommandId.OpenMedia);
        OpenTestMediaCommand = new RegistryCommand(registry, PlayerCommandId.OpenTestMedia);
        TogglePauseCommand = new RegistryCommand(registry, PlayerCommandId.TogglePause);
        StopCommand = new RegistryCommand(registry, PlayerCommandId.Stop);
        ToggleMuteCommand = new RegistryCommand(registry, PlayerCommandId.ToggleMute);
        SkipBackwardCommand = new RegistryCommand(registry, PlayerCommandId.SkipBackward);
        SkipForwardCommand = new RegistryCommand(registry, PlayerCommandId.SkipForward);
        SetPlaybackRateCommand = new RegistryCommand(registry, PlayerCommandId.SetPlaybackRate);
        ToggleFullscreenCommand = new RegistryCommand(registry, PlayerCommandId.ToggleFullscreen);
        SetAlwaysOnTopCommand = new RegistryCommand(registry, PlayerCommandId.SetAlwaysOnTop);
    }

    public IPlayerCommandRegistry Registry { get; }
    public ICommand OpenMediaCommand { get; }
    public ICommand OpenTestMediaCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand SkipBackwardCommand { get; }
    public ICommand SkipForwardCommand { get; }
    public ICommand SetPlaybackRateCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand SetAlwaysOnTopCommand { get; }

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
            if (Set(ref _isMuted, value)) OnPropertyChanged(nameof(MuteGlyph));
        }
    }
    public string MuteGlyph => IsMuted ? "🔇" : "🔊";
    public double PlaybackRate { get => _playbackRate; private set => Set(ref _playbackRate, value); }
    public string MediaTitle { get => _mediaTitle; private set => Set(ref _mediaTitle, value); }
    public bool IsFullscreen { get => _isFullscreen; set => Set(ref _isFullscreen, value); }

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
            MediaTitle = string.IsNullOrWhiteSpace(state.Title) ? "VLC Avalonia" : state.Title;
            if (!_isSeeking)
                ElapsedText = FormatTime(state.Elapsed);
            DurationText = FormatTime(state.Duration);
            IsPlaying = state.Status == PlaybackStatus.Playing;
        }
        finally { _isUpdatingFromEngine = false; }
    }

    private async Task OpenMediaAsync()
    {
        string? source = await _mediaSourcePicker.PickMediaAsync();
        if (!string.IsNullOrWhiteSpace(source))
            _transport.Open(source);
    }

    private static double ToDouble(object? parameter) => parameter switch
    {
        double value => value,
        float value => value,
        int value => value,
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

using System.ComponentModel;
using Avalonia.Controls;
using Player.Application;
using VLCAvalonia.Interop.Windows;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia;

// Settings the app applies itself rather than through VLC's options:
// Playback › When playback finishes, Video › Start videos in fullscreen and
// Stop the screensaver during playback. The rest go to each new core
// (EngineArguments, PlayerEngineOptions); the audio effects, replay gain and
// the device also go to the playing one (LiveAudio, AudioEngineOptions).
public partial class MainWindow
{
    private long _fullscreenStartGeneration = -1;

    private void InitializePlaybackSettings()
    {
        _presentation.PlaybackFinished += (_, _) => OnPlaybackFinished();
        _presentation.VideoAdjustments.PropertyChanged += OnVideoOutputChanged;
        _presentation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlayerPresentationViewModel.IsPlaying)) UpdateDisplayRequest();
        };
        Closed += (_, _) => { if (OperatingSystem.IsWindows()) DisplayRequest.Set(false); };
    }

    /// <summary>The libVLC options the next open starts with, the proxy password unprotected.</summary>
    internal static IReadOnlyList<string> EngineArguments(PlayerSettings settings) => PlayerEngineOptions.Arguments(settings,
        OperatingSystem.IsWindows() ? ProxyPasswordProtection.Unprotect(settings.HttpProxyPasswordProtected) : null);

    private void ApplyEngineSettings(PlayerSettings settings)
    {
        VideoSurface.OpenArguments = EngineArguments(settings);
        VideoSurface.LiveAudio = AudioEngineOptions.Live(settings);
        VideoSurface.QueueModes = (QueueModeSettings.Repeat(settings.QueueRepeat), QueueModeSettings.Order(settings.QueueShuffle));
        UpdateDisplayRequest();
    }

    private void OnPlaybackFinished()
    {
        switch (_settings.Current.PlaybackFinished)
        {
            case "Home":
                if (WindowState == WindowState.FullScreen) ToggleFullscreen();
                ExitPictureInPicture();
                _presentation.IsHomeOpen = true;
                break;
            case "Quit":
                Close();
                break;
        }
    }

    // Start videos in fullscreen: once per open, when its video output appears.
    private void OnVideoOutputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoAdjustmentsViewModel.Available)) return;
        UpdateDisplayRequest();
        if (!_presentation.VideoAdjustments.Available || VideoSurface.Player is not { } player) return;
        long generation = player.State.Generation;
        if (generation == _fullscreenStartGeneration) return;
        _fullscreenStartGeneration = generation;
        if (_settings.Current.StartFullscreen && WindowState != WindowState.FullScreen && !_presentation.IsPictureInPicture)
            ToggleFullscreen();
    }

    // Keeps the display on while video plays (Windows; VLC's own handling is in its video outputs).
    private void UpdateDisplayRequest()
    {
        if (!OperatingSystem.IsWindows()) return;
        DisplayRequest.Set(_settings.Current.StopScreensaver && _presentation.IsPlaying && _presentation.VideoAdjustments.Available);
    }
}

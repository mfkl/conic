using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Player.Contracts;
using Conic.Controls;

namespace Conic;

// Settings › Interface the window applies itself (phase 9d): Pause video
// when minimized and Resize the window to fit the video. Remember window
// size and position is MainWindow.WindowState.cs, Keep window above other
// windows and the file name in the title bar the view model's, Show
// controls in fullscreen and Auto-hide the controls SetChromeVisibility's.
public partial class MainWindow
{
    private bool _pausedByMinimize;
    private long _resizeGeneration = -1;

    private void InitializeInterfaceSettings() => VideoSurface.PresentationChanged += TryResizeToVideo;

    // As VLC's own interface: video that plays pauses when the window is
    // minimized and plays again when it comes back; audio keeps playing.
    private void ApplyMinimizedPause()
    {
        if (WindowState == WindowState.Minimized)
        {
            if (!_settings.Current.PauseWhenMinimized || !_presentation.IsPlaying || !_presentation.VideoAdjustments.Available || _presentation.IsPictureInPicture) return;
            _presentation.TogglePauseCommand.Execute(null);
            _pausedByMinimize = true;
            return;
        }
        if (!_pausedByMinimize) return;
        _pausedByMinimize = false;
        if (_presentation.PlaybackStatus == PlaybackStatus.Paused) _presentation.TogglePauseCommand.Execute(null);
    }

    /// <summary>Resize the window to fit the video, once per open: when the
    /// video's size is known, a normal (not maximized or fullscreen) window
    /// takes it, one video pixel to one screen pixel, within 90 % of the
    /// screen.</summary>
    private void RequestResizeToVideo(long generation)
    {
        _resizeGeneration = _settings.Current.ResizeToVideo ? generation : -1;
        TryResizeToVideo();
    }

    private void TryResizeToVideo()
    {
        if (_resizeGeneration < 0) return;
        if (VideoSurface.Player?.State.Generation != _resizeGeneration || WindowState != WindowState.Normal || _presentation.IsPictureInPicture || _presentation.IsHomeOpen)
        {
            _resizeGeneration = -1;
            return;
        }
        Size video = VideoSurface.NaturalVideoSize;
        // The size is known from the first frame.
        if (video.Width <= 0 || video.Height <= 0) return;
        _resizeGeneration = -1;
        double scale = RenderScaling > 0 ? RenderScaling : 1;
        // In a window the title bar and the control bar dock above and below the video.
        double chrome = PlatformChrome.TitleBarHeight + PlayerBarHeight;
        double width = video.Width / scale, height = video.Height / scale;
        Screen? screen = Screens.ScreenFromWindow(this);
        if (screen is not null)
        {
            double fit = Math.Min(1, Math.Min(screen.WorkingArea.Width / scale * .9 / width, (screen.WorkingArea.Height / scale * .9 - chrome) / height));
            if (fit > 0) { width *= fit; height *= fit; }
        }
        Width = Math.Max(MinWidth, Math.Round(width));
        Height = Math.Max(MinHeight, Math.Round(height + chrome));
        if (screen is null) return;
        // Keep the window on its screen.
        PixelRect area = screen.WorkingArea;
        PixelSize size = PixelSize.FromSize(new Size(Width, Height), scale);
        int x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - size.Width));
        int y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        if (x != Position.X || y != Position.Y) Position = new PixelPoint(x, y);
    }
}

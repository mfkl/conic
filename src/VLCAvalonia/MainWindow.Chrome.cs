using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Player.Application;
using VLCAvalonia.Controls;

namespace VLCAvalonia;

// Pointer, fullscreen and seek/volume interaction with the player chrome,
// plus the docked video layout and the responsive control bar.
public partial class MainWindow
{
    /// <summary>Height of the design's control bar (VideoArea's last row).</summary>
    private const double PlayerBarHeight = 54;

    private void OnPlayerInteraction(object? sender, PointerEventArgs e)
    {
        _keyboardNavigation = false;
        WakeChrome();
    }

    private void OnPlayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _keyboardNavigation = false;
        WakeChrome();
        Point point = e.GetPosition(this);
        if (OpenMenu.IsVisible && !GetClientRect(OpenMenu).Contains(point) && !GetClientRect(OpenOptionsButton).Contains(point))
            CloseOpenMenu(restoreFocus: false);
    }

    private void OnVideoPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        WakeChrome();
        if (e.ClickCount == 2 && (ReferenceEquals(e.Source, VideoSurface) || ReferenceEquals(e.Source, VideoArea)))
        {
            if (_presentation.IsPictureInPicture) ExitPictureInPicture(); else ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void WakeChrome()
    {
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (_chromeVisibility.HidesAutomatically) _autoHideTimer.Start();
    }

    // Also runs when the platform changes the state (macOS green button).
    private void OnWindowStateChanged()
    {
        bool fullscreen = WindowState == WindowState.FullScreen;
        if (_chromeVisibility.IsFullscreen != fullscreen) _chromeVisibility.SetFullscreen(fullscreen);
        _presentation.IsFullscreen = fullscreen;
        ApplyVideoLayout();
    }

    /// <summary>
    /// Windowed, the video sits between the docked title bar and control bar
    /// (row 1). In fullscreen and picture in picture it spans every row and
    /// the bars overlay it. Only the surface's row changes: re-parenting it
    /// would dispose the VLC backend, which re-reads its bounds every frame.
    /// </summary>
    private void ApplyVideoLayout()
    {
        bool docked = !_chromeVisibility.OverlaysVideo;
        foreach (Control layer in new Control[] { VideoSurface, SubtitleLayer })
        {
            Grid.SetRow(layer, docked ? 1 : 0);
            Grid.SetRowSpan(layer, docked ? 1 : 3);
        }
        SetChromeVisibility(_chromeVisibility.IsVisible);
    }

    // With the client area extended over the decorations, the top chrome is
    // the caption: drag it to move the window, double-click to maximize.
    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Border or TextBlock && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && WindowState != WindowState.FullScreen)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
                return;
            }
            if (WindowState != WindowState.FullScreen)
            {
                BeginMoveDrag(e);
                e.Handled = true;
            }
        }
    }

    private void ToggleFullscreen()
    {
        ExitPictureInPicture();
        if (WindowState != WindowState.FullScreen) _preFullscreenState = WindowState;
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _presentation.IsFullscreen = WindowState == WindowState.FullScreen;
        _chromeVisibility.SetFullscreen(_presentation.IsFullscreen);
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (_chromeVisibility.HidesAutomatically) _autoHideTimer.Start();
    }

    private Rect GetClientRect(Control control)
    {
        Point origin = control.TranslatePoint(default, this) ?? new Point(double.NaN, double.NaN);
        return new Rect(origin, control.Bounds.Size);
    }

    private void OnSeekStarted(object? sender, EventArgs e) =>
        BeginSeek();

    private void OnSeekCommitted(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(EndSeek);

    private void BeginSeek()
    {
        _isSeeking = true;
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        _presentation.BeginSeek();
    }

    private void OnVolumeDragStarted(object? sender, EventArgs e)
    {
        _isVolumeDragging = true;
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
    }

    private void OnVolumeDragCompleted(object? sender, EventArgs e)
    {
        _isVolumeDragging = false;
        if (_presentation.IsPlaying) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void EndSeek()
    {
        _isSeeking = false;
        _presentation.CommitSeek();
        if (_presentation.IsPlaying) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void ApplyResponsiveChrome()
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(Bounds.Width);
        ProductMark.IsVisible = !layout.IsCompact;
        ElapsedLabel.IsVisible = layout.ShowTimeLabels;
        DurationLabel.IsVisible = layout.ShowTimeLabels;
        PlayerVolume.IsVisible = layout.ShowVolumeSlider;
        // Below the narrow width the More menu and the context menu carry these.
        AudioTracksButton.IsVisible = layout.ShowTrackButtons;
        SubtitleTracksButton.IsVisible = layout.ShowTrackButtons;
        PlaybackSettingsButton.IsVisible = layout.ShowTrackButtons;
        bool denseBar = Classes.Contains("compact-density");
        DockGrid.ColumnSpacing = denseBar ? Math.Min(layout.Spacing, 12) : layout.Spacing;
        PlayerDock.Padding = new Thickness(denseBar ? Math.Min(layout.Padding, 12) : layout.Padding, 11);

        // Panels open between the title bar and the control bar.
        OpenMenu.MaxHeight = Math.Max(160, Bounds.Height - OpenMenu.Margin.Top - PlayerBarHeight - 8);
        ApplyDrawerLayout();
    }

    /// <summary>
    /// Bar panels (design: anchored to the right edge between the title bar
    /// and the control bar, 400 wide). The queue and playback panels fill
    /// that height; the tracks panel fits its content. Where the bar is
    /// too narrow for the track buttons the panels become a sheet above the
    /// bar, inset 16 from the sides. Fullscreen uses the same room: the bars
    /// overlay the video there and stay up while a panel is open.
    /// </summary>
    private void ApplyDrawerLayout()
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(Bounds.Width);
        bool sheet = !layout.ShowTrackButtons;
        double gap = sheet ? 8 : 20;
        double top = PlatformChrome.TitleBarHeight + gap, bottom = PlayerBarHeight + gap;
        double room = Math.Max(160, Bounds.Height - top - bottom);
        foreach (Border panel in new[] { QueueDrawer })
        {
            bool fitsContent = _presentation.IsTracksPanel;
            panel.Classes.Set("docked", !sheet);
            panel.Width = sheet ? double.NaN : 400;
            panel.MaxWidth = sheet ? double.PositiveInfinity : Math.Max(320, Bounds.Width - 32);
            panel.HorizontalAlignment = sheet ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
            panel.Margin = sheet ? new Thickness(16, top, 16, bottom) : new Thickness(0, top, 0, bottom);
            panel.MaxHeight = room;
            panel.Height = sheet && !fitsContent ? Math.Min(Math.Min(Math.Max(260, Bounds.Height * .72), 520), room) : double.NaN;
            panel.VerticalAlignment = sheet || fitsContent ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Stretch;
        }
    }

    private void ApplyResponsiveSettings() => SettingsPanel.ApplyResponsiveLayout();
}

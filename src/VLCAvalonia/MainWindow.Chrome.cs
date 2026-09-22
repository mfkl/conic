using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Player.Application;

namespace VLCAvalonia;

// Pointer, fullscreen and seek/volume interaction with the player chrome,
// plus the responsive dock layout.
public partial class MainWindow
{
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
        if (_presentation.IsPlaying && !_settings.Current.ReduceMotion) _autoHideTimer.Start();
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
        if (_presentation.IsPlaying) _autoHideTimer.Start();
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
        OpenMenu.MaxHeight = Math.Max(160, Bounds.Height - 90);
        bool compactTopChrome = Bounds.Width < PlayerLayoutPolicy.CompactWidth;
        TopChrome.Margin = new Thickness(0);
        TopChromeTitle.IsVisible = true;
        ProductMark.IsVisible = !compactTopChrome;
        LeftTransport.IsVisible = !compactTopChrome;
        PreviousButton.IsVisible = !compactTopChrome;
        NextButton.IsVisible = !compactTopChrome;
        PlayerVolume.IsVisible = true;
        if (compactTopChrome)
        {
            QueueDrawer.Width = double.NaN;
            QueueDrawer.MaxWidth = double.PositiveInfinity;
            QueueDrawer.Height = Math.Min(Math.Max(260, Bounds.Height * .72), 520);
            QueueDrawer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            QueueDrawer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            QueueDrawer.Margin = new Thickness(12);
        }
        else
        {
            QueueDrawer.Width = 400;
            QueueDrawer.MaxWidth = Math.Max(320, Bounds.Width * .42);
            QueueDrawer.Height = double.NaN;
            QueueDrawer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            QueueDrawer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            QueueDrawer.Margin = new Thickness(0, 76, 16, 18);
        }
        CenterTransport.Spacing = layout.Spacing;
        RightTransport.Spacing = layout.Spacing;
        Grid.SetRow(RightTransport, layout.RightRow);
        Grid.SetColumn(RightTransport, layout.RightColumn);
        Grid.SetColumnSpan(RightTransport, layout.RightColumnSpan);
        Grid.SetColumn(CenterTransport, compactTopChrome ? 0 : 1);
        Grid.SetColumnSpan(CenterTransport, compactTopChrome ? 3 : 1);
        RightTransport.HorizontalAlignment = layout.IsCompact
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Right;
        // Row reassignment can happen during the window's arrange pass. Queue
        // a fresh measure so the dock grows before the compact volume row paints.
        Dispatcher.UIThread.Post(() =>
        {
            DockGrid.InvalidateMeasure();
            PlayerDock.InvalidateMeasure();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyResponsiveSettings() => SettingsPanel.ApplyResponsiveLayout();
}

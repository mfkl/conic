using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Application;

namespace Conic;

// Settings › Controls › Mouse (phase 9d): a click, a double click and the
// wheel over the video; and Settings › Subtitles › Show feedback for
// shortcuts: what a shortcut or the wheel did, shown over the video for a
// moment.
public partial class MainWindow
{
    private readonly DispatcherTimer _feedbackTimer = new();
    private readonly DispatcherTimer _clickTimer = new();
    // The press that only closed a menu or a panel does nothing else.
    private bool _pressClosedOverlay;

    private void InitializeControlSettings()
    {
        _feedbackTimer.Tick += (_, _) => { _feedbackTimer.Stop(); ShortcutFeedback.IsVisible = false; };
        _clickTimer.Tick += (_, _) => { _clickTimer.Stop(); RunMouseAction(_settings.Current.ClickAction); };
        Closed += (_, _) => { _feedbackTimer.Stop(); _clickTimer.Stop(); };
    }

    private bool IsVideoSource(object? source) =>
        ReferenceEquals(source, VideoSurface) || ReferenceEquals(source, VideoArea) || ReferenceEquals(source, SubtitleLayer);

    private bool HasOverlayOpen =>
        OpenMenu.IsVisible || QueueDrawer.IsVisible || LibraryPanel.IsVisible || SettingsPanel.IsVisible || UrlPanel.IsVisible || ResumePanel.IsVisible;

    /// <summary>Shows what a shortcut or the wheel did, while the setting is on.</summary>
    private void ShowShortcutFeedback(string? text)
    {
        if (string.IsNullOrEmpty(text) || !_settings.Current.ShortcutFeedback || _presentation.IsHomeOpen) return;
        ShortcutFeedbackText.Text = text;
        ShortcutFeedback.IsVisible = true;
        _feedbackTimer.Stop();
        _feedbackTimer.Interval = TimeSpan.FromSeconds(_settings.Current.ShortcutFeedbackSeconds);
        _feedbackTimer.Start();
    }

    // A single click waits out the double-click time, so a double click
    // does not run the single click's action as well.
    private void OnVideoClick(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount >= 2)
        {
            _clickTimer.Stop();
            if (_presentation.IsPictureInPicture) ExitPictureInPicture();
            else RunMouseAction(_settings.Current.DoubleClickAction);
            e.Handled = true;
            return;
        }
        if (_pressClosedOverlay || _settings.Current.ClickAction == "None" || _presentation.IsPictureInPicture) return;
        if (_settings.Current.DoubleClickAction == "None") { RunMouseAction(_settings.Current.ClickAction); return; }
        _clickTimer.Stop();
        _clickTimer.Interval = this.GetPlatformSettings()?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500);
        _clickTimer.Start();
    }

    private void RunMouseAction(string action)
    {
        switch (action)
        {
            case "PlayPause" when _presentation.TogglePauseCommand.CanExecute(null):
                ShowShortcutFeedback(_presentation.ShortcutFeedbackText(PlayerCommandId.TogglePause));
                _presentation.TogglePauseCommand.Execute(null);
                break;
            case "Fullscreen":
                ToggleFullscreen();
                break;
        }
    }

    private void OnVideoPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsVideoSource(e.Source) || _presentation.IsHomeOpen || HasOverlayOpen) return;
        // Avalonia reports a move to the right as a negative X (as for
        // scrolling content); right seeks forward.
        WheelStep step = InterfaceOptions.Wheel(_settings.Current, -e.Delta.X, e.Delta.Y);
        if (step.Target == WheelTarget.None) return;
        ShowShortcutFeedback(_presentation.ApplyWheel(step));
        WakeChrome();
        e.Handled = true;
    }
}

using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Player.Application;
using Player.Contracts;
using Conic.ViewModels;

namespace Conic;

// The overlay panels: More menu, URL prompt, library drawer, resume prompt,
// settings, and the presentation state that opens and closes them.
public partial class MainWindow
{
    private void OpenSettings()
    {
        ExitPictureInPicture();
        CloseRightDrawers();
        OpenMenu.IsVisible = false;
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        SettingsPanel.IsVisible = true;
        SettingsPanel.ResetTransientState();
        UpdateOpenErrorSurface();
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => SettingsPanel.SettingsSearch.Focus());
    }

    private void OnOpenOptions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool open = !OpenMenu.IsVisible;
        if (open) CloseRightDrawers();
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        OpenMenu.IsVisible = open;
        UpdateOpenErrorSurface();
        SetChromeVisibility(true);
        if (open) Dispatcher.UIThread.Post(() => OpenMenu.Focus());
    }

    private void ShowUrlPanel()
    {
        ExitPictureInPicture();
        CloseRightDrawers();
        OpenMenu.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UrlPanel.IsVisible = true;
        UpdateOpenErrorSurface();
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => { UrlTextBox.Focus(); UrlTextBox.SelectAll(); });
    }

    private void OnUrlCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseUrlPanel();

    private void CloseUrlPanel()
    {
        UrlPanel.IsVisible = false;
        UpdateOpenErrorSurface();
        RestoreRouteFocus(OpenOptionsButton);
    }

    private void CloseOpenMenu(bool restoreFocus = true)
    {
        OpenMenu.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        if (restoreFocus) RestoreRouteFocus(OpenOptionsButton);
        WakeChrome();
    }

    private void OnUrlSubmit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _presentation.Registry.Execute(PlayerCommandId.SubmitUrl);
        if (!_presentation.HasOpenError) CloseUrlPanel();
        else Dispatcher.UIThread.Post(() => { UrlTextBox.Focus(); UrlTextBox.SelectAll(); });
    }

    private void OnUrlTextKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter) { OnUrlSubmit(sender, e); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape) { CloseUrlPanel(); e.Handled = true; }
    }

    private void OnClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearRecentButton.IsVisible = false;
        ClearRecentPrompt.IsVisible = true;
        SetChromeVisibility(true);
    }

    private void OnCancelClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearRecentPrompt.IsVisible = false;
        ClearRecentButton.IsVisible = _presentation.HasRecentItems;
    }

    private void OnConfirmClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearRecentPrompt.IsVisible = false;
        ClearRecentButton.IsVisible = _presentation.HasRecentItems;
    }

    private void OnOpenFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _presentation.Registry.Execute(PlayerCommandId.OpenMedia);
        CloseOpenMenu();
    }

    private void OnOpenRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RecentMediaEntry recent })
            _presentation.Registry.Execute(PlayerCommandId.OpenRecent, recent.Source);
        CloseOpenMenu();
    }

    private void OnOpenLibraryEntry(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? source = (sender as Button)?.DataContext switch
        {
            RecentMediaEntry recent => recent.Source,
            FavoriteMediaEntry favorite => favorite.Source,
            ResumeEntry resume => resume.Source,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(source)) _presentation.Registry.Execute(PlayerCommandId.OpenRecent, source);
        CloseLibrary();
    }

    private void OnLibraryTab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tab) _presentation.LibraryTab = tab;
    }

    private void OnCloseLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseLibrary();

    private void CloseLibrary()
    {
        _presentation.IsLibraryOpen = false;
        ClearLibraryPrompt.IsVisible = false;
        RestoreRouteFocus(LibraryButton);
        if (_presentation.IsPlaying) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void OnClearLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearLibraryButton.IsVisible = false;
        ClearLibraryPrompt.IsVisible = true;
        Dispatcher.UIThread.Post(() => CancelClearLibraryButton.Focus());
    }

    private void OnCancelClearLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearLibraryPrompt.IsVisible = false;
        ClearLibraryButton.IsVisible = true;
        ClearLibraryButton.Focus();
    }

    private void OnConfirmClearLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearLibraryPrompt.IsVisible = false;
        ClearLibraryButton.IsVisible = true;
        ClearLibraryButton.Focus();
    }

    private void UpdateOpenErrorSurface() => OpenErrorToast.IsVisible = !UrlPanel.IsVisible && !OpenMenu.IsVisible && !ResumePanel.IsVisible && !SettingsPanel.IsVisible && _presentation.HasOpenError;

    private void UpdateResumePrompt()
    {
        if (_presentation.HasPendingResume)
        {
            CloseRightDrawers();
            OpenMenu.IsVisible = false;
            UrlPanel.IsVisible = false;
            ClearRecentPrompt.IsVisible = false;
            UpdateOpenErrorSurface();
            SetChromeVisibility(true);
            _autoHideTimer.Stop();
            StartResumeCountdown();
            Dispatcher.UIThread.Post(() => ResumeAction.Focus());
        }
        else
        {
            _resumeTimer?.Stop();
            Dispatcher.UIThread.Post(() => OpenOptionsButton.Focus());
            UpdateOpenErrorSurface();
            if (_presentation.IsPlaying) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
        }
    }

    private void CloseRightDrawers()
    {
        _presentation.IsQueueOpen = false;
        _presentation.IsLibraryOpen = false;
    }

    private void UpdateHistorySettingsControls() => SettingsPanel.UpdateHistory(_presentation);

    private void OnAcceptResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(PlayerCommandId.AcceptResume);

    private void OnDeclineResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(PlayerCommandId.DeclineResume);

    private void OnDismissResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(PlayerCommandId.DismissResume);

    private const int ResumePromptSeconds = 8;
    private DispatcherTimer? _resumeTimer;
    private int _resumeSecondsLeft;

    // The prompt closes itself as if cancelled; the countdown holds while the pointer is on it.
    private void StartResumeCountdown()
    {
        if (_resumeTimer is null)
        {
            _resumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _resumeTimer.Tick += (_, _) =>
            {
                if (ResumePanel.IsPointerOver) return;
                if (--_resumeSecondsLeft > 0) { UpdateResumeCountdown(); return; }
                _resumeTimer.Stop();
                _presentation.Registry.Execute(PlayerCommandId.DismissResume);
            };
        }
        _resumeSecondsLeft = ResumePromptSeconds;
        UpdateResumeCountdown();
        _resumeTimer.Stop();
        _resumeTimer.Start();
    }

    private void UpdateResumeCountdown() => ResumeCountdown.Text = $"Closes in {_resumeSecondsLeft} s. The saved position is kept.";

    private void CloseSettings()
    {
        SettingsPanel.IsVisible = false;
        SettingsPanel.ResetTransientState();
        UpdateOpenErrorSurface();
        WakeChrome();
        RestoreRouteFocus(OpenSettingsButton);
        if (_presentation.IsPlaying) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void RestoreRouteFocus(Control playerControl)
    {
        if (_presentation.IsHomeOpen) HomeScreen.FocusPrimary();
        else if (playerControl.IsEffectivelyVisible) playerControl.Focus();
        else HomeButton.Focus();
    }

    private void OnPresentationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerPresentationViewModel.IsHomeOpen))
        {
            if (_presentation.IsHomeOpen) ExitPictureInPicture(restoreRoute: false);
            CloseRightDrawers();
            OpenMenu.IsVisible = false;
            UrlPanel.IsVisible = false;
            SettingsPanel.IsVisible = false;
            if (_presentation.IsHomeOpen && WindowState == WindowState.FullScreen)
            {
                WindowState = WindowState.Normal;
                _presentation.IsFullscreen = false;
            }
            SetChromeVisibility(true);
            PlaceCaptionButtons();
            Dispatcher.UIThread.Post(() =>
            {
                if (_presentation.IsHomeOpen) HomeScreen.FocusPrimary(); else HomeButton.Focus();
            });
        }
        if (e.PropertyName == nameof(PlayerPresentationViewModel.PlaybackRate))
            UpdatePlaybackRateSelection();
        if (e.PropertyName is nameof(PlayerPresentationViewModel.IsPlaying) or nameof(PlayerPresentationViewModel.PlaybackStatus))
        {
            _chromeVisibility.SetPlaybackState(_presentation.IsPlaying, _settings.Current.ReduceMotion);
            SetChromeVisibility(true);
            _autoHideTimer.Stop();
            if (_presentation.IsPlaying && !_settings.Current.ReduceMotion) _autoHideTimer.Start();
        }
        if (e.PropertyName is nameof(PlayerPresentationViewModel.OpenError) or nameof(PlayerPresentationViewModel.HasOpenError))
            UpdateOpenErrorSurface();
        if (e.PropertyName == nameof(PlayerPresentationViewModel.IsQueueOpen))
        {
            if (_presentation.IsQueueOpen)
            {
                ExitPictureInPicture(restoreRoute: false);
                SettingsPanel.IsVisible = false;
            }
            WakeChrome();
            // The view-model notification can precede the visibility binding and layout.
            _focusQueueOnLayout = _presentation.IsQueueOpen;
            if (!_presentation.IsQueueOpen) Dispatcher.UIThread.Post(() => RestoreRouteFocus(QueueButton));
        }
        if (e.PropertyName == nameof(PlayerPresentationViewModel.IsLibraryOpen))
        {
            if (_presentation.IsLibraryOpen)
            {
                ExitPictureInPicture(restoreRoute: false);
                OpenMenu.IsVisible = false;
                UrlPanel.IsVisible = false;
                ClearRecentPrompt.IsVisible = false;
                SettingsPanel.IsVisible = false;
                UpdateOpenErrorSurface();
            }
            WakeChrome();
            Dispatcher.UIThread.Post(() => { if (_presentation.IsLibraryOpen) LibraryRecentTab.Focus(); else RestoreRouteFocus(LibraryButton); });
        }
        if (e.PropertyName == nameof(PlayerPresentationViewModel.HasPendingResume))
            UpdateResumePrompt();
        if (e.PropertyName is nameof(PlayerPresentationViewModel.HasHistoryError) or nameof(PlayerPresentationViewModel.HistoryError) or nameof(PlayerPresentationViewModel.HasRecentItems) or nameof(PlayerPresentationViewModel.HasResumeItems) or nameof(PlayerPresentationViewModel.HasFavoriteItems) or nameof(PlayerPresentationViewModel.HasLibraryItems))
            UpdateHistorySettingsControls();
    }

    private void OnSpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackRate) return;
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem { Tag: string value } &&
            double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double rate))
            _presentation.Registry.Execute(PlayerCommandId.SetPlaybackRate, rate);
        UpdatePlaybackRateSelection();
    }

    private bool _updatingPlaybackRate;

    private void UpdatePlaybackRateSelection()
    {
        _updatingPlaybackRate = true;
        try
        {
            SpeedSelector.SelectedItem = SpeedSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                item.Tag is string value && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double rate)
                && Math.Abs(rate - _presentation.PlaybackRate) < .001);
        }
        finally { _updatingPlaybackRate = false; }
    }
}

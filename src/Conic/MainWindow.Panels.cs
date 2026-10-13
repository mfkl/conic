using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Controls;
using Conic.ViewModels;

namespace Conic;

// The overlay panels: More menu, URL prompt, library drawer, resume prompt,
// settings, and the presentation state that opens and closes them.
public partial class MainWindow
{
    private void OpenSettings() => OpenSettings(null);

    /// <summary>Opens Settings with the focus on <paramref name="focus"/> (the search by default).</summary>
    private void OpenSettings(Control? focus)
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
        Dispatcher.UIThread.Post(() => (focus ?? SettingsPanel.SettingsSearch).Focus());
    }

    private void OnOpenOptions(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool open = !OpenMenu.IsVisible;
        if (open) CloseRightDrawers();
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        if (open) PrepareOpenMenu();
        OpenMenu.IsVisible = open;
        UpdateOpenErrorSurface();
        SetChromeVisibility(true);
        if (open) Dispatcher.UIThread.Post(() => OpenMenu.Focus());
    }

    // Every opening starts on the options page, with the current shortcuts.
    private void PrepareOpenMenu()
    {
        ShowOpenMenuPage(recent: false, focus: false);
        MenuShortcuts.Apply(OpenMenu, EffectiveShortcuts());
    }

    private IReadOnlyDictionary<PlayerCommandId, string> EffectiveShortcuts() =>
        PlayerShortcutPolicy.Effective(_presentation.Registry, _settings.Current.ShortcutOverrides);

    private void OnVideoContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) =>
        MenuShortcuts.Apply(VideoContextMenu, EffectiveShortcuts());

    private void OnShowRecentPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowOpenMenuPage(recent: true, focus: true);

    private void OnShowMainMenuPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowOpenMenuPage(recent: false, focus: true);

    // Open Recent replaces the options with its page; going back focuses the
    // Open Recent option again.
    private void ShowOpenMenuPage(bool recent, bool focus)
    {
        // Show the new page and move the focus before hiding the old one, so
        // the focus never sits on a hidden option.
        (recent ? MoreMenuRecent : MoreMenuMain).IsVisible = true;
        if (focus) (recent ? RecentBackRow : OpenRecentRow).Focus(NavigationMethod.Directional);
        (recent ? MoreMenuMain : MoreMenuRecent).IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        ClearRecentButton.IsVisible = _presentation.HasRecentItems;
        OpenMenuScroll.Offset = default;
    }

    // Choosing an option closes the menu (before its command runs, so a
    // dialog or panel it opens keeps the focus). Open Recent and its back
    // row only change the page; the recent list's remove and clear buttons
    // keep it open.
    private void OnOpenMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is MenuRow row && !ReferenceEquals(row, OpenRecentRow) && !ReferenceEquals(row, RecentBackRow))
            CloseOpenMenu();
    }

    // Menu keys: Up/Down (Home/End) move between the options of the page
    // that shows, Right or Enter opens Open Recent, and Left, Backspace or
    // Escape leave it. Escape on the options page closes the menu
    // (OnPlayerKeyDown). A focused speed selector keeps its arrows.
    private void OnOpenMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        bool recent = MoreMenuRecent.IsVisible;
        if (recent && e.Key is Key.Left or Key.Back or Key.Escape)
        {
            ShowOpenMenuPage(recent: false, focus: true);
            e.Handled = true;
            return;
        }
        if (!recent && e.Key == Key.Right && e.Source is MenuRow { HasSubmenu: true } submenu)
        {
            submenu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
        Control page = recent ? MoreMenuRecent : MoreMenuMain;
        Control[] options = page.GetVisualDescendants().OfType<Control>()
            .Where(control => control is Button or ComboBox && control.TemplatedParent is null && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
            .ToArray();
        if (options.Length == 0) return;
        int current = Array.FindIndex(options, option => option.IsFocused || option.IsKeyboardFocusWithin);
        int next = e.Key switch
        {
            Key.Home => 0,
            Key.End => options.Length - 1,
            Key.Up => current < 0 ? options.Length - 1 : (current + options.Length - 1) % options.Length,
            _ => current < 0 ? 0 : (current + 1) % options.Length,
        };
        options[next].Focus(NavigationMethod.Directional);
        options[next].BringIntoView();
        e.Handled = true;
    }

    // From the More menu, the right-click menu or Settings' search: the
    // shortcut editor is the Controls page of Settings (design 2437:14832).
    private void OnShowKeyboardShortcuts(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowKeyboardShortcuts();

    private void ShowKeyboardShortcuts()
    {
        _settingsViewModel.Search = string.Empty;
        _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(page => page.Name == "Controls");
        OpenSettings(SettingsPanel.ShortcutSearchBox);
        SettingsPanel.BringShortcutsIntoView();
    }

    private void OnUrlDialogCloseRequested(object? sender, EventArgs e) => CloseUrlPanel();

    // The More menu and the dialogs it opens give focus back to the "…"
    // button they came from: Home's or the player's.
    private void RestoreMenuFocus()
    {
        if (_presentation.IsHomeOpen) HomeScreen.FocusMore();
        else RestoreRouteFocus(OpenOptionsButton);
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
        RestoreMenuFocus();
    }

    private void CloseOpenMenu(bool restoreFocus = true)
    {
        OpenMenu.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        if (restoreFocus) RestoreMenuFocus();
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
        RestoreRouteFocus(OpenOptionsButton);
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
        if (e.PropertyName == nameof(PlayerPresentationViewModel.IsPictureInPicture))
        {
            _chromeVisibility.SetPictureInPicture(_presentation.IsPictureInPicture);
            ApplyVideoLayout();
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
            if (!_presentation.IsQueueOpen)
            {
                Control target = DrawerReturnTarget();
                _drawerOpener = null;
                Dispatcher.UIThread.Post(() => RestoreRouteFocus(target));
            }
        }
        if (e.PropertyName == nameof(PlayerPresentationViewModel.DrawerPanel))
            ApplyDrawerLayout();
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

    // Bar panels: the bar button that opened the drawer gets focus back when
    // it closes; without one (shortcut, menu), the open panel's own button,
    // or the More button when the bar is too narrow to show it.
    private Control? _drawerOpener;

    private void OnBarPanelButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _drawerOpener = sender as Control;

    private void OnCloseDrawer(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseDrawer();

    private void CloseDrawer()
    {
        Control target = DrawerReturnTarget();
        _presentation.IsQueueOpen = false;
        RestoreRouteFocus(target);
    }

    private Control DrawerReturnTarget()
    {
        string panel = _presentation.DrawerPanel;
        if (_drawerOpener is { IsEffectivelyVisible: true } opener && PanelOfButton(opener) == panel) return opener;
        Control button = panel switch
        {
            PlayerPresentationViewModel.TracksPanel => _presentation.IsSubtitleTab ? SubtitleTracksButton : AudioTracksButton,
            PlayerPresentationViewModel.PlaybackPanel => PlaybackSettingsButton,
            _ => QueueButton,
        };
        return button.IsEffectivelyVisible ? button : OpenOptionsButton;
    }

    private string? PanelOfButton(Control button) =>
        ReferenceEquals(button, QueueButton) ? PlayerPresentationViewModel.QueuePanel :
        ReferenceEquals(button, AudioTracksButton) || ReferenceEquals(button, SubtitleTracksButton) ? PlayerPresentationViewModel.TracksPanel :
        ReferenceEquals(button, PlaybackSettingsButton) ? PlayerPresentationViewModel.PlaybackPanel : null;

    // "Show Preferences" opens the Settings page of the tab that is showing.
    private void OnShowPreferences(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string page = _presentation.IsSubtitleTab ? "Subtitles" : "Audio";
        OpenSettings();
        _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(candidate => candidate.Name == page);
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

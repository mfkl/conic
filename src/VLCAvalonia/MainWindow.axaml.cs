using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Player.Contracts;
using Player.Application;
using VLCAvalonia.Interop;
using VLCAvalonia.Services;
using VLCAvalonia.ViewModels;
using VLCLR;

namespace VLCAvalonia;

public partial class MainWindow : Window
{
    private readonly PlayerPresentationViewModel _presentation;
    private bool _focusQueueOnLayout;
    private readonly PlayerSettingsService _settings;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly ChromeVisibilityController _chromeVisibility = new();
    private bool _isSeeking;
    private bool _isVolumeDragging;
    private bool _keyboardNavigation;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPlayerPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
                _keyboardNavigation = true;
            WakeChrome();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        _settings = new PlayerSettingsService(new FilePlayerSettingsStorage());
        _settings.Load();
        _presentation = new PlayerPresentationViewModel(
            new CompositionVideoTransport(VideoSurface),
            new StorageMediaSourcePicker(StorageProvider),
            OpenTestMedia,
            ToggleFullscreen,
            OpenSettings,
            _settings,
            new MediaSourceValidator(new SystemMediaFileSystem()),
            CreateHistory(),
            showUrlPanel: ShowUrlPanel,
            clipboard: new AvaloniaClipboardService(() => Clipboard));
        _settingsViewModel = new SettingsViewModel(_settings, _presentation.Registry);
        SettingsPanel.Initialize(_settingsViewModel, _presentation);
        SettingsPanel.CloseRequested += (_, _) => CloseSettings();
        DataContext = _presentation;
        HomeScreen.MoreRequested += (_, _) => OnOpenOptions(null, new Avalonia.Interactivity.RoutedEventArgs());
        _presentation.PropertyChanged += OnPresentationPropertyChanged;
        InitializePictureInPicture();
        UpdatePlaybackRateSelection();
        UpdateHistorySettingsControls();
        _settings.Changed += (_, settings) => ApplyLiveSettings(settings);
        VideoSurface.AudioStartupArguments = ToAudioArguments(_settings.Current);
        ApplyLiveSettings(_settings.Current);
        SetChromeVisibility(false);
        _autoHideTimer.Tick += (_, _) =>
        {
            bool menuOpen = (VideoArea.ContextMenu?.IsOpen ?? false) || SettingsPanel.IsVisible || OpenMenu.IsVisible || UrlPanel.IsVisible || ResumePanel.IsVisible || QueueDrawer.IsVisible || LibraryPanel.IsVisible;
            bool chromeFocused = _keyboardNavigation && (PlayerDock.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin || PictureInPictureChrome.IsKeyboardFocusWithin);
            bool chromePointerOver = TopChrome.IsPointerOver || PlayerDock.IsPointerOver || PictureInPictureHeader.IsPointerOver || PictureInPicturePlay.IsPointerOver || PictureInPictureTimeline.IsPointerOver;
            if (_chromeVisibility.TryHide(_isSeeking || _isVolumeDragging, menuOpen, chromePointerOver, chromeFocused))
            {
                SetChromeVisibility(false);
                _autoHideTimer.Stop();
            }
        };
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Closed += (_, _) => { _presentation.OnClosing(); _autoHideTimer.Stop(); };
        SizeChanged += (_, _) => { ApplyResponsiveChrome(); ApplyResponsiveSettings(); };
        Activated += (_, _) => WakeChrome();
    }

    internal static IReadOnlyList<string> ToAudioArguments(PlayerSettings settings) => new VLCAudioStartupOptions(
        settings.AudioOutputModule == "Default" ? null : settings.AudioOutputModule,
        settings.NormalizationMaximumLevel is double level ? (float)level : null,
        settings.AudioPassthrough).ToArguments();

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

    private void OnPlayerKeyDown(object? sender, KeyEventArgs e)
    {
        WakeChrome();
        if (e.Key == Key.Escape && UrlPanel.IsVisible)
        {
            CloseUrlPanel();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && QueueDrawer.IsVisible)
        {
            _presentation.IsQueueOpen = false;
            RestoreRouteFocus(QueueButton);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && LibraryPanel.IsVisible)
        {
            CloseLibrary();
            e.Handled = true;
            return;
        }
        if (_presentation.HasPendingResume)
        {
            if (e.Key == Key.Enter) _presentation.Registry.Execute(Player.Application.PlayerCommandId.AcceptResume);
            else if (e.Key == Key.Escape) _presentation.Registry.Execute(Player.Application.PlayerCommandId.DeclineResume);
            else return;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && OpenMenu.IsVisible)
        {
            CloseOpenMenu();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && SettingsPanel.IsVisible)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _presentation.IsPictureInPicture)
        {
            ExitPictureInPicture();
            e.Handled = true;
            return;
        }
        if (SettingsPanel.IsVisible || ShortcutFocusPolicy.Suppresses(e.Source)) return;
        string key = e.Key == Key.OemComma ? "," : e.Key.ToString();
        string modifiers = (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "");
        string gesture = PlayerShortcutPolicy.Normalize(modifiers + key);
        IReadOnlyDictionary<PlayerCommandId, string> active = PlayerShortcutPolicy.Effective(_presentation.Registry, _settings.Current.ShortcutOverrides);
        e.Handled = PlayerShortcutPolicy.TryDispatch(_presentation.Registry, active, gesture);
    }

    private void ToggleFullscreen()
    {
        ExitPictureInPicture();
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _presentation.IsFullscreen = WindowState == WindowState.FullScreen;
        _chromeVisibility.SetFullscreen(_presentation.IsFullscreen);
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (_presentation.IsPlaying) _autoHideTimer.Start();
    }

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

    private static MediaHistoryService CreateHistory()
    {
        MediaHistoryService history = new(new FileMediaHistoryStorage());
        history.Load();
        return history;
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
        _presentation.Registry.Execute(Player.Application.PlayerCommandId.SubmitUrl);
        if (!_presentation.HasOpenError) CloseUrlPanel();
        else Dispatcher.UIThread.Post(() => { UrlTextBox.Focus(); UrlTextBox.SelectAll(); });
    }

    private void OnUrlTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnUrlSubmit(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseUrlPanel(); e.Handled = true; }
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
        _presentation.Registry.Execute(Player.Application.PlayerCommandId.OpenMedia);
        CloseOpenMenu();
    }

    private void OnOpenRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RecentMediaEntry recent })
            _presentation.Registry.Execute(Player.Application.PlayerCommandId.OpenRecent, recent.Source);
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

    private void OnMediaDragOver(object? sender, DragEventArgs e)
    {
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        bool local = items is { Length: > 0 } && items.All(item => item.Path.IsFile);
        e.DragEffects = local ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMediaDrop(object? sender, DragEventArgs e)
    {
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        string[] localSources = items is { Length: > 0 } && items.All(item => item.Path.IsFile)
            ? items.Select(item => item.Path.LocalPath).ToArray()
            : [];
        _presentation.OpenDroppedSources(localSources);
        UpdateOpenErrorSurface();
        e.Handled = true;
    }

    private void OnQueueDrawerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_focusQueueOnLayout && QueueDrawer.IsEffectivelyVisible && CloseQueueButton.Focus())
            _focusQueueOnLayout = false;
    }

    private void OnDrawerTabsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Button current || e.KeyModifiers != KeyModifiers.None) return;
        Button[] tabs = DrawerTabs.Children.OfType<Button>().ToArray();
        int index = Array.IndexOf(tabs, current);
        if (index < 0) return;
        int next = e.Key switch
        {
            Key.Left => (index + tabs.Length - 1) % tabs.Length,
            Key.Right => (index + 1) % tabs.Length,
            Key.Home => 0,
            Key.End => tabs.Length - 1,
            _ => -1
        };
        if (next < 0) return;
        Button target = tabs[next];
        target.Focus(NavigationMethod.Directional);
        if (target.Command?.CanExecute(target.CommandParameter) == true) target.Command.Execute(target.CommandParameter);
        e.Handled = true;
    }

    private void OnMoveQueueItemUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueueItem item } && item.Index > 0)
            _presentation.Registry.Execute(PlayerCommandId.QueueMove, new QueueMove(item.Token, item.Index - 1));
    }

    private void OnMoveQueueItemDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueueItem item } && item.Index + 1 < _presentation.QueueItems.Count)
            _presentation.Registry.Execute(PlayerCommandId.QueueMove, new QueueMove(item.Token, item.Index + 1));
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
            Dispatcher.UIThread.Post(() => ResumeAction.Focus());
        }
        else
        {
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

    private void OnAcceptResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(Player.Application.PlayerCommandId.AcceptResume);

    private void OnDeclineResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(Player.Application.PlayerCommandId.DeclineResume);

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


    private void ApplyLiveSettings(PlayerSettings settings)
    {
        _chromeVisibility.SetPlaybackState(_presentation.IsPlaying, settings.ReduceMotion);
        // Playback is deliberately dark, including the platform-owned caption.
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        _autoHideTimer.Interval = TimeSpan.FromSeconds(settings.ControlTimeoutSeconds);
        Classes.Set("reduced-motion", settings.ReduceMotion);
        if (settings.ReduceMotion) SetChromeVisibility(true);
        Classes.Set("compact-density", settings.Density == "Compact");
    }

    private void OnSpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackRate) return;
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem { Tag: string value } &&
            double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double rate))
            _presentation.Registry.Execute(Player.Application.PlayerCommandId.SetPlaybackRate, rate);
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

    internal void EnableGPUCompositionSmoke(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        VideoSurface.ProbeSharedTextureForSmoke = true;
        ulong firstDeviceGeneration = 0;
        ulong firstResourceGeneration = 0;
        string? firstAdapter = null;
        int phase = 0;
        DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            Console.Error.WriteLine(
                "[composition-ui-smoke] Timed out waiting for a GPU frame.");
            desktop.Shutdown(1);
        };
        VideoSurface.StatusChanged += status =>
        {
            Console.WriteLine($"[composition-ui-smoke] {status}");
            if (status.StartsWith(
                    "D3D11 presentation failed",
                    StringComparison.Ordinal) ||
                status.Contains(
                    "using CPU BGRA fallback",
                    StringComparison.Ordinal))
            {
                timeout.Stop();
                desktop.Shutdown(1);
            }
        };
        VideoSurface.GPUFramePresentedForSmoke +=
            (deviceGeneration, resourceGeneration, adapter) =>
            {
                if (phase == 0)
                {
                    firstDeviceGeneration = deviceGeneration;
                    firstResourceGeneration = resourceGeneration;
                    firstAdapter = adapter;
                    phase = 1;
                    Console.WriteLine(
                        "[composition-ui-smoke] resize-fullscreen " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Width += 120;
                    Height += 80;
                    WindowState = WindowState.FullScreen;
                    return;
                }

                if (phase == 1)
                {
                    phase = 2;
                    WindowState = WindowState.Normal;
                    Console.WriteLine(
                        "[composition-ui-smoke] PASS resize-fullscreen " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Console.WriteLine(
                        "[composition-ui-smoke] reopening " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Dispatcher.UIThread.Post(VideoSurface.ReopenForSmoke);
                    return;
                }

                if (resourceGeneration == firstResourceGeneration)
                    return;

                if (deviceGeneration <= firstDeviceGeneration ||
                    !string.Equals(
                        firstAdapter,
                        adapter,
                        StringComparison.Ordinal))
                {
                    timeout.Stop();
                    Console.Error.WriteLine(
                        "[composition-ui-smoke] Reopen did not recreate " +
                        "resources on the same adapter.");
                    desktop.Shutdown(1);
                    return;
                }

                timeout.Stop();
                Console.WriteLine(
                    "[composition-ui-smoke] PASS reopen " +
                    $"device-generation={deviceGeneration} " +
                    $"resource-generation={resourceGeneration} " +
                    $"adapter-luid={adapter}");
                desktop.Shutdown(0);
            };
        Opened += (_, _) => { OpenTestMedia(); timeout.Start(); };
    }

    internal void EnableLayoutSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _presentation.IsHomeOpen = false;
        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 720;
        int phase = 0;
        DispatcherTimer? timeout = null;
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            try
            {
                double width = ClientSize.Width;
                bool compact = width <= 480;
                Console.WriteLine($"[layout-smoke] phase={phase} width={width}");
                switch (phase)
                {
                    case 0: ValidateLayout(width, compact); ValidateTopChrome(width); OpenMenu.IsVisible = true; UrlPanel.IsVisible = true; SettingsPanel.IsVisible = true; _presentation.IsQueueOpen = true; _presentation.IsLibraryOpen = true; phase = 30; return;
                    case 30: ValidateLibraryDrawer(width); ClearLibraryButton.IsEnabled = true; OnClearLibrary(null, new Avalonia.Interactivity.RoutedEventArgs()); phase = 32; return;
                    case 32: ValidateSurface("library-clear-prompt", ClearLibraryPrompt, [ClearLibraryPrompt, CancelClearLibraryButton]); OnCancelClearLibrary(null, new Avalonia.Interactivity.RoutedEventArgs()); phase = 33; return;
                    case 33: if (!ClearLibraryButton.IsVisible) throw new InvalidOperationException("[layout-smoke] FAIL library clear focus restoration"); _presentation.IsLibraryOpen = false; ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase = 1; return;
                    case 1: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 20; return;
                    case 20: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction]); CloseOpenMenu(); ShowUrlPanel(); phase = 2; return;
                    case 2: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 3: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages[0]; ApplyResponsiveSettings(); phase++; return;
                    case 4: ValidateSettingsLayout(width, compact); BringSettingsControlIntoView(SettingsPanel.SettingsClearRecentButton); phase++; return;
                    case 5: ValidateSettingsControl("recent-clear", SettingsPanel.SettingsClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearRecentButton); phase++; return;
                    case 6: ValidateSettingsControl("recent-confirm", SettingsPanel.SettingsConfirmClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsPanel.SettingsClearResumeButton); phase++; return;
                    case 7: ValidateSettingsControl("resume-clear", SettingsPanel.SettingsClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearResumeButton); phase++; return;
                    case 8: ValidateSettingsControl("resume-confirm", SettingsPanel.SettingsConfirmClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
                    case 9: ValidateToastForSmoke(); OpenErrorToast.IsVisible = false; Width = 480; phase++; return;
                    case 10: ApplyResponsiveChrome(); ApplyResponsiveSettings(); ValidateLayout(width, true); ValidateTopChrome(width); OpenMenu.IsVisible = true; UrlPanel.IsVisible = true; SettingsPanel.IsVisible = true; _presentation.IsQueueOpen = true; _presentation.IsLibraryOpen = true; phase = 31; return;
                    case 31: ValidateLibraryDrawer(width); _presentation.IsLibraryOpen = false; ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase = 11; return;
                    case 11: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 21; return;
                    case 21: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction]); CloseOpenMenu(); ShowUrlPanel(); phase = 12; return;
                    case 12: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 13: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages[0]; ApplyResponsiveSettings(); phase++; return;
                    case 14: ValidateSettingsLayout(width, true); BringSettingsControlIntoView(SettingsPanel.SettingsClearRecentButton); phase++; return;
                    case 15: ValidateSettingsControl("recent-clear", SettingsPanel.SettingsClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearRecentButton); phase++; return;
                    case 16: ValidateSettingsControl("recent-confirm", SettingsPanel.SettingsConfirmClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsPanel.SettingsClearResumeButton); phase++; return;
                    case 17: ValidateSettingsControl("resume-clear", SettingsPanel.SettingsClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearResumeButton); phase++; return;
                    case 18: ValidateSettingsControl("resume-confirm", SettingsPanel.SettingsConfirmClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
                    case 19: ValidateToastForSmoke(); OpenErrorToast.IsVisible = false; timer.Stop(); timeout?.Stop(); Console.WriteLine("[layout-smoke] PASS opening surfaces wide-and-compact"); desktop.Shutdown(0); return;
                    default: throw new InvalidOperationException("[layout-smoke] unexpected phase");
                }
            }
            catch (Exception exception)
            {
                timer.Stop(); timeout?.Stop(); Console.Error.WriteLine($"[layout-smoke] FAIL phase={phase} {exception}"); desktop.Shutdown(1);
            }
        };
        timeout = new() { Interval = TimeSpan.FromSeconds(14) };
        timeout.Tick += (_, _) => { timeout.Stop(); timer.Stop(); Console.Error.WriteLine("[layout-smoke] FAIL timeout"); desktop.Shutdown(1); };
        timer.Start(); timeout.Start();
    }

    private void ValidateLayout(double width, bool compact)
    {
        Rect dock = PlayerDock.Bounds;
        Control[] required = compact ? [CenterTransport, RightTransport, PlayerTimeline] : [CenterTransport, RightTransport, PlayerTimeline, PlayerVolume];
        Rect ToDock(Control control)
        {
            Point origin = control.TranslatePoint(default, PlayerDock) ?? default;
            return new Rect(origin, control.Bounds.Size);
        }
        bool contained = required.All(control =>
        {
            Rect rect = ToDock(control);
            return rect.Width > 0 && rect.Height > 0 && rect.Left >= 0 && rect.Top >= 0 && rect.Right <= dock.Width && rect.Bottom <= dock.Height;
        });
        bool rows = compact ? Grid.GetRow(RightTransport) == 2 : Grid.GetRow(RightTransport) == 1;
        bool separate = compact ? ToDock(RightTransport).Top >= ToDock(CenterTransport).Bottom : !ToDock(RightTransport).Intersects(ToDock(CenterTransport));
        Console.WriteLine($"[layout-smoke] width={width} dock={dock} center={ToDock(CenterTransport)} right={ToDock(RightTransport)} timeline={ToDock(PlayerTimeline)} volume={ToDock(PlayerVolume)} rows={rows} contained={contained} separate={separate}");
        if (!contained || !rows || !separate || (!compact && Math.Abs(PlayerVolume.Bounds.Width - 120) > 0.1))
            throw new InvalidOperationException("[layout-smoke] FAIL bounds or row policy");
    }

    private void ShowOpenMenuForSmoke()
    {
        OpenMenu.IsVisible = true;
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        Dispatcher.UIThread.Post(() => OpenMenu.Focus());
    }

    private void ValidateTopChrome(double width)
    {
        Control[] controls = [QueueButton, OpenOptionsButton];
        bool contained = controls.All(control => IsContained(control));
        Rect open = GetClientRect(QueueButton);
        Rect settings = GetClientRect(OpenOptionsButton);
        bool separate = !open.Intersects(settings);
        Console.WriteLine($"[layout-smoke] top width={width} queue={open} more={settings} title-visible={TopChromeTitle.IsVisible} contained={contained} separate={separate}");
        if (!contained || !separate)
            throw new InvalidOperationException("[layout-smoke] FAIL top chrome bounds");
    }

    private void ValidateQueueDrawer(double width)
    {
        bool contained = IsContained(QueueDrawer) && QueueDrawer.IsVisible && QueueDrawer.Bounds.Width > 0 && QueueDrawer.Bounds.Width <= Math.Max(400, width - 24);
        Console.WriteLine($"[layout-smoke] queue-drawer width={width} bounds={QueueDrawer.Bounds} contained={contained}");
        if (!contained) throw new InvalidOperationException("[layout-smoke] FAIL queue drawer bounds");
    }

    private void ValidateLibraryDrawer(double width)
    {
        bool contained = IsContained(LibraryPanel) && LibraryPanel.IsVisible && LibraryPanel.Bounds.Width > 0 && LibraryPanel.Bounds.Width <= Math.Max(430, width - 32);
        bool exclusive = !QueueDrawer.IsVisible && !OpenMenu.IsVisible && !UrlPanel.IsVisible && !SettingsPanel.IsVisible && !ResumePanel.IsVisible;
        Console.WriteLine($"[layout-smoke] library-drawer width={width} bounds={LibraryPanel.Bounds} contained={contained} exclusive={exclusive}");
        if (!contained || !exclusive) throw new InvalidOperationException("[layout-smoke] FAIL library drawer bounds or overlay exclusivity");
    }

    private void ValidateMediaDrawer(double width)
    {
        Control[] controls = [QueueDrawer, AudioDelayInput];
        bool contained = controls.All(IsContained) && AudioDelayInput.Bounds.Width > 0 && AudioDelayInput.Bounds.Width <= 112.1;
        Console.WriteLine($"[layout-smoke] media-drawer width={width} drawer={QueueDrawer.Bounds} audio-delay={AudioDelayInput.Bounds} contained={contained}");
        if (!contained) throw new InvalidOperationException("[layout-smoke] FAIL media drawer bounds");
    }

    private void ValidateSurface(string name, Control surface, Control[] required)
    {
        bool contained = required.All(IsContained);
        Console.WriteLine($"[layout-smoke] {name} bounds={surface.Bounds} contained={contained}");
        if (!contained || (name == "open-menu" && GetClientRect(OpenMenu).Top < GetClientRect(OpenOptionsButton).Bottom))
            throw new InvalidOperationException($"[layout-smoke] FAIL {name} bounds or capability gate");
    }

    private Rect GetClientRect(Control control)
    {
        Point origin = control.TranslatePoint(default, this) ?? new Point(double.NaN, double.NaN);
        return new Rect(origin, control.Bounds.Size);
    }

    private bool IsContained(Control control)
    {
        Rect rect = GetClientRect(control);
        return rect.Width > 0 && rect.Height > 0 && !double.IsNaN(rect.X) && !double.IsNaN(rect.Y) &&
            rect.Left >= 0 && rect.Top >= 0 && rect.Right <= ClientSize.Width && rect.Bottom <= ClientSize.Height;
    }

    private bool IsContainedInSettings(Control control)
    {
        Rect panel = GetClientRect(SettingsPanel);
        Rect rect = GetClientRect(control);
        return IsContained(control) && rect.Left >= panel.Left && rect.Top >= panel.Top && rect.Right <= panel.Right && rect.Bottom <= panel.Bottom;
    }

    private void ValidateSettingsLayout(double width, bool compact)
    {
        Control navigation = compact ? SettingsPanel.CompactSettingsPages : SettingsPanel.SettingsPages;
        Control[] required = [SettingsPanel, SettingsPanel.SettingsSearch, navigation, SettingsPanel.SettingsContent];
        bool contained = required.All(IsContained);
        Console.WriteLine($"[settings-layout-smoke] width={width} compact={compact} panel={SettingsPanel.Bounds} search={SettingsPanel.SettingsSearch.Bounds} navigation={navigation.Bounds} content={SettingsPanel.SettingsContent.Bounds} recent-clear={SettingsPanel.SettingsClearRecentButton.Bounds} resume-clear={SettingsPanel.SettingsClearResumeButton.Bounds} open={OpenSettingsButton.Bounds} reset={SettingsPanel.SettingsResetRow.Bounds} contained={contained}");
        if (!contained || !navigation.IsVisible)
            throw new InvalidOperationException("[settings-layout-smoke] FAIL bounds or responsive navigation");
    }

    private void BringSettingsControlIntoView(Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.Post(() => control.Focus());
    }

    private void ValidateSettingsControl(string name, Control control)
    {
        Rect rect = GetClientRect(control);
        bool contained = control.IsVisible && IsContainedInSettings(control);
        Console.WriteLine($"[settings-layout-smoke] {name} rect={rect} panel={GetClientRect(SettingsPanel)} contained={contained}");
        if (!contained)
            throw new InvalidOperationException($"[settings-layout-smoke] FAIL {name} is not visible in settings viewport");
    }

    private void ValidateToastForSmoke()
    {
        Rect toast = GetClientRect(OpenErrorToast);
        Rect dock = GetClientRect(PlayerDock);
        bool valid = IsContained(OpenErrorToast) && !toast.Intersects(dock) && !OpenMenu.IsVisible && !UrlPanel.IsVisible && !ResumePanel.IsVisible && !SettingsPanel.IsVisible;
        Console.WriteLine($"[layout-smoke] toast={toast} dock={dock} valid={valid}");
        if (!valid)
            throw new InvalidOperationException("[layout-smoke] FAIL toast geometry");
    }

    internal void EnableAdapterMismatchSmoke(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        VideoSurface.ForceUnavailableAdapterForSmoke = true;
        bool fallbackObserved = false;
        DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            Console.Error.WriteLine(
                "[adapter-mismatch-smoke] Timed out waiting for safe CPU " +
                "fallback.");
            desktop.Shutdown(1);
        };
        VideoSurface.StatusChanged += status =>
        {
            Console.WriteLine($"[adapter-mismatch-smoke] {status}");
            if (status.Contains(
                    "fallback=requested-adapter-unavailable",
                    StringComparison.Ordinal) ||
                status.Contains(
                    "fallback=adapter-mismatch",
                    StringComparison.Ordinal))
            {
                fallbackObserved = true;
            }
            else if (status == "D3D11 frame presented by Avalonia compositor")
            {
                timeout.Stop();
                Console.Error.WriteLine(
                    "[adapter-mismatch-smoke] A mismatched producer was " +
                    "incorrectly presented.");
                desktop.Shutdown(1);
            }
            else if (status == "CPU BGRA fallback frame presented" &&
                fallbackObserved)
            {
                timeout.Stop();
                Console.WriteLine(
                    "[adapter-mismatch-smoke] PASS safe CPU fallback");
                desktop.Shutdown(0);
            }
        };
        Opened += (_, _) => { OpenTestMedia(); timeout.Start(); };
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

    private void SetChromeVisibility(bool visible)
    {
        PlayerDock.IsVisible = visible && !_presentation.IsHomeOpen && !_presentation.IsPictureInPicture;
        TopChrome.IsVisible = visible && !_presentation.IsHomeOpen && !_presentation.IsPictureInPicture;
        PictureInPictureChrome.IsVisible = visible && _presentation.IsPictureInPicture;
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

    private void OnEngineStateChanged(object? sender, MediaEngineState state) =>
        _presentation.ApplyEngineState(state);

    private void OpenTestMedia()
    {
        ExitPictureInPicture();
        if (FindTestFile() is { } testFile)
            VideoSurface.MediaPath = testFile;
    }

    private static string? FindTestFile()
    {
        string testFile = RepositoryPaths.TestMediaPath;
        return File.Exists(testFile) ? testFile : null;
    }
}

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
using Conic.Interop;
using Conic.Services;
using Conic.ViewModels;

namespace Conic;

public partial class MainWindow : Window
{
    private readonly PlayerPresentationViewModel _presentation;
    private readonly PlayerSettingsService _settings;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly ChromeVisibilityController _chromeVisibility = new();
    private bool _isSeeking;
    private bool _isVolumeDragging;

    public MainWindow()
    {
        InitializeComponent();
        _settings = new PlayerSettingsService(new FilePlayerSettingsStorage());
        _settings.Load();
        _settingsViewModel = new SettingsViewModel(_settings);
        SettingsPanel.DataContext = _settingsViewModel;
        _presentation = new PlayerPresentationViewModel(
            new CompositionVideoTransport(VideoSurface),
            new StorageMediaSourcePicker(StorageProvider),
            OpenTestMedia,
            ToggleFullscreen,
            OpenSettings,
            _settings,
            new MediaSourceValidator(new SystemMediaFileSystem()),
            CreateHistory(),
            showUrlPanel: ShowUrlPanel);
        DataContext = _presentation;
        _presentation.PropertyChanged += OnPresentationPropertyChanged;
        UpdateHistorySettingsControls();
        _settings.Changed += (_, settings) => ApplyLiveSettings(settings);
        ApplyLiveSettings(_settings.Current);
        _autoHideTimer.Tick += (_, _) =>
        {
            bool menuOpen = (VideoArea.ContextMenu?.IsOpen ?? false) || SettingsPanel.IsVisible || OpenMenu.IsVisible || UrlPanel.IsVisible || ResumePanel.IsVisible || QueueDrawer.IsVisible;
            bool chromeFocused = PlayerDock.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin;
            bool popupOpen = menuOpen || VideoArea.IsKeyboardFocusWithin &&
                (RightTransport.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin);
            bool chromePointerOver = TopChrome.IsPointerOver || PlayerDock.IsPointerOver;
            if (_chromeVisibility.TryHide(_isSeeking || _isVolumeDragging, popupOpen, chromePointerOver, chromeFocused))
                SetChromeVisibility(false);
        };
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Closed += (_, _) => { _presentation.OnClosing(); _autoHideTimer.Stop(); };
        SizeChanged += (_, _) => { ApplyResponsiveChrome(); ApplyResponsiveSettings(); };
        Loaded += (_, _) =>
        {
            if (!Environment.GetCommandLineArgs().Contains("--layout-smoke") &&
                string.IsNullOrWhiteSpace(VideoSurface.MediaPath) &&
                FindTestFile() is { } testFile)
            {
                VideoSurface.MediaPath = testFile;
            }
        };
    }

    private void OnPlayerInteraction(object? sender, PointerEventArgs e)
    {
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (WindowState == WindowState.FullScreen) _autoHideTimer.Start();
    }

    private void OnPlayerKeyDown(object? sender, KeyEventArgs e)
    {
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (e.Key == Key.Escape && UrlPanel.IsVisible)
        {
            CloseUrlPanel();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && QueueDrawer.IsVisible)
        {
            _presentation.IsQueueOpen = false;
            QueueButton.Focus();
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
        if (SettingsPanel.IsVisible || e.Source is TextBox or Slider or NumericUpDown or MenuItem) return;
        string key = e.Key == Key.OemComma ? "," : e.Key.ToString();
        string modifiers = (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "");
        string gesture = PlayerShortcutPolicy.Normalize(modifiers + key);
        IReadOnlyDictionary<PlayerCommandId, string> active = PlayerShortcutPolicy.Effective(_presentation.Registry, _settings.Current.ShortcutOverrides);
        e.Handled = PlayerShortcutPolicy.TryDispatch(_presentation.Registry, active, gesture);
    }

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _presentation.IsFullscreen = WindowState == WindowState.FullScreen;
        _chromeVisibility.SetFullscreen(_presentation.IsFullscreen);
        SetChromeVisibility(true);
        _autoHideTimer.Stop();
        if (_presentation.IsFullscreen) _autoHideTimer.Start();
    }

    private void OpenSettings()
    {
        OpenMenu.IsVisible = false;
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        SettingsPanel.IsVisible = true;
        UpdateOpenErrorSurface();
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => SettingsSearch.Focus());
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
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        OpenMenu.IsVisible = open;
        UpdateOpenErrorSurface();
        SetChromeVisibility(true);
        if (open) Dispatcher.UIThread.Post(() => OpenMenu.Focus());
    }

    private void ShowUrlPanel()
    {
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
        OpenOptionsButton.Focus();
    }

    private void CloseOpenMenu()
    {
        OpenMenu.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        OpenOptionsButton.Focus();
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
        if (e.PropertyName is nameof(PlayerPresentationViewModel.OpenError) or nameof(PlayerPresentationViewModel.HasOpenError))
            UpdateOpenErrorSurface();
        if (e.PropertyName == nameof(PlayerPresentationViewModel.IsQueueOpen) && QueueDrawer.IsVisible)
        {
            SetChromeVisibility(true);
            _autoHideTimer.Stop();
            Dispatcher.UIThread.Post(() => QueueDrawer.Focus());
        }
        if (e.PropertyName == nameof(PlayerPresentationViewModel.HasPendingResume))
            UpdateResumePrompt();
        if (e.PropertyName is nameof(PlayerPresentationViewModel.HasHistoryError) or nameof(PlayerPresentationViewModel.HistoryError) or nameof(PlayerPresentationViewModel.HasRecentItems) or nameof(PlayerPresentationViewModel.HasResumeItems))
            UpdateHistorySettingsControls();
    }

    private void UpdateOpenErrorSurface() => OpenErrorToast.IsVisible = !UrlPanel.IsVisible && !OpenMenu.IsVisible && !ResumePanel.IsVisible && !SettingsPanel.IsVisible && _presentation.HasOpenError;

    private void UpdateResumePrompt()
    {
        if (_presentation.HasPendingResume)
        {
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
            Dispatcher.UIThread.Post(() => TopChrome.Focus());
            UpdateOpenErrorSurface();
            if (_presentation.IsFullscreen) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
        }
    }

    private void UpdateHistorySettingsControls()
    {
        SettingsClearRecentButton.IsEnabled = _presentation.HasRecentItems;
        SettingsClearResumeButton.IsEnabled = _presentation.HasResumeItems;
        SettingsHistoryError.IsVisible = _presentation.HasHistoryError;
        SettingsHistoryError.Text = _presentation.HistoryError;
    }

    private void OnAcceptResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(Player.Application.PlayerCommandId.AcceptResume);

    private void OnDeclineResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _presentation.Registry.Execute(Player.Application.PlayerCommandId.DeclineResume);

    private void OnSettingsClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearRecentButton.IsVisible = false;
        SettingsClearRecentPrompt.IsVisible = true;
    }

    private void OnSettingsCancelClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearRecentPrompt.IsVisible = false;
        SettingsClearRecentButton.IsVisible = true;
    }

    private void OnSettingsConfirmClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _presentation.Registry.Execute(Player.Application.PlayerCommandId.ClearRecent);
        if (!_presentation.HasRecentItems)
        {
            SettingsClearRecentPrompt.IsVisible = false;
            SettingsClearRecentButton.IsVisible = true;
        }
    }

    private void OnSettingsClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearResumeButton.IsVisible = false;
        SettingsClearResumePrompt.IsVisible = true;
    }

    private void OnSettingsCancelClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearResumePrompt.IsVisible = false;
        SettingsClearResumeButton.IsVisible = true;
    }

    private void OnSettingsConfirmClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _presentation.Registry.Execute(Player.Application.PlayerCommandId.ClearResumeHistory);
        if (!_presentation.HasResumeItems)
        {
            SettingsClearResumePrompt.IsVisible = false;
            SettingsClearResumeButton.IsVisible = true;
        }
    }

    private void CloseSettings()
    {
        SettingsPanel.IsVisible = false;
        SettingsClearRecentPrompt.IsVisible = false;
        SettingsClearResumePrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        TopChrome.Focus();
        if (_presentation.IsFullscreen) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void OnSettingsClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseSettings();
    private void OnSettingsSearchChanged(object? sender, TextChangedEventArgs e) => _settingsViewModel.Search = (sender as TextBox)?.Text ?? string.Empty;

    private void ApplyLiveSettings(PlayerSettings settings)
    {
        RequestedThemeVariant = settings.Theme == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Default;
        _autoHideTimer.Interval = TimeSpan.FromSeconds(settings.ControlTimeoutSeconds);
        Classes.Set("compact-density", settings.Density == "Compact");
    }

    private void OnSpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem { Tag: string value } &&
            double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double rate))
            _presentation.Registry.Execute(Player.Application.PlayerCommandId.SetPlaybackRate, rate);
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
        timeout.Start();
    }

    internal void EnableLayoutSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
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
                    case 0: ValidateLayout(width, compact); ValidateTopChrome(width); ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase++; return;
                    case 1: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 20; return;
                    case 20: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction, RecentEmptyState]); CloseOpenMenu(); ShowUrlPanel(); phase = 2; return;
                    case 2: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 3: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages[0]; ApplyResponsiveSettings(); phase++; return;
                    case 4: ValidateSettingsLayout(width, compact); BringSettingsControlIntoView(SettingsClearRecentButton); phase++; return;
                    case 5: ValidateSettingsControl("recent-clear", SettingsClearRecentButton); SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsConfirmClearRecentButton); phase++; return;
                    case 6: ValidateSettingsControl("recent-confirm", SettingsConfirmClearRecentButton); SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsClearResumeButton); phase++; return;
                    case 7: ValidateSettingsControl("resume-clear", SettingsClearResumeButton); SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsConfirmClearResumeButton); phase++; return;
                    case 8: ValidateSettingsControl("resume-confirm", SettingsConfirmClearResumeButton); SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
                    case 9: ValidateToastForSmoke(); OpenErrorToast.IsVisible = false; Width = 480; phase++; return;
                    case 10: ApplyResponsiveChrome(); ApplyResponsiveSettings(); ValidateLayout(width, true); ValidateTopChrome(width); ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase++; return;
                    case 11: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 21; return;
                    case 21: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction, RecentEmptyState]); CloseOpenMenu(); ShowUrlPanel(); phase = 12; return;
                    case 12: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 13: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages[0]; ApplyResponsiveSettings(); phase++; return;
                    case 14: ValidateSettingsLayout(width, true); BringSettingsControlIntoView(SettingsClearRecentButton); phase++; return;
                    case 15: ValidateSettingsControl("recent-clear", SettingsClearRecentButton); SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsConfirmClearRecentButton); phase++; return;
                    case 16: ValidateSettingsControl("recent-confirm", SettingsConfirmClearRecentButton); SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsClearResumeButton); phase++; return;
                    case 17: ValidateSettingsControl("resume-clear", SettingsClearResumeButton); SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsConfirmClearResumeButton); phase++; return;
                    case 18: ValidateSettingsControl("resume-confirm", SettingsConfirmClearResumeButton); SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
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
        Control[] required = [CenterTransport, RightTransport, PlayerTimeline, PlayerVolume];
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
        if (!contained || !rows || !separate || Math.Abs(PlayerVolume.Bounds.Width - 112) > 0.1)
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
        Control[] controls = [OpenOptionsButton, OpenSettingsButton];
        bool contained = controls.All(control => IsContained(control));
        Rect open = GetClientRect(OpenOptionsButton);
        Rect settings = GetClientRect(OpenSettingsButton);
        bool separate = !open.Intersects(settings);
        Console.WriteLine($"[layout-smoke] top width={width} open={open} settings={settings} title-visible={TopChromeTitle.IsVisible} always-visible={AlwaysOnTopChrome.IsVisible} contained={contained} separate={separate}");
        if (!contained || !separate)
            throw new InvalidOperationException("[layout-smoke] FAIL top chrome bounds");
    }

    private void ValidateQueueDrawer(double width)
    {
        bool contained = IsContained(QueueDrawer) && QueueDrawer.IsVisible && QueueDrawer.Bounds.Width > 0 && QueueDrawer.Bounds.Width <= Math.Max(390, width - 32);
        Console.WriteLine($"[layout-smoke] queue-drawer width={width} bounds={QueueDrawer.Bounds} contained={contained}");
        if (!contained) throw new InvalidOperationException("[layout-smoke] FAIL queue drawer bounds");
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
        if (!contained || (name == "open-menu" && OpenMenu.Bounds.Height > 380))
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
        Control navigation = compact ? CompactSettingsPages : SettingsPages;
        Control[] required = [SettingsPanel, SettingsSearch, navigation, SettingsContent, OpenSettingsButton];
        bool contained = required.All(IsContained);
        Console.WriteLine($"[settings-layout-smoke] width={width} compact={compact} panel={SettingsPanel.Bounds} search={SettingsSearch.Bounds} navigation={navigation.Bounds} content={SettingsContent.Bounds} recent-clear={SettingsClearRecentButton.Bounds} resume-clear={SettingsClearResumeButton.Bounds} open={OpenSettingsButton.Bounds} reset={SettingsResetRow.Bounds} contained={contained}");
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
        timeout.Start();
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
        if (_presentation.IsFullscreen) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void EndSeek()
    {
        _isSeeking = false;
        _presentation.CommitSeek();
        if (_presentation.IsFullscreen) { _autoHideTimer.Stop(); _autoHideTimer.Start(); }
    }

    private void SetChromeVisibility(bool visible)
    {
        PlayerDock.IsVisible = visible;
        TopChrome.IsVisible = visible;
    }

    private void ApplyResponsiveChrome()
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(Bounds.Width);
        bool compactTopChrome = Bounds.Width < 700;
        TopChrome.Margin = compactTopChrome ? new Thickness(12, 12, 12, 0) : new Thickness(24, 20, 24, 0);
        TopChromeTitle.IsVisible = !compactTopChrome;
        AlwaysOnTopChrome.IsVisible = !compactTopChrome;
        CenterTransport.Spacing = layout.Spacing;
        RightTransport.Spacing = layout.Spacing;
        Grid.SetRow(RightTransport, layout.RightRow);
        Grid.SetColumn(RightTransport, layout.RightColumn);
        Grid.SetColumnSpan(RightTransport, layout.RightColumnSpan);
        Grid.SetColumn(CenterTransport, 0);
        Grid.SetColumnSpan(CenterTransport, 3);
        RightTransport.HorizontalAlignment = layout.IsCompact
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Right;
    }

    private void ApplyResponsiveSettings()
    {
        bool compact = Bounds.Width < 900;
        SettingsPages.IsVisible = !compact;
        CompactSettingsPages.IsVisible = compact;
        SettingsGrid.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("190,*");
        SettingsGrid.RowDefinitions = compact ? new RowDefinitions("Auto,Auto,*") : new RowDefinitions("Auto,*");
        Grid.SetRow(SettingsSearch, 0);
        Grid.SetColumn(SettingsSearch, 0);
        Grid.SetColumnSpan(SettingsSearch, compact ? 1 : 2);
        Grid.SetRow(SettingsPages, 1); Grid.SetColumn(SettingsPages, 0);
        Grid.SetRow(CompactSettingsPages, 1); Grid.SetColumn(CompactSettingsPages, 0); Grid.SetColumnSpan(CompactSettingsPages, compact ? 1 : 2);
        Grid.SetRow(SettingsContent, compact ? 2 : 1); Grid.SetColumn(SettingsContent, compact ? 0 : 1);
    }

    private void OnEngineStateChanged(object? sender, MediaEngineState state) =>
        _presentation.ApplyEngineState(state);

    private void OpenTestMedia()
    {
        if (FindTestFile() is { } testFile)
            VideoSurface.MediaPath = testFile;
    }

    private static string? FindTestFile()
    {
        string testFile = RepositoryPaths.TestMediaPath;
        return File.Exists(testFile) ? testFile : null;
    }
}

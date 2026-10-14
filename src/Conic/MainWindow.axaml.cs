using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Player.Application;
using Player.Contracts;
using Conic.Controls;
using Conic.Interop;
using Conic.Services;
using Conic.ViewModels;
using VLCLR;

namespace Conic;

public partial class MainWindow : Window
{
    private readonly PlayerPresentationViewModel _presentation;
    private bool _focusQueueOnLayout;
    private readonly PlayerSettingsService _settings;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    // Remember volume writes once the volume has settled, not on every step of a drag.
    private readonly DispatcherTimer _volumeSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ChromeVisibilityController _chromeVisibility = new();
    private readonly WindowGeometryStore _windowGeometry;
    private readonly StorageMediaSourcePicker _mediaPicker;
    private WindowState _preFullscreenState = WindowState.Normal;
    private WindowState? _requestedWindowState;
    private long _windowStateRequestedAt;
    private bool _isSeeking;
    private bool _isVolumeDragging;
    private bool _keyboardNavigation;

    public MainWindow()
    {
        InitializeComponent();
        PlatformChrome.Apply(this);
        TopChrome.Padding = PlatformChrome.ClearOfCaptionButtons(TopChrome.Padding);
        OpenMenu.Margin = PlatformChrome.ClearOfCaptionButtons(OpenMenu.Margin);
        // Settings first: Remember window size and position decides whether the window opens where it was.
        _settings = new PlayerSettingsService(new FilePlayerSettingsStorage());
        _settings.Load();
        _windowGeometry = new WindowGeometryStore(PlayerProfile.FilePath("window.json"));
        RestoreWindowGeometry();
        AddHandler(PointerPressedEvent, OnPlayerPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
                _keyboardNavigation = true;
            WakeChrome();
        }, RoutingStrategies.Tunnel, true);
        _mediaPicker = new StorageMediaSourcePicker(StorageProvider);
        _presentation = new PlayerPresentationViewModel(
            new CompositionVideoTransport(VideoSurface),
            _mediaPicker,
            OpenTestMedia,
            ToggleFullscreen,
            OpenSettings,
            _settings,
            new MediaSourceValidator(new SystemMediaFileSystem()),
            CreateHistory(),
            showUrlPanel: ShowUrlPanel,
            clipboard: new AvaloniaClipboardService(() => Clipboard),
            volumeMemory: new FileVolumeMemory(PlayerProfile.FilePath("volume.json")));
        _settingsViewModel = new SettingsViewModel(_settings, _presentation.Registry);
        SettingsPanel.Initialize(_settingsViewModel, _presentation);
        SettingsPanel.CloseRequested += (_, _) => CloseSettings();
        DataContext = _presentation;
        HomeScreen.MoreRequested += (_, _) => OnOpenOptions(null, new RoutedEventArgs());
        OpenMenu.AddHandler(Button.ClickEvent, OnOpenMenuClick);
        // Tunnel: the multi-line field would take Enter for a new line first.
        UrlTextBox.AddHandler(KeyDownEvent, OnUrlTextKeyDown, RoutingStrategies.Tunnel);
        _presentation.PropertyChanged += OnPresentationPropertyChanged;
        _presentation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlayerPresentationViewModel.Volume) or nameof(PlayerPresentationViewModel.IsMuted))
            { _volumeSaveTimer.Stop(); _volumeSaveTimer.Start(); }
        };
        _volumeSaveTimer.Tick += (_, _) => { _volumeSaveTimer.Stop(); _presentation.SaveVolume(); };
        InitializePictureInPicture();
        InitializePlaybackSettings();
        InitializeInterfaceSettings();
        InitializeControlSettings();
        InitializeMacMenu();
        InitializeMessages();
        InitializeDialogs();
        UpdatePlaybackRateSelection();
        UpdateHistorySettingsControls();
        _settings.Changed += (_, settings) => ApplyLiveSettings(settings);
        ApplyLiveSettings(_settings.Current);
        SetChromeVisibility(false);
        _autoHideTimer.Tick += (_, _) =>
        {
            // Docked (windowed) chrome never hides; stop polling until it overlays the video again.
            if (!_chromeVisibility.HidesAutomatically) { _autoHideTimer.Stop(); return; }
            bool menuOpen = (VideoArea.ContextMenu?.IsOpen ?? false) || SettingsPanel.IsVisible || OpenMenu.IsVisible || UrlPanel.IsVisible || IsDialogOpen || ResumePanel.IsVisible || QueueDrawer.IsVisible || LibraryPanel.IsVisible;
            bool chromeFocused = _keyboardNavigation && (PlayerDock.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin || PictureInPictureChrome.IsKeyboardFocusWithin);
            bool chromePointerOver = TopChrome.IsPointerOver || PlayerDock.IsPointerOver || PictureInPictureHeader.IsPointerOver || PictureInPicturePlay.IsPointerOver || PictureInPictureTimeline.IsPointerOver;
            if (_chromeVisibility.TryHide(_isSeeking || _isVolumeDragging, menuOpen, chromePointerOver, chromeFocused))
            {
                SetChromeVisibility(false);
                _autoHideTimer.Stop();
            }
        };
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        SubtitleLayer.Surface = VideoSurface;
        Closed += (_, _) => { SaveWindowGeometry(); _presentation.OnClosing(); _autoHideTimer.Stop(); _volumeSaveTimer.Stop(); };
        SizeChanged += (_, _) => { ApplyResponsiveChrome(); ApplyResponsiveSettings(); PlaceCaptionButtons(); };
        Activated += (_, _) => { WakeChrome(); PlaceCaptionButtons(); };
        Deactivated += (_, _) => PlaceCaptionButtons();
        Opened += (_, _) => PlaceCaptionButtons();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) OnWindowStateChanged();
            if (e.Property == WindowStateProperty || e.Property == WindowDecorationsProperty) PlaceCaptionButtons();
        };
    }

    /// <summary>
    /// Centers the macOS traffic lights on the 40 px title bar, which Home and
    /// the player share. Posted so it runs after AppKit's own title bar layout.
    /// </summary>
    private void PlaceCaptionButtons()
    {
        if (!PlatformChrome.UsesSystemCaptionButtons) return;
        Dispatcher.UIThread.Post(() =>
            PlatformChrome.PlaceCaptionButtons(this, PlatformChrome.TitleBarHeight / 2),
            DispatcherPriority.Background);
    }

    private static MediaHistoryService CreateHistory()
    {
        MediaHistoryService history = new(new FileMediaHistoryStorage());
        history.Load();
        return history;
    }

    private void ApplyLiveSettings(PlayerSettings settings)
    {
        // Settings › Interface › Auto-hide the controls (Reduce motion kept
        // them up before version 8; the settings migration carries that over).
        _chromeVisibility.SetPlaybackState(_presentation.IsPlaying, settings.AutoHideControls);
        // Theme: Dark, Light or System (follows the OS); the accent family
        // follows the Accent color. The video and the controls drawn over it
        // stay dark (Styles/PlayerPalette.axaml).
        if (Avalonia.Application.Current is { } application)
        {
            Theming.AppTheme.Apply(application, settings.Theme);
            Theming.AccentTheme.Apply(application, settings.Accent);
        }
        _autoHideTimer.Interval = TimeSpan.FromSeconds(settings.ControlTimeoutSeconds);
        VideoSurface.OpenPlaybackRate = settings.DefaultSpeed;
        ApplyEngineSettings(settings);
        Classes.Set("reduced-motion", settings.ReduceMotion);
        Theming.ReducedMotion.Apply(settings.ReduceMotion, this);
        // Auto-hide turned off shows the controls at once; Show controls in
        // fullscreen hides or shows them.
        SetChromeVisibility(_chromeVisibility.IsVisible);
        Classes.Set("compact-density", settings.Density == "Compact");
        ApplyResponsiveChrome();
    }

    /// <summary>
    /// Shows or hides the playback chrome. <paramref name="visible"/> only
    /// hides the bars where they overlay the video (fullscreen); docked
    /// around the video in a window they always show. Home covers them and
    /// picture in picture has its own overlay.
    /// </summary>
    private void SetChromeVisibility(bool visible)
    {
        // Settings › Interface › Show controls in fullscreen: off keeps the
        // bars away over fullscreen video (keyboard, mouse and menus still work).
        bool hiddenInFullscreen = _chromeVisibility.IsFullscreen && !_settings.Current.ShowControlsInFullscreen;
        bool bars = !_presentation.IsHomeOpen && !_presentation.IsPictureInPicture && !hiddenInFullscreen &&
            (visible || !_chromeVisibility.OverlaysVideo);
        PlayerDock.IsVisible = bars;
        TopChrome.IsVisible = bars;
        PictureInPictureChrome.IsVisible = visible && _presentation.IsPictureInPicture;
        // The traffic lights belong to the title bar: they show whenever it
        // does (always while docked), hide with it over fullscreen video and
        // stay on Home.
        if (PlatformChrome.UsesSystemCaptionButtons)
            PlatformChrome.SetCaptionButtonsVisible(this, _presentation.IsPictureInPicture ? visible : bars || _presentation.IsHomeOpen);
    }

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

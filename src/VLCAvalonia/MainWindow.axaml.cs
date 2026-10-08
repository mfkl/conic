using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Controls;
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
    private readonly WindowGeometryStore _windowGeometry;
    private WindowState _preFullscreenState = WindowState.Normal;
    private bool _isSeeking;
    private bool _isVolumeDragging;
    private bool _keyboardNavigation;

    public MainWindow()
    {
        InitializeComponent();
        PlatformChrome.Apply(this);
        TopChrome.Padding = PlatformChrome.ClearOfCaptionButtons(TopChrome.Padding);
        _windowGeometry = new WindowGeometryStore(PlayerProfile.FilePath("window.json"));
        RestoreWindowGeometry();
        AddHandler(PointerPressedEvent, OnPlayerPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
                _keyboardNavigation = true;
            WakeChrome();
        }, RoutingStrategies.Tunnel, true);
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
        HomeScreen.MoreRequested += (_, _) => OnOpenOptions(null, new RoutedEventArgs());
        _presentation.PropertyChanged += OnPresentationPropertyChanged;
        InitializePictureInPicture();
        InitializeMacMenu();
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
        SubtitleLayer.Surface = VideoSurface;
        Closed += (_, _) => { SaveWindowGeometry(); _presentation.OnClosing(); _autoHideTimer.Stop(); };
        SizeChanged += (_, _) => { ApplyResponsiveChrome(); ApplyResponsiveSettings(); PlaceCaptionButtons(); };
        Activated += (_, _) => { WakeChrome(); PlaceCaptionButtons(); };
        Deactivated += (_, _) => PlaceCaptionButtons();
        Opened += (_, _) => PlaceCaptionButtons();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == WindowDecorationsProperty) PlaceCaptionButtons();
        };
    }

    /// <summary>
    /// Centers the macOS traffic lights on the visible caption row: Home's
    /// 52 px header, or the player's 44 px row below the top chrome's padding.
    /// Posted so it runs after AppKit's own title bar layout.
    /// </summary>
    private void PlaceCaptionButtons()
    {
        if (!PlatformChrome.UsesSystemCaptionButtons) return;
        Dispatcher.UIThread.Post(() =>
            PlatformChrome.PlaceCaptionButtons(this, _presentation.IsHomeOpen ? 26 : TopChrome.Padding.Top + 22),
            DispatcherPriority.Background);
    }

    internal static IReadOnlyList<string> ToAudioArguments(PlayerSettings settings, bool? windows = null)
    {
        string output = AudioOutputPolicy.Effective(settings.AudioOutputModule, windows);
        return new VLCAudioStartupOptions(
            output == "Default" ? null : output,
            settings.NormalizationMaximumLevel is double level ? (float)level : null,
            settings.AudioPassthrough).ToArguments();
    }

    private static MediaHistoryService CreateHistory()
    {
        MediaHistoryService history = new(new FileMediaHistoryStorage());
        history.Load();
        return history;
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

    private void SetChromeVisibility(bool visible)
    {
        PlayerDock.IsVisible = visible && !_presentation.IsHomeOpen && !_presentation.IsPictureInPicture;
        TopChrome.IsVisible = visible && !_presentation.IsHomeOpen && !_presentation.IsPictureInPicture;
        PictureInPictureChrome.IsVisible = visible && _presentation.IsPictureInPicture;
        // The traffic lights belong to the top chrome: they hide with it over
        // playing video and stay on Home.
        if (PlatformChrome.UsesSystemCaptionButtons)
            PlatformChrome.SetCaptionButtonsVisible(this, visible || _presentation.IsHomeOpen);
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

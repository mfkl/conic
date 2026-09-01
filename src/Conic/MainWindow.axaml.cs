using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls.ApplicationLifetimes;
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
            _settings);
        DataContext = _presentation;
        _settings.Changed += (_, settings) => ApplyLiveSettings(settings);
        ApplyLiveSettings(_settings.Current);
        _autoHideTimer.Tick += (_, _) =>
        {
            bool menuOpen = VideoArea.ContextMenu?.IsOpen ?? false;
            bool chromeFocused = PlayerDock.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin;
            bool popupOpen = menuOpen || VideoArea.IsKeyboardFocusWithin &&
                (RightTransport.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin);
            bool chromePointerOver = TopChrome.IsPointerOver || PlayerDock.IsPointerOver;
            if (_chromeVisibility.TryHide(_isSeeking || _isVolumeDragging, popupOpen, chromePointerOver, chromeFocused))
                SetChromeVisibility(false);
        };
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Closed += (_, _) => _autoHideTimer.Stop();
        SizeChanged += (_, _) => ApplyResponsiveChrome();
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
        if (e.Source is TextBox) return;
        switch (e.Key)
        {
            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Control): _presentation.Registry.Execute(Player.Application.PlayerCommandId.OpenSettings); break;
            case Key.Escape when SettingsPanel.IsVisible: SettingsPanel.IsVisible = false; TopChrome.Focus(); break;
            case Key.F11: _presentation.Registry.Execute(Player.Application.PlayerCommandId.ToggleFullscreen); break;
            case Key.M: _presentation.Registry.Execute(Player.Application.PlayerCommandId.ToggleMute); break;
            case Key.Space: _presentation.Registry.Execute(Player.Application.PlayerCommandId.TogglePause); break;
            case Key.Left when e.KeyModifiers.HasFlag(KeyModifiers.Alt): _presentation.Registry.Execute(Player.Application.PlayerCommandId.SkipBackward); break;
            case Key.Right when e.KeyModifiers.HasFlag(KeyModifiers.Alt): _presentation.Registry.Execute(Player.Application.PlayerCommandId.SkipForward); break;
            default: return;
        }
        e.Handled = true;
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
        SettingsPanel.IsVisible = true;
        _chromeVisibility.Wake();
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => SettingsSearch.Focus());
    }

    private void OnSettingsClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SettingsPanel.IsVisible = false;
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
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (phase == 0) { ValidateLayout(1280, false); Width = 480; phase = 1; return; }
            if (phase == 1) { ValidateLayout(480, true); timer.Stop(); Console.WriteLine("[layout-smoke] PASS normal-and-compact"); desktop.Shutdown(0); }
        };
        DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(8) };
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
        if (!contained || !rows || !separate)
            throw new InvalidOperationException("[layout-smoke] FAIL bounds or row policy");
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

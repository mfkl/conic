using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Player.Contracts;
using Player.Application;
using VLCAvalonia.Interop;
using VLCAvalonia.Services;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia;

public partial class MainWindow : Window
{
    private readonly PlayerPresentationViewModel _presentation;
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly ChromeVisibilityController _chromeVisibility = new();
    private bool _isSeeking;

    public MainWindow()
    {
        InitializeComponent();
        _presentation = new PlayerPresentationViewModel(
            new CompositionVideoTransport(VideoSurface),
            new StorageMediaSourcePicker(StorageProvider),
            OpenTestMedia,
            ToggleFullscreen);
        DataContext = _presentation;
        _autoHideTimer.Tick += (_, _) =>
        {
            bool menuOpen = VideoArea.ContextMenu?.IsOpen ?? false;
            bool chromeFocused = PlayerDock.IsKeyboardFocusWithin || TopChrome.IsKeyboardFocusWithin;
            if (_chromeVisibility.TryHide(_isSeeking, menuOpen, tooltipOpen: false, chromeFocused))
                SetChromeVisibility(false);
        };
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Closed += (_, _) => _autoHideTimer.Stop();
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(VideoSurface.MediaPath) &&
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

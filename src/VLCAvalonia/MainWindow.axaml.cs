using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Player.Contracts;

namespace VLCAvalonia;

public partial class MainWindow : Window
{
    private bool _updatingControls;
    private bool _isSeeking;
    private TimeSpan _duration;

    public static readonly StyledProperty<string?> MediaPathProperty =
        AvaloniaProperty.Register<MainWindow, string?>(nameof(MediaPath));

    public string? MediaPath
    {
        get => GetValue(MediaPathProperty);
        set => SetValue(MediaPathProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();

        // Slider template parts handle pointer events themselves. Listen on
        // the tunnel route as well so playback polling cannot overwrite a
        // seek while the thumb is being dragged.
        PositionSlider.AddHandler(
            InputElement.PointerPressedEvent,
            OnPositionPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PositionSlider.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPositionReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        PositionSlider.PointerCaptureLost += OnPositionCaptureLost;

        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(VideoSurface.MediaPath) &&
                FindTestFile() is { } testFile)
            {
                VideoSurface.MediaPath = testFile;
            }
        };
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

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        VideoSurface.TogglePause();
    }

    private void OnStopClick(object? sender, RoutedEventArgs e) =>
        VideoSurface.Stop();

    private void OnPrevClick(object? sender, RoutedEventArgs e) { }
    private void OnNextClick(object? sender, RoutedEventArgs e) { }

    private void OnVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingControls)
            VideoSurface.SetVolume(e.NewValue / 100);
    }

    private void OnPositionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (PositionSlider.IsEnabled &&
            e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            _isSeeking = true;
        }
    }

    private void OnPositionReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSeeking)
            Dispatcher.UIThread.Post(CommitSeek);
    }

    private void OnPositionCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        if (_isSeeking)
            Dispatcher.UIThread.Post(CommitSeek);
    }

    private void OnPositionChanged(
        object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControls)
            return;

        if (_isSeeking)
        {
            ElapsedText.Text = FormatTime(_duration * e.NewValue);
            return;
        }

        // Keyboard interaction has no pointer release, so seek immediately.
        VideoSurface.Seek(e.NewValue);
    }

    private void CommitSeek()
    {
        if (!_isSeeking)
            return;

        _isSeeking = false;
        VideoSurface.Seek(PositionSlider.Value);
    }

    private void OnEngineStateChanged(object? sender, MediaEngineState state)
    {
        _updatingControls = true;
        try
        {
            _duration = state.Duration;
            if (!_isSeeking)
                PositionSlider.Value = state.Position;
            PositionSlider.IsEnabled = state.CanSeek;
            VolumeSlider.Value = state.Volume * 100;
            if (!_isSeeking)
                ElapsedText.Text = FormatTime(state.Elapsed);
            TimeText.Text = FormatTime(state.Duration);
            bool isPlaying = state.Status == PlaybackStatus.Playing;
            PlayIcon.IsVisible = !isPlaying;
            PauseIcon.IsVisible = isPlaying;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open Media File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media Files")
                {
                    Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.mp3", "*.flac", "*.mkv" }
                },
                FilePickerFileTypes.All
            }
        };

        var result = await StorageProvider.OpenFilePickerAsync(options);
        if (result.Count > 0)
        {
            var path = result[0].Path.LocalPath;
            VideoSurface.MediaPath = path;
        }
    }

    private void OnTestPatternClick(object? sender, RoutedEventArgs e)
    {
        var testFile = FindTestFile();
        if (testFile != null)
        {
            VideoSurface.MediaPath = testFile;
        }
    }

    private void OnTopMostChanged(object? sender, RoutedEventArgs e)
    {
        Topmost = TopMostCheck.IsChecked == true;
    }

    private static string? FindTestFile()
    {
        string testFile = Interop.RepositoryPaths.TestMediaPath;
        return System.IO.File.Exists(testFile) ? testFile : null;
    }
}

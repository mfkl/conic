using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Player.Contracts;

namespace Conic;

public partial class MainWindow : Window
{
    private bool _updatingControls;

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

        VideoSurface.StatusChanged += status => StatusText.Text = status;
        VideoSurface.EngineStateChanged += OnEngineStateChanged;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(VideoSurface.MediaPath) &&
                FindTestFile() is { } testFile)
            {
                VideoSurface.MediaPath = testFile;
                StatusText.Text = $"Test: {System.IO.Path.GetFileName(testFile)}";
            }
        };
    }

    internal void EnableGPUCompositionSmoke(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(15),
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
            if (status == "D3D11 frame presented by Avalonia compositor")
            {
                timeout.Stop();
                desktop.Shutdown(0);
            }
            else if (status.StartsWith(
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

    private void OnPositionReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_updatingControls)
            VideoSurface.Seek(PositionSlider.Value);
    }

    private void OnEngineStateChanged(object? sender, MediaEngineState state)
    {
        _updatingControls = true;
        try
        {
            PositionSlider.Value = state.Position;
            PositionSlider.IsEnabled = state.CanSeek;
            VolumeSlider.Value = state.Volume * 100;
            ElapsedText.Text = FormatTime(state.Elapsed);
            TimeText.Text = FormatTime(state.Duration);
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
            StatusText.Text = $"Playing: {System.IO.Path.GetFileName(path)}";
        }
    }

    private void OnTestPatternClick(object? sender, RoutedEventArgs e)
    {
        var testFile = FindTestFile();
        if (testFile != null)
        {
            VideoSurface.MediaPath = testFile;
            StatusText.Text = $"Test: {System.IO.Path.GetFileName(testFile)}";
        }
    }

    private void OnTopMostChanged(object? sender, RoutedEventArgs e)
    {
        Topmost = TopMostCheck.IsChecked == true;
    }

    private void OnOverlayPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        StatusText.Text = "Overlay clicked! Composition works.";
    }

    private static string? FindTestFile()
    {
        string testFile = Interop.RepositoryPaths.TestMediaPath;
        return System.IO.File.Exists(testFile) ? testFile : null;
    }
}

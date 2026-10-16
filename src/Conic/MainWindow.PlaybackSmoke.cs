#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Contracts;
using Conic.Interop;
using Conic.Interop.MacOS;

namespace Conic;

public partial class MainWindow
{
    // Interactive review uses the actual VLC 3 engine and the configured local
    // fixture. It deliberately stays open for pointer/keyboard inspection.
    internal void EnablePlaybackReview()
    {
        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 720;
        Opened += (_, _) => OpenTestMedia();
    }

    // Exercises the actual styled controls and routed input without opening media.
    internal void EnablePlaybackControlsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 720;
        VideoSurface.EngineStateChanged -= OnEngineStateChanged;
        _presentation.IsHomeOpen = false;
        Opened += async (_, _) =>
        {
            try
            {
                async Task Within(Func<bool> condition, double seconds)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                }
                await Task.Delay(500);
                MediaEngineState playing = MediaEngineState.Initial with
                {
                    Generation = 1, Status = PlaybackStatus.Playing, CanSeek = true,
                    Position = .25, Elapsed = TimeSpan.FromSeconds(30),
                    Duration = TimeSpan.FromSeconds(120), Volume = .75,
                    Title = "Playback controls review"
                };
                _presentation.ApplyEngineState(playing);
                Require(SpeedSelector.SelectedItem is ComboBoxItem { Tag: "1" }, "speed displays effective normal rate");
                _presentation.ApplyEngineState(playing with { PlaybackRate = 1.5 });
                Require(SpeedSelector.SelectedItem is ComboBoxItem { Tag: "1.5" }, "speed follows native readback");
                _presentation.ApplyEngineState(playing);
                _chromeVisibility.SetPlaybackState(true, autoHide: true);
                _autoHideTimer.Interval = TimeSpan.FromMilliseconds(150);
                await Task.Delay(50);

                Track timeline = RequireTrack(PlayerTimeline);
                Track volume = RequireTrack(PlayerVolume);
                Require(Math.Abs(timeline.Value - .25) < .001, "elapsed track follows playback position");
                Require(Math.Abs(volume.Value - 75) < .001, "volume track follows engine volume");
                Require(Math.Abs(GetClientRect(ProductMark).Center.Y - GetClientRect(TopChromeTitle).Center.Y) < 1, "title aligns with logo");

                int commits = 0;
                PlayerTimeline.SeekCommitted += (_, _) => commits++;
                ClickTrack(timeline, .75);
                await Task.Delay(50);
                Require(PlayerTimeline.Value > .65 && PlayerTimeline.Value < .85 && commits == 1, "track click seeks and commits once");
                Require(!_isSeeking, "seek drag is released");
                ClickTrack(volume, .8);
                Require(PlayerVolume.Value > 140 && _presentation.Volume > 140, "volume click updates level");
                Require(!_isVolumeDragging, "volume drag is released");
                PlayerTimeline.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Home });
                Require(PlayerTimeline.Value == 0, "Home seeks to start");
                PlayerTimeline.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.End });
                Require(PlayerTimeline.Value == 1, "End seeks to end");
                await Task.Delay(50);

                OpenOptionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                Require(OpenMenu.IsVisible && GetClientRect(OpenMenu).Right <= ClientSize.Width &&
                    GetClientRect(OpenMenu).Left >= 0 &&
                    GetClientRect(OpenMenu).Left <= GetClientRect(OpenOptionsButton).Left &&
                    GetClientRect(OpenMenu).Top >= GetClientRect(OpenOptionsButton).Bottom,
                    "More menu is anchored below the title bar's More button");
                Press(VideoArea, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
                Require(!OpenMenu.IsVisible, "outside pointer press dismisses menu");

                // Windowed, the bars are docked around the video and never hide
                // (phase 4a); inactivity hiding is checked in fullscreen below.
                MuteButton.Focus(NavigationMethod.Pointer);
                _autoHideTimer.Start();
                await Task.Delay(400);
                Require(PlayerDock.IsVisible && TopChrome.IsVisible, "docked bars stay visible while playing windowed");
                WakeChrome();
                Require(PlayerDock.IsVisible, "interaction keeps controls visible");
                PlayerTimeline.Focus(NavigationMethod.Tab);
                PlayerTimeline.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Right });
                await Task.Delay(400);
                Require(PlayerDock.IsVisible, "keyboard seek focus keeps controls visible");
                _presentation.ApplyEngineState(playing with { Status = PlaybackStatus.Paused, Position = .4 });
                await Task.Delay(200);
                Require(PlayerDock.IsVisible, "paused controls stay visible");

                SavePlaybackCapture("playback-controls-wide.png");
                // Design bar: 54 tall at the bottom, video between the bars,
                // the timeline filling the room between transport and volume.
                ValidateLayout(ClientSize.Width, false);
                foreach (double level in new[] { 0d, 100d, 200d })
                {
                    _presentation.ApplyEngineState(playing with { Status = PlaybackStatus.Paused, Volume = level / 100 });
                    await Task.Delay(80);
                    Require(IsContained(RequireTrack(PlayerVolume).Thumb!), "volume thumb stays contained at all endpoints");
                    SavePlaybackCapture($"playback-volume-{level:0}.png");
                }
                _presentation.ApplyEngineState(playing with { Status = PlaybackStatus.Paused, Position = .4 });
                OpenOptionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(80);
                SavePlaybackCapture("playback-more.png");
                Press(VideoArea, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
                _presentation.IsQueueOpen = true;
                await Task.Delay(80);
                SavePlaybackCapture("playback-queue.png");
                _presentation.DrawerTab = "Audio";
                await Task.Delay(80);
                SavePlaybackCapture("playback-audio.png");
                _presentation.IsQueueOpen = false;
                // Fullscreen: the video spans the window, the bars overlay it
                // and hide after inactivity, even with mouse focus on a bar.
                _presentation.ApplyEngineState(playing with { Position = .4 });
                Size windowed = ClientSize;
                ToggleFullscreen();
                await Task.Delay(300);
                WakeChrome();
                Rect video = GetClientRect(VideoSurface);
                Require(WindowState == WindowState.FullScreen && video.Top < .5 && Math.Abs(video.Height - ClientSize.Height) < .5 &&
                    Grid.GetRowSpan(SubtitleLayer) == 3 && PlayerDock.IsVisible && TopChrome.IsVisible &&
                    Math.Abs(GetClientRect(PlayerDock).Bottom - ClientSize.Height) < .5 && FullscreenButton.IsEffectivelyVisible,
                    "fullscreen video spans the window under the bars, with an exit button");
                SavePlaybackCapture("playback-fullscreen.png");
                _keyboardNavigation = false;
                MuteButton.Focus(NavigationMethod.Pointer);
                // The real pointer over a bar rightly holds it up: on macOS the
                // smoke parks it mid-window, where a user's rests on the video.
                if (OperatingSystem.IsMacOS()) ParkPointerMidWindow();
                else if (OperatingSystem.IsLinux() && TryGetPlatformHandle()?.HandleDescriptor == "XID")
                {
                    PixelPoint middle = this.PointToScreen(new Point(Bounds.Width / 2, Bounds.Height / 2));
                    Conic.Interop.Linux.X11ScreenSampler.WarpPointer(middle.X, middle.Y);
                }
                // macOS animates into fullscreen and its events wake the bars
                // meanwhile: they hide once it has settled.
                await Within(() => !PlayerDock.IsVisible && !TopChrome.IsVisible, 3);
                Require(!PlayerDock.IsVisible && !TopChrome.IsVisible, "fullscreen bars hide after inactivity; mouse focus does not block it" +
                    (TopChrome.IsPointerOver || PlayerDock.IsPointerOver ? " (the real pointer rests on a bar: park the cursor mid-screen)" : ""));
                SavePlaybackCapture("playback-hidden.png");
                WakeChrome();
                Require(PlayerDock.IsVisible && TopChrome.IsVisible, "interaction reveals the fullscreen bars");
                _presentation.ApplyEngineState(playing with { Status = PlaybackStatus.Paused, Position = .4 });
                ToggleFullscreen();
                bool Docked() => WindowState == WindowState.Normal && Grid.GetRow(VideoSurface) == 1 && PlayerDock.IsVisible && TopChrome.IsVisible;
                // macOS reports Normal before its exit animation ends by putting
                // the earlier frame back: the resizes below wait for that frame.
                await Within(() => Docked() && Math.Abs(ClientSize.Width - windowed.Width) < 1 && Math.Abs(ClientSize.Height - windowed.Height) < 1, 3);
                Require(Docked(), "leaving fullscreen docks the bars again");
                WakeChrome();
                Width = 900;
                await Task.Delay(200);
                ValidateLayout(900, false);
                Width = 899;
                await Task.Delay(150);
                ValidateLayout(899, true);
                Width = 480;
                Height = 360;
                await Task.Delay(200);
                ValidateLayout(480, true);
                Require(MuteButton.IsEffectivelyVisible && IsContained(MuteButton) && !PlayerVolume.IsVisible,
                    "narrow bar keeps mute and drops the volume slider");
                SavePlaybackCapture("playback-controls-compact.png");
                _presentation.ApplyEngineState(playing with { Status = PlaybackStatus.Paused,
                    Title = "A very long media title that must remain inside the header beside queue and More controls" });
                await Task.Delay(80);
                ValidateTopChrome(480);
                SavePlaybackCapture("playback-long-title.png");
                Width = 1280;
                Height = 720;
                foreach (PlaybackStatus status in new[] { PlaybackStatus.Opening, PlaybackStatus.Faulted, PlaybackStatus.Closed })
                {
                    _presentation.ApplyEngineState(MediaEngineState.Initial with { Status = status, Title = "Playback review" });
                    WakeChrome();
                    await Task.Delay(80);
                    SavePlaybackCapture($"playback-{status.ToString().ToLowerInvariant()}.png");
                }
                Console.WriteLine("[playback-controls-smoke] PASS sliders, seeking, volume, title alignment, menu placement/dismissal, docked bars, fullscreen inactivity, keyboard focus, compact layout");
                desktop.Shutdown(0);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[playback-controls-smoke] FAIL {exception}");
                desktop.Shutdown(1);
            }
        };
    }

    // Moves the real pointer to the middle of the window and tells AppKit, so
    // the bars do not see it over them.
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void ParkPointerMidWindow()
    {
        var handle = TryGetPlatformHandle() ?? throw new InvalidOperationException("No native window.");
        Point middle = new(Bounds.Width / 2, Bounds.Height / 2);
        PixelPoint screen = this.PointToScreen(middle);
        MacEventInjection.Warp(screen.X / RenderScaling, screen.Y / RenderScaling);
        MacEventInjection.Move(handle.Handle, handle.HandleDescriptor == "NSView", middle.X, middle.Y);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Track RequireTrack(Slider slider)
    {
        Track? track = slider.GetVisualDescendants().OfType<Track>().SingleOrDefault();
        Require(track is { Bounds.Width: > 0, Thumb.Bounds.Width: > 0 }, $"{slider.Name} has a visible track and thumb");
        Require(track!.Thumb!.GetVisualDescendants().OfType<Border>().Any(b => b.Background is not null && b.Bounds.Width > 0), $"{slider.Name} thumb has a rendered surface");
        // The fill sits inside a transparent hit area; it must be painted and
        // at least 4 tall (the design's played part is inset in the track).
        Require(track.DecreaseButton!.GetVisualDescendants().OfType<Border>().Any(b => IsPainted(b.Background) && b.Bounds.Height >= 4), $"{slider.Name} has a rendered progress fill");
        return track!;
    }

    private static bool IsPainted(IBrush? brush) => brush is not null and not ISolidColorBrush { Color.A: 0 };

    private void ClickTrack(Track track, double fraction)
    {
        Control target = fraction > (track.Value - track.Minimum) / (track.Maximum - track.Minimum)
            ? track.IncreaseButton! : track.DecreaseButton!;
        Point point = track.TranslatePoint(new Point(track.Bounds.Width * fraction, track.Bounds.Height / 2), this)!.Value;
        Pointer pointer = Press(target, point);
        target.RaiseEvent(new PointerReleasedEventArgs(target, pointer, this, point, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        pointer.Capture(null);
    }

    private Pointer Press(Control target, Point point)
    {
        Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        target.RaiseEvent(new PointerPressedEventArgs(target, pointer, this, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        return pointer;
    }

    private void SavePlaybackCapture(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "playback-review");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
#endif // SMOKE

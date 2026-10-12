#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Player.Contracts;
using VLCAvalonia.Interop;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnablePictureInPictureReview()
    {
        EnablePlaybackReview();
        EventHandler<MediaEngineState>? enterWhenPlaying = null;
        enterWhenPlaying = (_, state) =>
        {
            if (state.Status != PlaybackStatus.Playing) return;
            VideoSurface.EngineStateChanged -= enterWhenPlaying;
            TogglePictureInPicture();
        };
        VideoSurface.EngineStateChanged += enterWhenPlaying;
    }

    internal void EnablePictureInPictureSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 720;
        VideoSurface.ProbeSharedTextureForSmoke = true;
        int presented = 0;
        VideoSurface.GPUFramePresentedForSmoke += (_, _, _) => presented++;
        VideoSurface.StatusChanged += status => Console.WriteLine($"[pip-smoke] status: {status}");
        Opened += async (_, _) =>
        {
            try
            {
                OpenTestMedia();
                await Until(() => VideoSurface.Player?.State.Status == PlaybackStatus.Playing && presented > 2);
                IMediaEngine engine = VideoSurface.Player!;
                long generation = engine.State.Generation;
                PixelPoint position = Position;
                // The OS may clamp the requested 1280x720 to a smaller screen.
                Size normalSize = ClientSize;
                _presentation.IsAlwaysOnTop = false;
                await Task.Delay(100);
                int before = presented;
                _presentation.TogglePictureInPictureCommand.Execute(null);
                await Until(() => presented > before + 3 && Math.Abs(ClientSize.Width - 480) < 2);
                Require(_presentation.IsPictureInPicture && Topmost && WindowDecorations == WindowDecorations.BorderOnly, "PiP is compact and always on top");
                Require(!HomeScreen.IsVisible && !PlayerDock.IsVisible && !TopChrome.IsVisible, "main layout is hidden");
                CapturePictureInPicture("pip-playing");
                Width = 320;
                Height = 180;
                await Task.Delay(250);
                Require(IsContained(PictureInPictureHeader) && IsContained(PictureInPicturePlay) && IsContained(PictureInPictureTimeline), "minimum-size controls fit");
                CapturePictureInPicture("pip-minimum");
                PictureInPictureChrome.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                await Until(() => engine.State.Status == PlaybackStatus.Paused);
                _presentation.Registry.Execute(Player.Application.PlayerCommandId.Seek, .25);
                await Until(() => Math.Abs(engine.State.Position - .25) < .04);
                KeyEventArgs escape = new() { Key = Key.Escape };
                OnPlayerKeyDown(this, escape);
                await Task.Delay(250);
                Require(escape.Handled && !_presentation.IsPictureInPicture && !Topmost, "Escape returns and restores topmost=false");
                // Window frame changes complete asynchronously on macOS.
                bool BoundsRestored() => Math.Abs(ClientSize.Width - normalSize.Width) < 2 && Math.Abs(ClientSize.Height - normalSize.Height) < 2 && Position == position;
                for (DateTime settle = DateTime.UtcNow.AddSeconds(3); !BoundsRestored() && DateTime.UtcNow < settle;) await Task.Delay(50);
                Require(BoundsRestored(), $"normal bounds restored (expected {normalSize.Width}x{normalSize.Height} at {position}, got {ClientSize.Width}x{ClientSize.Height} at {Position})");
                Require(engine.State.Status == PlaybackStatus.Paused && Math.Abs(engine.State.Position - .25) < .04, "paused seek survives return");
                _presentation.TogglePauseCommand.Execute(null);
                await Until(() => engine.State.Status == PlaybackStatus.Playing);

                _presentation.IsAlwaysOnTop = true;
                TogglePictureInPicture();
                await Task.Delay(150);
                TogglePictureInPicture();
                Require(Topmost && _presentation.IsAlwaysOnTop, "topmost=true preference restored");
                _presentation.IsAlwaysOnTop = false;
                Require(!Topmost, "topmost binding survives PiP");

                WindowState = WindowState.Maximized;
                await Task.Delay(150);
                TogglePictureInPicture();
                await Task.Delay(150);
                TogglePictureInPicture();
                Require(WindowState == WindowState.Maximized, "maximized state restored");
                WindowState = WindowState.Normal;
                await Task.Delay(150);

                ToggleFullscreen();
                await Task.Delay(200);
                TogglePictureInPicture();
                await Task.Delay(200);
                Require(WindowState == WindowState.Normal && _presentation.IsPictureInPicture, "fullscreen enters PiP");
                await RequireCompact("PiP entered from fullscreen is compact");
                TogglePictureInPicture();
                await Task.Delay(200);
                Require(WindowState == WindowState.FullScreen && _presentation.IsFullscreen, "fullscreen state restored");
                ToggleFullscreen();
                await Task.Delay(200);
                _presentation.ShowHomeCommand.Execute(null);
                TogglePictureInPicture();
                await Task.Delay(150);
                TogglePictureInPicture();
                Require(_presentation.IsHomeOpen && _presentation.ShowMiniPlayer, "Home return route restored");

                TogglePictureInPicture();
                _presentation.ShowHomeCommand.Execute(null);
                Require(!_presentation.IsPictureInPicture && _presentation.IsHomeOpen && Width >= 480, "Home action restores main layout");
                TogglePictureInPicture();
                _presentation.OpenSettingsCommand.Execute(null);
                Require(!_presentation.IsPictureInPicture && SettingsPanel.IsVisible, "Settings restores main layout");
                CloseSettings();
                TogglePictureInPicture();
                _presentation.OpenUrlCommand.Execute(null);
                Require(!_presentation.IsPictureInPicture && UrlPanel.IsVisible, "Open URL restores main layout");
                CloseUrlPanel();
                TogglePictureInPicture();
                _presentation.OpenSource(Path.Combine(RepositoryPaths.ArtifactsDirectory, "missing-pip-test.mp4"), Player.Application.MediaSourceKind.File);
                Require(!_presentation.IsPictureInPicture && _presentation.HasOpenError, "invalid media opens error in full layout");
                Require(ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation, "all window transitions preserve the same native session");
                before = presented;
                TogglePictureInPicture();
                await Until(() => presented > before + 3);
                await RequireCompact("PiP after rapid transitions is compact");
                Console.WriteLine($"[pip-smoke] PASS GPU frames={presented}, same engine/generation, pause/seek, resize, Escape, fullscreen, topmost binding, Home, Settings, URL, open error; closing PiP");
                // Exercise the actual close path and normal desktop lifetime shutdown.
                OnPictureInPictureClose(null, new Avalonia.Interactivity.RoutedEventArgs());
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[pip-smoke] FAIL {error}");
                Console.Error.WriteLine($"[pip-smoke] state={VideoSurface.Player?.State.Status} position={VideoSurface.Player?.State.Position:0.000} " +
                    $"gpu-frames={presented} pip={_presentation.IsPictureInPicture} window={WindowState} {ClientSize.Width}x{ClientSize.Height} at {Position}");
                desktop.Shutdown(1);
            }
        };

        // Frame changes settle asynchronously on macOS; CI once saw PiP left at
        // 480x2645370 after leaving fullscreen.
        async Task RequireCompact(string message)
        {
            bool Compact() => Math.Abs(ClientSize.Width - 480) < 2 && Math.Abs(ClientSize.Height - 270) < 2;
            for (DateTime settle = DateTime.UtcNow.AddSeconds(3); !Compact() && DateTime.UtcNow < settle;) await Task.Delay(50);
            Require(Compact(), $"{message} (got {ClientSize.Width}x{ClientSize.Height} at {Position})");
        }

        static async Task Until(Func<bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (!predicate()) throw new TimeoutException("PiP state/frame transition did not complete");
        }
    }

    private void CapturePictureInPicture(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "pip-review");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
#endif // SMOKE

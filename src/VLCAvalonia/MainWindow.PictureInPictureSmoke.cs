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
        Opened += async (_, _) =>
        {
            try
            {
                OpenTestMedia();
                await Until(() => VideoSurface.Player?.State.Status == PlaybackStatus.Playing && presented > 2);
                IMediaEngine engine = VideoSurface.Player!;
                long generation = engine.State.Generation;
                PixelPoint position = Position;
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
                Require(Math.Abs(ClientSize.Width - 1280) < 2 && Math.Abs(ClientSize.Height - 720) < 2 && Position == position, "normal bounds restored");
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
                Console.WriteLine($"[pip-smoke] PASS GPU frames={presented}, same engine/generation, pause/seek, resize, Escape, fullscreen, topmost binding, Home, Settings, URL, open error; closing PiP");
                // Exercise the actual close path and normal desktop lifetime shutdown.
                OnPictureInPictureClose(null, new Avalonia.Interactivity.RoutedEventArgs());
            }
            catch (Exception error) { Console.Error.WriteLine($"[pip-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };

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

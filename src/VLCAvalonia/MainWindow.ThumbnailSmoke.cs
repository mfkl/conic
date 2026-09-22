#if SMOKE
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Player.Contracts;
using Player.Application;
using VLCAvalonia.Controls;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableThumbnailSmoke(IClassicDesktopStyleApplicationLifetime desktop, bool review)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                string media = Environment.GetEnvironmentVariable("VLC_AVALONIA_TEST_MEDIA") ?? throw new InvalidOperationException("Provide a local thumbnail fixture");
                _presentation.OpenSource(media, MediaSourceKind.File);
                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                while (VideoSurface.Player?.State.Status != PlaybackStatus.Playing && DateTime.UtcNow < deadline) await Task.Delay(50);
                var engine = VideoSurface.Player ?? throw new InvalidOperationException("No media engine");
                Require(engine.State.Status == PlaybackStatus.Playing, "fixture starts playing");
                long generation = engine.State.Generation;
                double position = engine.State.Position;
                while (_presentation.HomeItems.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
                _presentation.ShowHomeCommand.Execute(null);
                Require(_presentation.HomeItems.Count > 0, "opening media records a Home card");
                bool HasArtwork() => HomeScreen.GetVisualDescendants().OfType<MediaThumbnail>().Any(t => t.IsEffectivelyVisible && t.HasThumbnail);
                while (!HasArtwork() && DateTime.UtcNow < deadline) await Task.Delay(100);
                Require(HasArtwork(), "Home renders a real local video thumbnail");
                CaptureHome("home-thumbnails-wide");
                _presentation.HomeSearch = "no-such-media-abcdef";
                await Task.Delay(100);
                Require(!HasArtwork(), "removed cards detach their artwork");
                _presentation.HomeSearch = "";
                deadline = DateTime.UtcNow.AddSeconds(5);
                while (!HasArtwork() && DateTime.UtcNow < deadline) await Task.Delay(50);
                Require(HasArtwork(), "returning cards restore cached artwork");
                Width = 480; Height = 360;
                await Task.Delay(200);
                Require(IsContained(HomeScreen), "Home with artwork fits compact window");
                CaptureHome("home-thumbnails-compact");
                // A disk-cache hit can finish before VLC's next position notification.
                deadline = DateTime.UtcNow.AddSeconds(3);
                while (engine.State.Position <= position && DateTime.UtcNow < deadline) await Task.Delay(50);
                Require(ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation && engine.State.Position > position, "thumbnail loading preserves advancing playback");
                Console.WriteLine("[thumbnail-smoke] PASS real artwork, detach/reload, compact layout, unchanged advancing playback");
                if (review) { Width = 1280; Height = 720; }
                else desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[thumbnail-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

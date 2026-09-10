using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Contracts;
using Player.Application;
using VLCAvalonia.Interop;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableMediaInformationSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                OpenTestMedia();
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while (VideoSurface.Player?.State.Status != PlaybackStatus.Playing && DateTime.UtcNow < deadline) await Task.Delay(50);
                var engine = VideoSurface.Player ?? throw new InvalidOperationException("No media engine");
                long generation = engine.State.Generation;
                _presentation.MediaInformationCommand.Execute(null);
                while (!_presentation.MediaInformation.HasStreams && DateTime.UtcNow < deadline)
                { await Task.Delay(150); _presentation.MediaInformationCommand.Execute(null); }
                MediaInformationState snapshot = _presentation.MediaInformation;
                Require(snapshot.HasStreams && snapshot.Streams.Any(s => s.Fields.ContainsKey("Video resolution")), "VLC reports video codec/resolution");
                Require(snapshot.Streams.All(s => s.Fields.ContainsKey("Codec")), "all streams contain actual codec values");
                Console.WriteLine("[media-information-smoke] " + MediaDiagnostics.Build(snapshot));
                string directory = Path.Combine(RepositoryPaths.Root, "artifacts", "media-information-review");
                Directory.CreateDirectory(directory);
                foreach (int width in new[] { 1280, 480 })
                {
                    Width = width; Height = width == 480 ? 360 : 720;
                    await Task.Delay(200);
                    Require(IsContained(QueueDrawer), "information drawer fits window");
                    var information = QueueDrawer.GetVisualDescendants().OfType<Views.MediaInformationView>().Single();
                    Require(information.IsEffectivelyVisible && information.Bounds.Height > 0, "information content is visible");
                    Require(information.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Video resolution"), "codec fields render in the drawer");
                    using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
                    bitmap.Render(VideoArea);
                    bitmap.Save(Path.Combine(directory, $"media-info-{width}.png"), PngBitmapEncoderOptions.Default);
                }
                Require(ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation, "information reads do not reopen media");
                Console.WriteLine($"[media-information-smoke] PASS {snapshot.Streams.Count} codec streams, resolution, immutable snapshot, compact/wide layout and unchanged session");
                desktop.Shutdown(0);
            }
            catch (Exception e) { Console.Error.WriteLine($"[media-information-smoke] FAIL {e}"); desktop.Shutdown(1); }
        };
    }
}

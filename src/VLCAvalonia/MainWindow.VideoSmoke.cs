#if SMOKE
using VLCAvalonia.Interop.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Interop;
using VLCLR;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableVideoSmoke(IClassicDesktopStyleApplicationLifetime desktop, bool cpu)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        VideoSurface.ForceUnavailableAdapterForSmoke = cpu;
        Opened += async (_, _) =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Video native smoke requires Windows.");
                async Task WaitFor(Func<bool> condition, string message)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                string folder = Path.Combine(RepositoryPaths.ArtifactsDirectory, "video-review", cpu ? "cpu" : "gpu");
                Directory.CreateDirectory(folder);
                async Task Capture(string name)
                {
                    await Task.Delay(200);
                    using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(ClientSize, 1), new Vector(96, 96));
                    bitmap.Render(this); bitmap.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
                    if (desktop.Args?.Contains("--video-capture") == true)
                    {
                        Console.WriteLine($"[video-capture] {name}");
                        await Task.Delay(1500);
                    }
                }
                _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                await WaitFor(() => _presentation.IsPlaying && _presentation.VideoAdjustments.Available && VideoSurface.GeometryForSmoke.Active != 0, "active video exposes controls and native geometry");
                await WaitFor(() => cpu ? VideoSurface.HasCpuFrameForSmoke : VideoSurface.HasGpuFrameForSmoke, "requested rendering path presents a frame");
                var player = (VlcCorePlayer)VideoSurface.Player!;
                var original = player.ReadVideoForSmoke();
                Require(player.SetVideoForSmoke(original, "crop", "invalid").Code == VLCPlayerCapabilityResultCode.InvalidArgument, "invalid ratio rejected");
                Require(player.SetVideoForSmoke(original with { OutputToken = 0 }, "crop", "").Code == VLCPlayerCapabilityResultCode.StaleSnapshot, "stale output rejected");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "pause");
                _presentation.DrawerTab = "Video"; _presentation.IsQueueOpen = true;
                await Task.Delay(150);
                await Capture("default");
                var video = _presentation.VideoAdjustments;
                video.Aspect = video.Aspects.Single(x => x.Value == "4:3");
                await WaitFor(() => video.Aspect?.Value == "4:3" && Math.Abs(VideoSurface.CurrentVideoLayout.Destination.Width / VideoSurface.CurrentVideoLayout.Destination.Height - 4d / 3) < .01, "aspect readback and display geometry agree");
                await Capture("aspect-4-3");
                Require(this.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && c.SelectedItem is VideoChoice { Value: "4:3" }), "aspect dropdown reflects native readback");
                video.Crop = video.Crops.Single(x => x.Value == "1:1");
                await WaitFor(() => video.Crop?.Value == "1:1" && Math.Abs(VideoSurface.CurrentVideoLayout.Destination.Width / VideoSurface.CurrentVideoLayout.Destination.Height - 1) < .01, "center crop readback and display geometry agree");
                await Capture("crop-square");
                Require(this.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && c.SelectedItem is VideoChoice { Value: "1:1" }), "crop dropdown reflects native readback");
                video.Zoom = video.Zooms.Single(x => x.Value == "0.5");
                await WaitFor(() => video.Zoom?.Value == "0.5" && VideoSurface.GeometryForSmoke.AutoScale == 0 && Math.Abs(VideoSurface.GeometryForSmoke.Zoom - .5) < .01, "fixed zoom reaches native display configuration");
                double height = VideoSurface.CurrentVideoLayout.Destination.Height;
                Width = 800; Height = 500;
                await Task.Delay(200);
                Require(Math.Abs(VideoSurface.CurrentVideoLayout.Destination.Height - height) < .01, "fixed zoom survives resize while paused");
                await Capture("half-size");
                _presentation.TogglePictureInPictureCommand.Execute(null);
                await Task.Delay(200);
                Require(video.Crop?.Value == "1:1" && video.Zoom?.Value == "0.5", "PiP retains video controls");
                _presentation.TogglePictureInPictureCommand.Execute(null);
                video.Reset();
                await WaitFor(() => video.Aspect?.Value == "" && video.Crop?.Value == "" && video.Zoom?.Value == "fit" && VideoSurface.GeometryForSmoke.Width == 1 && VideoSurface.GeometryForSmoke.Height == 1 && VideoSurface.GeometryForSmoke.AutoScale != 0, "reset restores original geometry and Fit");
                Width = 640; Height = 360; FontSize = 20;
                _presentation.DrawerTab = "Video"; _presentation.IsQueueOpen = true;
                await Capture("compact-large-text");
                _presentation.StopCommand.Execute(null);
                await WaitFor(() => !video.Available, "Stop clears video controls");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => video.Available && _presentation.IsPlaying, "Play reacquires output");
                Require(player.SetVideoForSmoke(original, "crop", "1:1").Code == VLCPlayerCapabilityResultCode.StaleSnapshot, "old input cannot change replay");
                Require(video.Aspect?.Value == "" && video.Crop?.Value == "" && video.Zoom?.Value == "fit", "new input uses defaults");
                string audioPath = Path.Combine(folder, "audio-only.wav");
                using (var writer = new BinaryWriter(File.Create(audioPath)))
                {
                    const int bytes = 44100 * 2 * 10;
                    writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8);
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
                    writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                    writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
                }
                _presentation.OpenSource(audioPath, MediaSourceKind.File);
                await WaitFor(() => player.State.Status == PlaybackStatus.Playing && player.State.Duration > TimeSpan.Zero && player.State.Source?.Contains("audio-only", StringComparison.Ordinal) == true, "audio-only input plays");
                await WaitFor(() => !video.Available && VideoSurface.GeometryForSmoke.Active == 0, "audio-only input clears controls and stale video image");
                Require(player.ReadVideoForSmoke() is null, "audio-only input has no video output");
                Console.WriteLine($"[video-smoke] PASS {(cpu ? "CPU fallback" : "GPU")} native choices, invalid/stale requests, paused aspect/crop/zoom geometry, resize, PiP, reset, Stop/Play and audio-only");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[video-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

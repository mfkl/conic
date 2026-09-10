using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Interop;

namespace Conic;

public partial class MainWindow
{
    internal void EnableStopSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                async Task WaitFor(Func<bool> condition, string message)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                await WaitFor(() => _presentation.IsPlaying, "fixture plays");
                foreach (int width in new[] { 1280, 900, 899, 480 })
                {
                    Width = width; Height = width == 480 ? 360 : 720;
                    WakeChrome(); await Task.Delay(100);
                    Require(StopButton.IsEffectivelyVisible && StopButton.IsEnabled && IsContained(StopButton), "Playback Stop is visible and contained");
                }
                _presentation.ShowHomeCommand.Execute(null);
                await Task.Delay(100);
                var homeStop = HomeScreen.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "HomeStopButton");
                Require(homeStop.IsEffectivelyVisible && homeStop.IsEnabled && IsContained(homeStop), "Home Stop is visible and contained");
                homeStop.Command!.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Stopped, "Stop reaches the engine");
                await Task.Delay(100);
                Require(!_presentation.ShowMiniPlayer && !homeStop.IsEffectivelyVisible, "Stop clears the Home bar");
                Require(_presentation.HomeItems.Count > 0, "Stop preserves recent history");
                _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                await WaitFor(() => _presentation.IsPlaying && _presentation.CanSeek, "media can reopen after Stop");
                VideoSurface.Player!.Seek(.92);
                _presentation.ShowHomeCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Stopped, "media ends naturally");
                await Task.Delay(150);
                Require(!_presentation.ShowMiniPlayer && !homeStop.IsEffectivelyVisible, "end of media clears the Home bar");
                CaptureHome("home-after-playback");
                Console.WriteLine("[stop-smoke] PASS visible Stop wide/compact, native Stop, reopen, natural end, cleared Home bar and retained history");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[stop-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}

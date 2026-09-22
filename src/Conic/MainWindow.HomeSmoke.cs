#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Contracts;
using Conic.Interop;

namespace Conic;

public partial class MainWindow
{
    internal void EnableHomeSmoke(IClassicDesktopStyleApplicationLifetime desktop, bool native)
    {
        WindowState = WindowState.Normal;
        Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                await Task.Delay(350);
                Require(_presentation.IsHomeOpen && HomeScreen.IsVisible, "startup opens Home");
                CaptureHome("home-wide");
                if (_presentation.HomeItems.Count > 0)
                {
                    _presentation.HomeSearch = "no-such-media-abcdef";
                    await Task.Delay(100);
                    Require(_presentation.ShowHomeEmpty && !_presentation.HomeHasItems, "search has an honest empty result");
                    CaptureHome("home-search-empty");
                    _presentation.HomeSearch = string.Empty;
                }
                _presentation.HomeListLayout = true;
                await Task.Delay(100);
                CaptureHome("home-list");
                _presentation.HomeListLayout = false;
                Width = 480; Height = 360;
                await Task.Delay(250);
                Require(IsContained(HomeScreen), "compact Home stays within client bounds");
                foreach (Button button in HomeScreen.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    Rect rect = GetClientRect(button);
                    Require(rect.Left >= 0 && rect.Right <= ClientSize.Width + 1, $"{button.Content} has no horizontal overflow");
                }
                CaptureHome("home-compact");
                OnOpenOptions(null, new Avalonia.Interactivity.RoutedEventArgs());
                await Task.Delay(100);
                Require(OpenMenu.IsVisible && IsContained(OpenMenu), "Home More fits compact window");
                CloseOpenMenu();
                Require(HomeScreen.IsKeyboardFocusWithin, "closing More restores Home focus");
                _presentation.OpenLibraryCommand.Execute(null);
                await Task.Delay(100);
                Require(LibraryPanel.IsVisible, "Home opens library management");
                CloseLibrary();
                await Task.Delay(100);
                Require(HomeScreen.IsKeyboardFocusWithin, "closing library restores Home focus");
                Width = 1280; Height = 720;
                if (native)
                {
                    OpenTestMedia();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                    while (VideoSurface.Player?.State.Status != PlaybackStatus.Playing && DateTime.UtcNow < deadline) await Task.Delay(100);
                    IMediaEngine engine = VideoSurface.Player ?? throw new InvalidOperationException("No native engine");
                    Require(engine.State.Status == PlaybackStatus.Playing, "native fixture is playing");
                    long generation = engine.State.Generation;
                    double before = engine.State.Position;
                    _presentation.ShowHomeCommand.Execute(null);
                    await Task.Delay(1000);
                    Require(_presentation.ShowMiniPlayer && HomeScreen.IsVisible, "Home shows now playing");
                    Require(ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation && engine.State.Position > before,
                        "Home preserves the engine and advancing playback");
                    CaptureHome("home-now-playing");
                    _presentation.ShowPlayerCommand.Execute(null);
                    await Task.Delay(150);
                    Require(!_presentation.IsHomeOpen && ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation,
                        "return to Playback does not reopen media");
                    before = engine.State.Position;
                    OpenSettings();
                    await Task.Delay(750);
                    Require(SettingsPanel.IsVisible && ReferenceEquals(engine, VideoSurface.Player) && engine.State.Generation == generation && engine.State.Position > before,
                        "Settings preserves advancing playback");
                    CloseSettings();
                }
                Console.WriteLine($"[home-smoke] PASS startup, search, grid/list, compact containment; native-session={native}");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[home-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
    private void CaptureHome(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "home-review");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
#endif // SMOKE

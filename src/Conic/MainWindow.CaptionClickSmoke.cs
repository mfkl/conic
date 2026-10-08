#if SMOKE
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Interop.MacOS;

namespace Conic;

public partial class MainWindow
{
    /// <summary>
    /// Clicks the caption row's buttons through AppKit (macOS), where the
    /// system title bar overlaps them: the player's back arrow while playing,
    /// Home's "Return to player" and the player's options button.
    /// </summary>
    /// <remarks>
    /// Posted events skip the window server, which drags the window from
    /// title bar regions before the app sees a real click, so this covers
    /// AppKit's routing but not that; check real clicks by hand.
    /// </remarks>
    internal void EnableCaptionClickSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> failures = [];
            try
            {
                if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The caption click smoke posts AppKit events.");
                async Task<bool> WaitFor(Func<bool> condition, int seconds = 5)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    return condition();
                }
                var handle = TryGetPlatformHandle() ?? throw new InvalidOperationException("No native window.");
                bool isView = handle.HandleDescriptor == "NSView";
                Point CenterOf(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this)
                    ?? throw new InvalidOperationException($"{control.Name} is not in the window.");
                // Like a user: the pointer travels up from the video to the
                // button and rests there past the auto-hide delay.
                async Task Approach(Point target)
                {
                    Point start = new(Bounds.Width / 2, Bounds.Height / 2);
                    for (int step = 1; step <= 12; step++)
                    {
                        double t = step / 12.0;
                        MacEventInjection.Move(handle.Handle, isView, start.X + (target.X - start.X) * t, start.Y + (target.Y - start.Y) * t);
                        await Task.Delay(40);
                    }
                    for (int rest = 0; rest < 8; rest++)
                    {
                        MacEventInjection.Move(handle.Handle, isView, target.X, target.Y + (rest % 2));
                        await Task.Delay(500);
                    }
                }
                async Task Click(Control control, string name, Func<bool> expected)
                {
                    Point center = CenterOf(control);
                    await Approach(center);
                    if (!control.IsEffectivelyVisible) Console.WriteLine($"[caption-click-smoke] note: {name} hidden after hovering it");
                    // A real click holds the button for about a tenth of a second.
                    MacEventInjection.Press(handle.Handle, isView, center.X, center.Y);
                    await Task.Delay(120);
                    MacEventInjection.Release(handle.Handle, isView, center.X, center.Y);
                    bool passed = await WaitFor(expected);
                    Console.WriteLine($"[caption-click-smoke] {(passed ? "PASS" : "FAIL")} {name} at {center.X:0},{center.Y:0}");
                    if (!passed) failures.Add(name);
                }

                _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                Require(await WaitFor(() => _presentation.IsPlaying) || await WaitFor(() => _presentation.IsPlaying), "fixture plays");
                WakeChrome();
                MacEventInjection.Move(handle.Handle, isView, Bounds.Width / 2, Bounds.Height / 2);
                await Task.Delay(200);
                Console.WriteLine($"[caption-click-smoke] window at {Position} size {Bounds.Size}; topOver after moving to the video={TopChrome.IsPointerOver}");
                // Let the playing chrome hide before the user reaches for it.
                if (!await WaitFor(() => !TopChrome.IsVisible, 10))
                    throw new InvalidOperationException($"playing chrome did not auto-hide: timer={_autoHideTimer.IsEnabled} topOver={TopChrome.IsPointerOver} dockOver={PlayerDock.IsPointerOver} focus={TopChrome.IsKeyboardFocusWithin || PlayerDock.IsKeyboardFocusWithin} keyboard={_keyboardNavigation}");
                bool lightsWithHiddenChrome = MacWindowButtons.IsVisible(handle.Handle, isView);
                Console.WriteLine($"[caption-click-smoke] {(lightsWithHiddenChrome ? "FAIL" : "PASS")} traffic lights hide with the playing chrome");
                if (lightsWithHiddenChrome) failures.Add("traffic lights hide with the chrome");
                await Click(HomeButton, "player back arrow while playing", () => _presentation.IsHomeOpen);
                if (!_presentation.IsHomeOpen) _presentation.IsHomeOpen = true;
                // Playback continues under Home, which must cover the video.
                bool opaque = HomeScreen.Background is Avalonia.Media.ISolidColorBrush { Color.A: 255, Opacity: 1 };
                Console.WriteLine($"[caption-click-smoke] {(opaque ? "PASS" : "FAIL")} Home covers the playing video");
                if (!opaque) failures.Add("Home covers the playing video");
                await Task.Delay(300);
                Button homeBack = HomeScreen.GetVisualDescendants().OfType<Button>()
                    .First(b => AutomationProperties.GetName(b) == "Return to player" && b.IsEffectivelyVisible);
                await Click(homeBack, "Home back arrow while playing", () => !_presentation.IsHomeOpen);
                if (_presentation.IsHomeOpen) _presentation.IsHomeOpen = false;
                await Task.Delay(300);
                await Click(OpenOptionsButton, "player options button", () => OpenMenu.IsVisible);
                bool lightsWithChrome = MacWindowButtons.IsVisible(handle.Handle, isView);
                Console.WriteLine($"[caption-click-smoke] {(lightsWithChrome ? "PASS" : "FAIL")} traffic lights show with the chrome");
                if (!lightsWithChrome) failures.Add("traffic lights show with the chrome");
                CloseOpenMenu();

                // The queue shortcut is both a menu key equivalent and a
                // window shortcut; it must toggle once.
                _presentation.IsQueueOpen = false;
                MacEventInjection.CommandKey(handle.Handle, isView, "j", 38);
                await Task.Delay(800);
                Console.WriteLine($"[caption-click-smoke] {(_presentation.IsQueueOpen ? "PASS" : "FAIL")} Cmd+J opens the queue once");
                if (!_presentation.IsQueueOpen) failures.Add("Cmd+J opens the queue once");

                if (failures.Count == 0)
                {
                    Console.WriteLine("[caption-click-smoke] PASS AppKit clicks reach the caption row buttons");
                    desktop.Shutdown(0);
                }
                else
                {
                    Console.Error.WriteLine($"[caption-click-smoke] FAIL {string.Join(", ", failures)}");
                    desktop.Shutdown(1);
                }
            }
            catch (Exception error) { Console.Error.WriteLine($"[caption-click-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

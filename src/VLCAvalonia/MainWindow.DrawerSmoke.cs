#if SMOKE
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableDrawerSmoke(IClassicDesktopStyleApplicationLifetime desktop, bool native)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                async Task WaitFor(Func<bool> condition, string message)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                await Task.Delay(400);
                _presentation.IsHomeOpen = false;
                if (native)
                {
                    OpenTestMedia();
                    await WaitFor(() => _presentation.IsPlaying && _presentation.AudioTracks.Count > 1 && _presentation.SubtitleTracks.Count > 1, "fixture supplies track choices");
                    foreach (var kind in new[] { MediaTrackKind.Audio, MediaTrackKind.Subtitle })
                    {
                        _presentation.DrawerTab = kind == MediaTrackKind.Audio ? "Audio" : "Subtitles";
                        _presentation.IsQueueOpen = true;
                        await Task.Delay(100);
                        IReadOnlyList<MediaTrack> Tracks() => kind == MediaTrackKind.Audio ? _presentation.AudioTracks : _presentation.SubtitleTracks;
                        string previous = Tracks().First(t => t.IsSelected && !t.IsDisabled).Id;
                        var off = QueueDrawer.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.DataContext is MediaTrack { IsDisabled: true });
                        Require(off.IsEnabled, "Disable is an enabled track choice");
                        off.Command!.Execute(off.CommandParameter);
                        await WaitFor(() => Tracks().Any(t => t.IsDisabled && t.IsSelected), "VLC readback confirms tracks disabled");
                        var command = kind == MediaTrackKind.Audio ? _presentation.SelectAudioTrackCommand : _presentation.SelectSubtitleTrackCommand;
                        command.Execute(previous);
                        await WaitFor(() => Tracks().Any(t => t.Id == previous && t.IsSelected), "VLC readback confirms original track restored");
                    }
                    Console.WriteLine("[drawer-smoke] PASS native audio/subtitle Disable and restore through the drawer commands");
                    desktop.Shutdown(0); return;
                }

                _presentation.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Paused });
                string label = "A very long media and language description — " + new string('W', 140);
                _presentation.ApplyQueueState(new(1, 1, QueueLoadState.Ready,
                    Enumerable.Range(0, 24).Select(i => new QueueItem(i.ToString(), $"{i} {label}", $"fixture-{i}.mp4", i, i == 0)).ToArray(), 0, QueueRepeatMode.All, QueueOrder.Random));
                MediaTrack[] tracks = [new("-1", "Disable", true, false), .. Enumerable.Range(0, 24).Select(i => new MediaTrack(i.ToString(), $"{i} {label}", false, i == 0))];
                _presentation.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready, tracks, tracks,
                    [new("0", label, true)], Enumerable.Range(0, 24).Select(i => new MediaChapter(i.ToString(), $"{i} {label}", i == 0)).ToArray(), new(0, 0)));
                _presentation.DrawerTab = "Queue";
                _presentation.IsQueueOpen = true;
                await WaitFor(() => CloseQueueButton.IsFocused, "opening the drawer places focus inside it");
                Button[] VisibleTabs() => DrawerTabs.Children.OfType<Button>().Where(tab => tab.IsVisible).ToArray();
                bool ShowsTabsOf(string tab) => VisibleTabs().Select(button => button.Tag as string).SequenceEqual(PlayerPresentationViewModel.TabsFor(tab));
                foreach (int width in new[] { 1280, 480 })
                {
                    Width = width; Height = width == 480 ? 360 : 720;
                    foreach (var entry in new[] { ("Queue", QueueScroll), ("Chapters", ChapterScroll), ("Audio", AudioScroll), ("Subtitles", SubtitleScroll) })
                    {
                        _presentation.DrawerTab = entry.Item1;
                        ScrollViewer scroll = entry.Item2;
                        scroll.Offset = default;
                        await Task.Delay(120);
                        Require(ShowsTabsOf(entry.Item1), $"{entry.Item1} shows its own panel's tabs");
                        Require(IsContained(QueueDrawer) && VisibleTabs().All(IsContained) && IsContained(scroll) && scroll.Viewport.Height > 20, "drawer viewport is contained and usable");
                        Require(scroll.Extent.Height > scroll.Viewport.Height, "long lists scroll vertically");
                        Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "long labels do not add horizontal overflow");
                        CaptureHome($"drawer-{entry.Item1}-{width}");
                        scroll.Offset = new Vector(0, scroll.Extent.Height);
                        await Task.Delay(100);
                        if (entry.Item1 == "Audio") Require(IsContained(AudioDelayInput), "audio delay remains reachable at the end of the list");
                        if (entry.Item1 == "Subtitles") Require(IsContained(SubtitleDelayInput), "subtitle delay remains reachable at the end of the list");
                    }
                    foreach (string tab in new[] { "Playback", "Video", "Information" })
                    {
                        _presentation.DrawerTab = tab;
                        await Task.Delay(120);
                        Require(ShowsTabsOf(tab), $"{tab} shows the playback settings tabs");
                        Require(IsContained(QueueDrawer) && VisibleTabs().All(IsContained), "playback settings panel and tabs are contained");
                        if (tab == "Playback") Require(IsContained(SpeedSlider) && SpeedSlider.Bounds.Width > 100, "speed slider is contained and usable");
                        CaptureHome($"drawer-{tab}-{width}");
                    }
                }

                // Each bar button opens its own tab set, shows the active state
                // while its panel is open, and gets focus back on Escape.
                Width = 1280; Height = 720;
                _presentation.IsQueueOpen = false;
                await Task.Delay(150);
                void Press(Control target, Key key) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
                foreach (var (opener, tab) in new (Button, string)[] { (QueueButton, "Queue"), (AudioTracksButton, "Audio"), (SubtitleTracksButton, "Subtitles"), (PlaybackSettingsButton, "Playback") })
                {
                    ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(opener)!).Invoke();
                    await Task.Delay(150);
                    Require(_presentation.IsQueueOpen && _presentation.DrawerTab == tab && ShowsTabsOf(tab), $"{opener.Name} opens {tab} with its tab set");
                    Require(ToolsGroup.Children.OfType<Button>().Where(button => button.Classes.Contains("active")).SequenceEqual([opener]), $"{opener.Name} alone shows the active state");
                    Button[] tabs = VisibleTabs();
                    tabs[0].Focus(NavigationMethod.Tab);
                    Press(tabs[0], Key.Right);
                    Require(_presentation.DrawerTab == (string)tabs[1].Tag! && tabs[1].IsFocused, "Right arrow selects and focuses the next tab");
                    Press(tabs[1], Key.End);
                    Require(_presentation.DrawerTab == (string)tabs[^1].Tag! && tabs[^1].IsFocused, "End selects the last tab");
                    Press(tabs[^1], Key.Home);
                    Require(_presentation.DrawerTab == (string)tabs[0].Tag! && tabs[0].IsFocused, "Home selects the first tab");
                    Press(tabs[0], Key.Left);
                    Require(_presentation.DrawerTab == (string)tabs[^1].Tag! && tabs[^1].IsFocused, "Left arrow wraps to the last tab");
                    Require(ShowsTabsOf(tab), "arrows stay within the panel's tabs");
                    if (opener == QueueButton)
                    {
                        Press(tabs[^1], Key.Home);
                        await Task.Delay(100);
                        CloseQueueButton.Focus(NavigationMethod.Tab);
                        for (int index = 0; index < 110; index++)
                        {
                            Require(FocusManager!.TryMoveFocus(NavigationDirection.Next), "Tab moves through drawer controls");
                            Require(QueueDrawer.IsKeyboardFocusWithin, "Tab stays inside the drawer");
                        }
                    }
                    (FocusManager!.GetFocusedElement() as Control)!.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Escape });
                    await Task.Delay(150);
                    Require(!_presentation.IsQueueOpen && opener.IsFocused, $"Escape closes the {tab} panel and returns focus to {opener.Name}");
                    Require(!ToolsGroup.Children.OfType<Button>().Any(button => button.Classes.Contains("active")), "no bar button is active once the panel closes");
                }
                // Narrow: the track buttons are hidden, so focus returns to More.
                Width = 480; Height = 360;
                await Task.Delay(150);
                _presentation.ToggleMediaControlsCommand.Execute("Audio");
                await Task.Delay(150);
                Require(!AudioTracksButton.IsVisible && IsContained(QueueDrawer), "narrow tracks panel is contained");
                CloseQueueButton.Focus(NavigationMethod.Tab);
                CloseQueueButton.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Escape });
                await Task.Delay(150);
                Require(!_presentation.IsQueueOpen && OpenOptionsButton.IsFocused, "narrow Escape returns focus to More");
                Console.WriteLine("[drawer-smoke] PASS per-button tab sets and active states, long labels/lists, compact scrolling, reachable delays, tab arrows, focus cycle and Escape focus return");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[drawer-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

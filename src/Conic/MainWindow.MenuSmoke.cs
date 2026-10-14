#if SMOKE
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Controls;
using Conic.Interop;

namespace Conic;

// Phase 7 menus and dialogs: the More menu (options, groups, keys, Open
// Recent page, closing and focus), the right-click menu (groups, shortcut
// hints, speed check, keyboard submenu), Open Network Stream in the modal
// shell (bounds, compact, Escape and focus) and Keyboard Shortcuts.
public partial class MainWindow
{
    private static readonly string[] MoreMenuOptions =
    [
        "Open File…", "Open Folder…", "Open Network Stream…", "Load Playlist…", "Open Recent",
        "Stop", "Tracks and Synchronization", "Video Adjustments", "Media Information", "Picture in Picture",
        "Keyboard Shortcuts", "Settings",
    ];

    private static readonly string[] VideoMenuOptions =
    [
        "Pause", "Stop", "Previous Item", "Next Item", "Skip Back 10 Seconds", "Skip Forward 10 Seconds",
        "Playback", "Video", "Audio", "Subtitle", "More Settings", "Playlist", "Open Media",
    ];

    internal void EnableMenuSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal;
        Width = 1280; Height = 720;
        VideoSurface.EngineStateChanged -= OnEngineStateChanged;
        _presentation.IsHomeOpen = false;
        Opened += async (_, _) =>
        {
            try
            {
                // Focus checks need the activated window: activation restores
                // the focus it saved, so let it settle first.
                Activate();
                for (DateTime deadline = DateTime.UtcNow.AddSeconds(5); !IsActive && DateTime.UtcNow < deadline;) await Task.Delay(50);
                await Task.Delay(1000);
                _presentation.ApplyEngineState(MediaEngineState.Initial with
                {
                    Generation = 1, Status = PlaybackStatus.Playing, CanSeek = true, Position = .25,
                    Elapsed = TimeSpan.FromSeconds(30), Duration = TimeSpan.FromSeconds(120), Volume = .75, Title = "Menu review",
                });
                IReadOnlyDictionary<PlayerCommandId, string> shortcuts = EffectiveShortcuts();
                Control? Focused() => FocusManager?.GetFocusedElement() as Control;
                // Posted focus moves (opening the menu, closing a page) must
                // land before the next check: wait until the dispatcher is idle.
                async Task Settle()
                {
                    await Task.Delay(60);
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.ContextIdle);
                }
                async Task Key(Key key)
                {
                    await Settle();
                    Control target = Focused() ?? OpenMenu;
                    target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, Source = target });
                    await Settle();
                }

                // More menu: the design's groups with the app's own commands
                // in the middle, hints from the effective shortcuts.
                OpenOptionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
                MenuRow[] rows = MoreMenuMain.GetVisualDescendants().OfType<MenuRow>().ToArray();
                Require(rows.Select(row => row.Content as string).SequenceEqual(MoreMenuOptions), $"More menu options: {string.Join(", ", rows.Select(row => row.Content))}");
                Require(rows.All(row => !string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(row).GetName())), "More menu options are named");
                Require(rows[0].Shortcut == PlayerShortcutPolicy.Display(shortcuts[PlayerCommandId.OpenMedia]) && rows[^1].Shortcut == PlayerShortcutPolicy.Display(shortcuts[PlayerCommandId.ShowAbout]), "More menu shows the effective shortcuts");
                Require(rows.All(row => Math.Abs(row.Bounds.Height - 48) < .5) && Math.Abs(OpenMenu.Bounds.Width - 375) < .5, "design option and menu sizes");
                CaptureMenu("more-menu");

                // Keys: Down from the menu goes to the first option, End to
                // the last, Up wraps back; Right opens Open Recent, Left
                // returns to its option.
                await Settle();
                OpenMenu.Focus();
                await Key(Avalonia.Input.Key.Down);
                Require(ReferenceEquals(Focused(), rows[0]), $"Down focuses the first option: {Describe(Focused())}");
                await Key(Avalonia.Input.Key.Up);
                Require(ReferenceEquals(Focused(), rows[^1]), "Up wraps to the last option");
                await Key(Avalonia.Input.Key.Home);
                Require(ReferenceEquals(Focused(), rows[0]), "Home focuses the first option");
                OpenRecentRow.Focus(NavigationMethod.Directional);
                await Key(Avalonia.Input.Key.Right);
                Require(MoreMenuRecent.IsVisible && !MoreMenuMain.IsVisible && RecentBackRow.IsFocused, $"Right opens Open Recent: recent={MoreMenuRecent.IsVisible} main={MoreMenuMain.IsVisible} focused={Focused()?.GetType().Name}/{(Focused() as ContentControl)?.Content}");
                CaptureMenu("more-menu-recent");
                await Key(Avalonia.Input.Key.Escape);
                Require(MoreMenuMain.IsVisible && OpenMenu.IsVisible && OpenRecentRow.IsFocused, "Escape leaves Open Recent first");
                await Key(Avalonia.Input.Key.Right);
                await Key(Avalonia.Input.Key.Left);
                Require(MoreMenuMain.IsVisible && OpenRecentRow.IsFocused, $"Left returns from Open Recent: recent={MoreMenuRecent.IsVisible} focused={Focused()?.GetType().Name}/{(Focused() as ContentControl)?.Content}");

                // Choosing an option closes the menu and gives the focus back.
                rows.Single(row => row.Content as string == "Media Information").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Require(!OpenMenu.IsVisible && OpenOptionsButton.IsFocused, "an option closes the menu and focus returns to More");
                OnOpenOptions(null, new RoutedEventArgs());
                await Settle();
                KeyEventArgs escape = new() { Key = Avalonia.Input.Key.Escape };
                OnPlayerKeyDown(this, escape);
                Require(escape.Handled && !OpenMenu.IsVisible && OpenOptionsButton.IsFocused, "Escape closes the menu and focus returns to More");

                // Right-click menu, opened as the keyboard (Apps key) does.
                PlayButton.Focus(NavigationMethod.Tab);
                PlayButton.RaiseEvent(new ContextRequestedEventArgs());
                await Task.Delay(300);
                Require(VideoContextMenu.IsOpen, "the context menu request opens the right-click menu");
                MenuItem[] items = VideoContextMenu.Items.OfType<MenuItem>().ToArray();
                Require(items.Select(item => item.Header as string).SequenceEqual(VideoMenuOptions), $"right-click options: {string.Join(", ", items.Select(item => item.Header))}");
                Require(VideoContextMenu.Items.OfType<Separator>().Count() == 2, "right-click groups");
                Require(Equals(items[0].InputGesture, MenuShortcuts.ToKeyGesture(shortcuts[PlayerCommandId.TogglePause])), "right-click shows the effective shortcut");
                MenuItem playback = items.Single(item => item.Header as string == "Playback");
                MenuItem[] speeds = playback.Items.OfType<MenuItem>().Where(item => (item.Header as string)!.StartsWith("Speed", StringComparison.Ordinal)).ToArray();
                Require(speeds.Length == 4 && speeds.Count(item => item.IsChecked) == 1 && speeds[1].IsChecked, "the current speed is checked");
                Require(VideoContextMenu.GetLogicalDescendants().OfType<MenuItem>().All(item => !string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(item).GetName())), "right-click options are named");
                playback.Focus(NavigationMethod.Directional);
                playback.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Avalonia.Input.Key.Right, Source = playback });
                await Task.Delay(200);
                Require(playback.IsSubMenuOpen, "Right opens a submenu");
                VideoContextMenu.Close();
                await Task.Delay(150);

                // Open Network Stream: the Settings shell, centered below the
                // title bar; Escape closes it and focus returns to More.
                ShowUrlPanel();
                await Task.Delay(200);
                await Settle();
                Rect dialog = GetClientRect(UrlPanel.DialogPart!);
                Require(UrlPanel.IsVisible && UrlTextBox.IsFocused, "the URL field has focus");
                Require(dialog.Width <= 1048.5 && dialog.Top >= PlatformChrome.TitleBarHeight && Math.Abs(dialog.Left - (ClientSize.Width - dialog.Right)) <= 1 &&
                    IsContained(UrlPanel.DialogPart!) && IsContained(UrlSubmitButton), $"Open Network Stream is centered below the title bar: {dialog}");
                CaptureMenu("network-stream");
                escape = new() { Key = Avalonia.Input.Key.Escape };
                OnPlayerKeyDown(this, escape);
                Require(escape.Handled && !UrlPanel.IsVisible && OpenOptionsButton.IsFocused, "Escape closes Open Network Stream and focus returns to More");
                Width = 480; Height = 360;
                await Task.Delay(250);
                ShowUrlPanel();
                await Task.Delay(200);
                dialog = GetClientRect(UrlPanel.DialogPart!);
                Require(UrlPanel.IsCompact && dialog.Left <= .5 && dialog.Top <= .5 && dialog.Right >= ClientSize.Width - .5 && IsContained(UrlSubmitButton) && IsContained(UrlCancelButton), $"compact Open Network Stream fills the window: {dialog}");
                CaptureMenu("network-stream-compact");
                CloseUrlPanel();
                Width = 1280; Height = 720;
                await Task.Delay(250);

                // Keyboard Shortcuts opens the Controls page with its search.
                ShowKeyboardShortcuts();
                await Task.Delay(300);
                await Settle();
                Require(SettingsPanel.IsVisible && _settingsViewModel.IsControls && SettingsPanel.ShortcutSearchBox.IsFocused, $"Keyboard Shortcuts opens the shortcut editor: settings={SettingsPanel.IsVisible} controls={_settingsViewModel.IsControls} focused={Describe(Focused())}");
                CaptureMenu("keyboard-shortcuts");
                CloseSettings();

                // Home's "…" opens the same menu and gets the focus back.
                _presentation.IsHomeOpen = true;
                await Task.Delay(200);
                OnOpenOptions(null, new RoutedEventArgs());
                await Task.Delay(150);
                Require(OpenMenu.IsVisible && IsContained(OpenMenu) && MoreMenuMain.IsVisible, "Home opens the same More menu");
                CloseOpenMenu();
                await Settle();
                Require(Focused() is { } homeFocus && Avalonia.Automation.AutomationProperties.GetName(homeFocus) == "Open options", "focus returns to Home's More button");
                Console.WriteLine("[menu-smoke] PASS More menu groups, hints, keys, Open Recent and focus; right-click groups, hints, speed check and keyboard submenu; Open Network Stream shell wide and compact; Keyboard Shortcuts; Home menu");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[menu-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }

    private string Describe(Control? control) => $"active={IsActive} " + (control is null ? "none" : $"{control.GetType().Name}#{control.Name} '{(control as ContentControl)?.Content}' {Avalonia.Automation.AutomationProperties.GetName(control)}");

    private void CaptureMenu(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "menu-review");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    // Review captures (--menu-review=<step>[,WIDTHxHEIGHT] in DIPs): plays the
    // test video, pauses it and opens the menu or dialog, which stays open
    // for a screen capture. Context menus are popups, so they are opened here
    // rather than with a real right-click.
    internal void EnableMenuReview(string step, Size? size)
    {
        WindowState = WindowState.Normal;
        Width = size?.Width ?? 1280; Height = size?.Height ?? 720;
        Position = new PixelPoint(0, 0);
        Opened += async (_, _) =>
        {
            OpenTestMedia();
            for (DateTime deadline = DateTime.UtcNow.AddSeconds(20); !_presentation.IsPlaying && DateTime.UtcNow < deadline;) await Task.Delay(100);
            await Task.Delay(2500);
            if (_presentation.IsPlaying) _presentation.TogglePauseCommand.Execute(null);
            await Task.Delay(500);
            switch (step)
            {
                case "more":
                case "more-recent":
                    OnOpenOptions(null, new RoutedEventArgs());
                    if (step == "more-recent") ShowOpenMenuPage(recent: true, focus: true);
                    break;
                case "more-home":
                    _presentation.IsHomeOpen = true;
                    await Task.Delay(500);
                    OnOpenOptions(null, new RoutedEventArgs());
                    break;
                case "context":
                case "context-playback":
                    PlayButton.Focus(NavigationMethod.Tab);
                    PlayButton.RaiseEvent(new ContextRequestedEventArgs());
                    await Task.Delay(400);
                    if (step == "context-playback" && VideoContextMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Header as string == "Playback") is { } playback)
                    {
                        playback.Focus(NavigationMethod.Directional);
                        playback.IsSubMenuOpen = true;
                    }
                    break;
                case "home-context":
                    _presentation.IsHomeOpen = true;
                    await Task.Delay(800);
                    if (HomeScreen.GetVisualDescendants().OfType<Panel>().FirstOrDefault(panel => panel.Classes.Contains("mediaCard")) is { } card)
                    {
                        card.Focus();
                        card.RaiseEvent(new ContextRequestedEventArgs());
                    }
                    break;
                case "network":
                    ShowUrlPanel();
                    break;
                case "network-error":
                    ShowUrlPanel();
                    _presentation.UrlText = "gopher://example.com/stream.mp4";
                    OnUrlSubmit(null, new RoutedEventArgs());
                    break;
                case "shortcuts":
                    ShowKeyboardShortcuts();
                    break;
            }
        };
    }
}
#endif // SMOKE

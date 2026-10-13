#if SMOKE
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Controls;
using Conic.Interop;
using Conic.ViewModels;

namespace Conic;

// --interactions-smoke (the test coverage audit's gaps 1 to 5): what a user
// does with the window's own controls and handlers, in a real VLC session.
// Opening media (More menu rows with the file pickers answered by the
// smoke, Open Recent, library and Home rows, drag and drop), queue editing
// (Previous from the control bar, the right-click menu and the mini-player;
// Move, Remove and Clear in the drawer), every default shortcut pressed on
// the focused control as the keyboard would (the resume prompt, the panels'
// Escape and Enter in the network URL field included), clicks, double
// clicks and the wheel on the video, and the drawer's delay, Add audio file,
// title and chapter controls. Buttons are pressed through their automation
// peers, keys and pointer input are raised on the window's controls. Fixtures
// come from artifacts/settings-fixtures (eng/make-settings-fixtures.ps1). The
// window is activated, as the keys go where the focus is.
public partial class MainWindow
{
    internal void EnableInteractionsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> passed = [];
            // Shortcut checks go on after a miss, so one run reports every key that fails.
            List<string> keyFailures = [];
            try
            {
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string Fixture(string name)
                {
                    string path = Path.Combine(fixtures, name);
                    Require(File.Exists(path), $"fixture {name} exists (run eng/make-settings-fixtures.ps1)");
                    return path;
                }
                string work = Path.Combine(RepositoryPaths.ArtifactsDirectory, "interactions");
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string gop = Fixture("gop.mp4"), dark = Fixture("dark.mp4"), chapters = Fixture("chapters.mkv"), languages = Fixture("languages.mkv"), audio = Fixture("audio-mix.wav");

                void Pass(string what) { passed.Add(what); Console.WriteLine($"[interactions-smoke] ok: {what}"); }
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                async Task<bool> Within(Func<bool> condition, double seconds)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    return condition();
                }
                // Posted focus moves and bindings land before the next step.
                async Task Settle()
                {
                    await Task.Delay(80);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                }
                MediaEngineState Engine() => VideoSurface.Player!.State;
                double Elapsed() => VideoSurface.Player?.State.Elapsed.TotalSeconds ?? 0;
                bool Plays(string path) => _presentation.IsPlaying && string.Equals(CurrentSource(), path, StringComparison.OrdinalIgnoreCase);
                string? CurrentSource() => _presentation.QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source is { } source && Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : _presentation.QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source;
                string Queue() => string.Join(", ", _presentation.QueueItems.Select(item => Path.GetFileName(item.Source) + (item.IsCurrent ? "*" : "")));
                Control? Focused() => FocusManager?.GetFocusedElement() as Control;
                // A press needs a control the user can see and use.
                async Task Press(Control control, string what)
                {
                    await Settle();
                    Require(control.IsEffectivelyVisible && control.IsEffectivelyEnabled, $"{what} is visible and enabled ({control.IsEffectivelyVisible}, {control.IsEffectivelyEnabled})");
                    ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(control)).Invoke();
                    await Settle();
                }
                Button Named(Visual root, string name) =>
                    root.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name)
                    ?? throw new InvalidOperationException($"no visible button named \"{name}\"");
                // Keys go to the focused control, as from the keyboard, and bubble to the window's handlers.
                async Task Key(Key key, KeyModifiers modifiers = KeyModifiers.None)
                {
                    await Settle();
                    Control target = Focused() ?? VideoArea;
                    target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });
                    Control up = Focused() ?? VideoArea;
                    up.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = up });
                    await Settle();
                }
                async Task<string> Shortcut(PlayerCommandId command)
                {
                    string gesture = EffectiveShortcuts()[command];
                    KeyGesture key = MenuShortcuts.ToKeyGesture(gesture) ?? throw new InvalidOperationException($"{gesture} has no key");
                    string focus = Describe(Focused());
                    await Key(key.Key, key.KeyModifiers);
                    return $"{gesture} (focus {focus})";
                }
                void KeyCheck(bool condition, string message)
                {
                    if (condition) return;
                    keyFailures.Add(message);
                    Console.Error.WriteLine($"[interactions-smoke] key miss: {message}");
                }
                async Task Open(string path)
                {
                    _presentation.OpenSource(path, MediaSourceKind.File);
                    await WaitFor(() => Plays(path), $"{Path.GetFileName(path)} plays (queue {Queue()})");
                }
                Point VideoCenter() => VideoSurface.TranslatePoint(new Point(VideoSurface.Bounds.Width / 2, VideoSurface.Bounds.Height / 2), this)!.Value;
                void Click(int count)
                {
                    Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    VideoSurface.RaiseEvent(new PointerPressedEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, count));
                    VideoSurface.RaiseEvent(new PointerReleasedEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
                    pointer.Capture(null);
                }
                void Wheel(double x, double y)
                {
                    Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    VideoSurface.RaiseEvent(new PointerWheelEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(x, y)));
                }
                async Task<DataTransfer> Files(params string[] paths)
                {
                    DataTransfer data = new();
                    foreach (string path in paths)
                        data.Add(DataTransferItem.CreateFile((await StorageProvider.TryGetFileFromPathAsync(new Uri(path)))
                            ?? throw new InvalidOperationException($"no storage file for {path}")));
                    return data;
                }
                DragEventArgs Drag(RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data) =>
                    new(routedEvent, data, VideoArea, VideoCenter(), KeyModifiers.None) { DragEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link };

                _settings.ResetAll();
                _settings.Update(s => s with { ResumePlayback = false });
                _mediaPicker.ScriptedForSmoke = true;
                int finished = 0;
                _presentation.PlaybackFinished += (_, _) => finished++;
                Activate();
                await WaitFor(() => IsActive, "the window is active (the keys go where the focus is)", 5);
                await Task.Delay(1000);

                // ---- Opening media (gap 2) ----
                Require(_presentation.IsHomeOpen, "the app starts on Home");
                _mediaPicker.PicksForSmoke.Enqueue(gop);
                OnOpenOptions(null, new RoutedEventArgs());
                await Settle();
                await Press(MoreMenuMain.GetVisualDescendants().OfType<MenuRow>().Single(row => row.Content as string == "Open File…"), "More › Open File…");
                await WaitFor(() => Plays(gop), $"the picked file plays (queue {Queue()})");
                Require(_mediaPicker.RequestsForSmoke.SequenceEqual(["media"]) && !_presentation.IsHomeOpen && !OpenMenu.IsVisible, "Open File… asked for one file, closed the menu and left Home");
                Pass("Open File… plays the picked file");

                string folder = Path.Combine(work, "folder");
                Directory.CreateDirectory(Path.Combine(folder, "sub"));
                File.Copy(dark, Path.Combine(folder, "a.mp4"));
                File.Copy(gop, Path.Combine(folder, "sub", "b.mp4"));
                _mediaPicker.PicksForSmoke.Enqueue(folder);
                OnOpenOptions(null, new RoutedEventArgs());
                await Press(OpenFolderAction, "More › Open Folder…");
                await WaitFor(() => _presentation.QueueItems.Count == 2 && Plays(Path.Combine(folder, "a.mp4")), $"the picked folder's two files are queued and the first plays (queue {Queue()})");
                Require(_mediaPicker.RequestsForSmoke[^1] == "folder", "Open Folder… asked for a folder");
                Pass("Open Folder… queues the picked folder");

                OnOpenOptions(null, new RoutedEventArgs());
                await Settle();
                OnShowRecentPage(null, new RoutedEventArgs());
                await Settle();
                await Press(MoreMenuRecent.GetVisualDescendants().OfType<Button>().First(button => button.DataContext is RecentMediaEntry entry && string.Equals(entry.Source, gop, StringComparison.OrdinalIgnoreCase)), "Open Recent › gop.mp4");
                await WaitFor(() => Plays(gop) && _presentation.QueueItems.Count == 1, $"the recent row opens its file ({Queue()})");
                Require(!OpenMenu.IsVisible, "the menu closes");
                Pass("an Open Recent row opens its file");

                _presentation.IsLibraryOpen = true;
                await Settle();
                await Press(LibraryPanel.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.DataContext is RecentMediaEntry entry && entry.Source.EndsWith("a.mp4", StringComparison.OrdinalIgnoreCase)), "library › Recent › a.mp4");
                await WaitFor(() => Plays(Path.Combine(folder, "a.mp4")), $"the library row opens its file ({Queue()})");
                Require(!_presentation.IsLibraryOpen, "the library drawer closes");
                Pass("a library row opens its file");

                _presentation.ShowHomeCommand.Execute(null);
                await Settle();
                await Press(HomeScreen.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Classes.Contains("mediaTile") && button.DataContext is HomeMediaItem item && string.Equals(item.Source, gop, StringComparison.OrdinalIgnoreCase)), "the Home card of gop.mp4");
                await WaitFor(() => Plays(gop) && !_presentation.IsHomeOpen, $"the Home card opens its file and leaves Home ({Queue()})");
                Pass("a Home card opens its file");

                // Drag and drop on the window: local files are accepted, anything else is not.
                DragEventArgs over = Drag(DragDrop.DragOverEvent, await Files(dark));
                VideoArea.RaiseEvent(over);
                DataTransfer text = new();
                text.Add(DataTransferItem.CreateText("https://example.invalid/clip.mp4"));
                DragEventArgs overText = Drag(DragDrop.DragOverEvent, text);
                VideoArea.RaiseEvent(overText);
                Require(over.Handled && over.DragEffects == DragDropEffects.Copy && overText.Handled && overText.DragEffects == DragDropEffects.None, $"a file drag is accepted as a copy, text is refused ({over.DragEffects}, {overText.DragEffects})");
                VideoArea.RaiseEvent(Drag(DragDrop.DropEvent, await Files(dark)));
                await WaitFor(() => Plays(dark) && _presentation.QueueItems.Count == 1, $"a dropped file plays ({Queue()})");
                DragEventArgs drop = Drag(DragDrop.DropEvent, await Files(gop, dark, chapters));
                VideoArea.RaiseEvent(drop);
                await WaitFor(() => _presentation.QueueItems.Count == 3 && Plays(gop), $"three dropped files are queued in order ({Queue()})");
                Require(drop.Handled, "the drop is handled");
                Pass("drag over and drop on the window");

                // ---- Queue editing (gap 1), on gop, dark, chapters ----
                async Task Current(int index, string message) =>
                    await WaitFor(() => _presentation.QueueState.CurrentIndex == index && _presentation.IsPlaying, $"{message} (queue {Queue()}, index {_presentation.QueueState.CurrentIndex})");
                await Press(NextButton, "the control bar's Next");
                await Current(1, "Next plays the second item");
                await Press(PreviousButton, "the control bar's Previous");
                await Current(0, "the control bar's Previous plays the first item again");
                Pass("Previous in the control bar");
                await Press(NextButton, "the control bar's Next");
                await Current(1, "Next plays the second item");
                PlayButton.RaiseEvent(new ContextRequestedEventArgs());
                await Task.Delay(300);
                Require(VideoContextMenu.IsOpen, "the right-click menu opens");
                MenuItem previousItem = VideoContextMenu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Previous Item");
                Require(previousItem.IsEffectivelyEnabled, "Previous Item is enabled on the second item");
                previousItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Current(0, "the right-click menu's Previous Item plays the first item again");
                VideoContextMenu.Close();
                Pass("Previous Item in the right-click menu");
                await Press(NextButton, "the control bar's Next");
                await Current(1, "Next plays the second item");
                _presentation.ShowHomeCommand.Execute(null);
                await WaitFor(() => _presentation.ShowMiniPlayer, "the mini-player shows on Home");
                await Press(Named(HomeScreen, "Previous queue item"), "the mini-player's Previous");
                await Current(0, "the mini-player's Previous plays the first item again");
                _presentation.ShowPlayerCommand.Execute(null);
                Pass("Previous in the mini-player");

                _presentation.DrawerTab = "Queue";
                _presentation.IsQueueOpen = true;
                await Settle();
                Button RowAction(string source, string tip) => QueueDrawer.GetVisualDescendants().OfType<Button>()
                    .First(button => button.IsEffectivelyVisible && button.DataContext is QueueItem item && string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase) && ToolTip.GetTip(button) as string == tip);
                string[] Order() => _presentation.QueueItems.Select(item => item.Source).ToArray();
                bool InOrder(params string[] paths) => Order().Select(source => Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : source).SequenceEqual(paths, StringComparer.OrdinalIgnoreCase);
                await Press(RowAction(gop, "Move earlier"), "Move earlier on the first row");
                await Task.Delay(500);
                Require(InOrder(gop, dark, chapters), $"the first row cannot move earlier ({Queue()})");
                await Press(RowAction(gop, "Move later"), "Move later on gop.mp4");
                await WaitFor(() => InOrder(dark, gop, chapters), $"Move later swaps the first two rows ({Queue()})");
                await Press(RowAction(chapters, "Move earlier"), "Move earlier on chapters.mkv");
                await WaitFor(() => InOrder(dark, chapters, gop), $"Move earlier swaps the last two rows ({Queue()})");
                Require(Plays(gop), "what plays keeps playing while rows move");
                Pass("Move earlier and Move later in the queue");
                await Press(RowAction(chapters, "Remove from queue"), "Remove on chapters.mkv");
                await WaitFor(() => InOrder(dark, gop), $"Remove takes the row out ({Queue()})");
                Require(Plays(gop), "what plays keeps playing");
                Pass("Remove from queue");
                await Press(Named(QueueDrawer, "Clear queue"), "Clear queue");
                await WaitFor(() => _presentation.QueueItems.Count == 0 && !_presentation.IsPlaying, $"Clear queue empties the queue and stops ({Queue()}, {_presentation.PlaybackStatus})");
                await Task.Delay(500);
                Require(finished == 0, "Clear queue is not the end of the queue (no Playback finished action)");
                _presentation.IsQueueOpen = false;
                Pass("Clear queue");

                // ---- Every default shortcut (gap 3), pressed where the focus is ----
                _settings.Update(s => s with { SkipBackwardSeconds = 4, SkipForwardSeconds = 5, VeryShortSkipSeconds = 2, MediumSkipSeconds = 6, LongSkipSeconds = 8 });
                await Open(gop);
                _presentation.Registry.Execute(PlayerCommandId.Seek, .5);
                await WaitFor(() => Math.Abs(Elapsed() - 15) < 1.5, "seeked to the middle");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "paused");
                string pressed = await Shortcut(PlayerCommandId.TogglePause);
                KeyCheck(await Within(() => _presentation.IsPlaying, 3), $"{pressed} plays");
                pressed = await Shortcut(PlayerCommandId.TogglePause);
                KeyCheck(await Within(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, 3), $"{pressed} pauses");
                foreach ((PlayerCommandId command, int seconds) in new[]
                {
                    (PlayerCommandId.SkipForward, 5), (PlayerCommandId.SkipBackward, -4), (PlayerCommandId.SkipForwardVeryShort, 2), (PlayerCommandId.SkipBackwardVeryShort, -2),
                    (PlayerCommandId.SkipForwardMedium, 6), (PlayerCommandId.SkipBackwardMedium, -6), (PlayerCommandId.SkipForwardLong, 8), (PlayerCommandId.SkipBackwardLong, -8),
                })
                {
                    double before = Elapsed();
                    pressed = await Shortcut(command);
                    KeyCheck(await Within(() => Math.Abs(Elapsed() - before - seconds) < 1.2, 5), $"{pressed} moves {seconds} s ({before:0.0} -> {Elapsed():0.0})");
                }
                bool muted = _presentation.IsMuted;
                pressed = await Shortcut(PlayerCommandId.ToggleMute);
                KeyCheck(await Within(() => _presentation.IsMuted != muted, 3), $"{pressed} mutes");
                pressed = await Shortcut(PlayerCommandId.ToggleMute);
                KeyCheck(await Within(() => _presentation.IsMuted == muted, 3), $"{pressed} unmutes");
                pressed = await Shortcut(PlayerCommandId.ToggleFullscreen);
                KeyCheck(await Within(() => WindowState == WindowState.FullScreen, 3), $"{pressed} goes fullscreen");
                pressed = await Shortcut(PlayerCommandId.ToggleFullscreen);
                KeyCheck(await Within(() => WindowState != WindowState.FullScreen, 3), $"{pressed} leaves fullscreen");
                if (WindowState == WindowState.FullScreen) ToggleFullscreen();
                int requests = _mediaPicker.RequestsForSmoke.Count;
                pressed = await Shortcut(PlayerCommandId.OpenMedia);
                KeyCheck(await Within(() => _mediaPicker.RequestsForSmoke.Count > requests && _mediaPicker.RequestsForSmoke[^1] == "media", 3), $"{pressed} asks for a file");
                requests = _mediaPicker.RequestsForSmoke.Count;
                pressed = await Shortcut(PlayerCommandId.OpenFolder);
                KeyCheck(await Within(() => _mediaPicker.RequestsForSmoke.Count > requests && _mediaPicker.RequestsForSmoke[^1] == "folder", 3), $"{pressed} asks for a folder");
                Require(Plays(gop) || _presentation.PlaybackStatus == PlaybackStatus.Paused, "cancelled pickers leave gop.mp4 open");
                pressed = await Shortcut(PlayerCommandId.OpenSettings);
                KeyCheck(SettingsPanel.IsVisible, $"{pressed} opens Settings");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!SettingsPanel.IsVisible, $"Escape closes Settings (focus {Describe(Focused())})");
                if (SettingsPanel.IsVisible) CloseSettings();
                pressed = await Shortcut(PlayerCommandId.OpenLibrary);
                KeyCheck(_presentation.IsLibraryOpen, $"{pressed} opens the library drawer");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!_presentation.IsLibraryOpen, $"Escape closes the library drawer (focus {Describe(Focused())})");
                if (_presentation.IsLibraryOpen) CloseLibrary();
                pressed = await Shortcut(PlayerCommandId.ToggleQueue);
                KeyCheck(_presentation.IsQueueOpen && _presentation.DrawerTab == "Queue", $"{pressed} opens the queue");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!_presentation.IsQueueOpen, $"Escape closes the queue (focus {Describe(Focused())})");
                pressed = await Shortcut(PlayerCommandId.MediaInformation);
                KeyCheck(_presentation.IsQueueOpen && _presentation.DrawerTab == "Information", $"{pressed} opens Media Information");
                await Key(Avalonia.Input.Key.Escape);
                if (_presentation.IsQueueOpen) CloseDrawer();
                pressed = await Shortcut(PlayerCommandId.Snapshot);
                KeyCheck(_presentation.MediaControlsNotice.Contains("Snapshot", StringComparison.Ordinal), $"{pressed} answers with the snapshot notice");
                pressed = await Shortcut(PlayerCommandId.TogglePictureInPicture);
                KeyCheck(await Within(() => _presentation.IsPictureInPicture, 3), $"{pressed} enters picture in picture");
                pressed = await Shortcut(PlayerCommandId.TogglePictureInPicture);
                KeyCheck(await Within(() => !_presentation.IsPictureInPicture, 3), $"{pressed} leaves picture in picture");
                if (_presentation.IsPictureInPicture) ExitPictureInPicture();
                pressed = await Shortcut(PlayerCommandId.ShowHome);
                KeyCheck(_presentation.IsHomeOpen, $"{pressed} shows Home");
                _presentation.ShowPlayerCommand.Execute(null);
                await Settle();

                // Open Network Stream: Enter in the URL field opens it, or keeps the field with the error.
                using (MediaServer server = new(gop))
                {
                    pressed = await Shortcut(PlayerCommandId.OpenUrl);
                    KeyCheck(UrlPanel.IsVisible && UrlTextBox.IsFocused, $"{pressed} opens Open Network Stream with the focus in the address");
                    if (!UrlPanel.IsVisible) ShowUrlPanel();
                    await Settle();
                    UrlTextBox.Focus();
                    UrlTextBox.Text = "ftp://example.invalid/clip.mp4";
                    await Key(Avalonia.Input.Key.Enter);
                    Require(UrlPanel.IsVisible && _presentation.HasOpenError && UrlTextBox.IsFocused, $"Enter on an ftp address keeps the dialog with its error ({_presentation.OpenError})");
                    UrlTextBox.Text = server.Url;
                    await Key(Avalonia.Input.Key.Enter);
                    await WaitFor(() => !UrlPanel.IsVisible && _presentation.IsPlaying && Engine().Source?.StartsWith("http://127.0.0.1", StringComparison.Ordinal) == true, $"Enter in the address plays the stream ({Engine().Source}, {_presentation.PlaybackStatus})");
                    Pass("Enter in the network address field");
                    _presentation.StopCommand.Execute(null);
                    await WaitFor(() => !_presentation.IsPlaying, "the stream stops");
                }

                // The resume prompt: Enter resumes, Escape closes it and keeps the saved position.
                _settings.Update(s => s with { ResumePlayback = true, ResumeAutomatically = false, ResumeMinimumElapsedSeconds = 5, ResumeMaximumRemainingSeconds = 5 });
                async Task Prompt()
                {
                    await Open(gop);
                    // gop.mp4 has a saved position from earlier cases: that prompt is not the one checked.
                    if (_presentation.HasPendingResume) _presentation.Registry.Execute(PlayerCommandId.DismissResume);
                    _presentation.Registry.Execute(PlayerCommandId.Seek, .5);
                    await WaitFor(() => Math.Abs(Elapsed() - 15) < 1.5, "seeked to 15 s");
                    _presentation.StopCommand.Execute(null);
                    await WaitFor(() => !_presentation.IsPlaying, "stopped, the position saved");
                    _presentation.OpenSource(gop, MediaSourceKind.File);
                    await WaitFor(() => _presentation.HasPendingResume && ResumePanel.IsVisible, "reopening offers to resume");
                    await Settle();
                }
                await Prompt();
                string focus = Describe(Focused());
                await Key(Avalonia.Input.Key.Enter);
                KeyCheck(await Within(() => !_presentation.HasPendingResume && Elapsed() > 13, 5), $"Enter on the resume prompt resumes at 15 s (focus {focus}, at {Elapsed():0.0} s)");
                if (_presentation.HasPendingResume) _presentation.Registry.Execute(PlayerCommandId.DismissResume);
                await Prompt();
                focus = Describe(Focused());
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(await Within(() => !_presentation.HasPendingResume && !ResumePanel.IsVisible, 3) && Elapsed() < 10 && _presentation.ResumeItems.Any(entry => string.Equals(entry.Source, gop, StringComparison.OrdinalIgnoreCase)),
                    $"Escape closes the resume prompt, plays from the start and keeps the position (focus {focus}, at {Elapsed():0.0} s)");
                if (_presentation.HasPendingResume) _presentation.Registry.Execute(PlayerCommandId.DismissResume);
                Require(keyFailures.Count == 0, $"{keyFailures.Count} shortcut checks failed: {string.Join("; ", keyFailures)}");
                Pass("every default shortcut, Escape on Settings, the library and the drawer, Enter and Escape on the resume prompt");

                // ---- The mouse on the video (gap 4), default settings ----
                _settings.ResetAll();
                _settings.Update(s => s with { ResumePlayback = false });
                await Open(gop);
                Click(1);
                await Task.Delay(800);
                Require(_presentation.IsPlaying && WindowState != WindowState.FullScreen, "a single click does nothing by default");
                Click(1);
                Click(2);
                await WaitFor(() => WindowState == WindowState.FullScreen && _chromeVisibility.IsFullscreen, "a double click goes fullscreen by default", 3);
                Click(1);
                Click(2);
                await WaitFor(() => WindowState != WindowState.FullScreen && !_chromeVisibility.IsFullscreen, "a second double click leaves fullscreen", 3);
                Require(_presentation.IsPlaying, "double clicks leave playback alone");
                Pass("double click toggles fullscreen, a single click does nothing (defaults)");
                _settings.Update(s => s with { ClickAction = "PlayPause" });
                Click(1);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "Single click Play/Pause pauses after the double-click time", 3);
                Click(1);
                await WaitFor(() => _presentation.IsPlaying, "and plays again", 3);
                Pass("the single click action");
                _presentation.TogglePictureInPictureCommand.Execute(null);
                await WaitFor(() => _presentation.IsPictureInPicture && Math.Abs(ClientSize.Width - 480) < 2, "picture in picture");
                Click(1);
                await Task.Delay(800);
                Require(_presentation.IsPictureInPicture && _presentation.IsPlaying, "a single click in picture in picture does nothing");
                Click(1);
                Click(2);
                await WaitFor(() => !_presentation.IsPictureInPicture, "a double click leaves picture in picture", 3);
                Require(_presentation.IsPlaying && WindowState != WindowState.FullScreen, "and goes back to the window, still playing");
                Pass("double click leaves picture in picture");
                await Task.Delay(500);
                double volume = _presentation.Volume;
                Wheel(0, 1);
                await WaitFor(() => Math.Abs(_presentation.Volume - (volume + _settings.Current.VolumeStep)) < .5, $"the wheel raises the volume by its step ({volume} -> {_presentation.Volume})", 3);
                Wheel(0, -1);
                await WaitFor(() => Math.Abs(_presentation.Volume - volume) < .5, $"and lowers it ({_presentation.Volume})", 3);
                double position = Elapsed();
                Wheel(-1, 0);
                await WaitFor(() => Math.Abs(Elapsed() - position - _settings.Current.VeryShortSkipSeconds) < 1.5, $"tilting right seeks by the very short skip ({position:0.0} -> {Elapsed():0.0})", 5);
                Pass("the wheel through the window, with the default actions");

                // ---- The drawer's sync controls, Add audio file, titles and chapters (gap 5) ----
                await Open(languages);
                await WaitFor(() => _presentation.CanAdjustDelays && _presentation.AudioTracks.Count > 1 && _presentation.SubtitleTracks.Count > 1, "languages.mkv lists its tracks");
                _presentation.ToggleMediaControlsCommand.Execute("Subtitles");
                await Settle();
                async Task Delay(string button, Func<int> delay, int expected)
                {
                    await Press(Named(QueueDrawer, button), button);
                    await WaitFor(() => delay() == expected, $"{button}: VLC reads back {expected} ms ({delay()} ms)", 5);
                }
                int Subtitle() => VideoSurface.MediaCapabilities.Delays.SubtitleMilliseconds;
                int Audio() => VideoSurface.MediaCapabilities.Delays.AudioMilliseconds;
                await Delay("Increase subtitle delay by 100 milliseconds", Subtitle, 100);
                await Delay("Decrease subtitle delay by 100 milliseconds", Subtitle, 0);
                await Delay("Decrease subtitle delay by 100 milliseconds", Subtitle, -100);
                Require(_presentation.SubtitleDelayMilliseconds == -100 && (int)(SubtitleDelayInput.Value ?? 0) == -100, $"the drawer shows -100 ms ({SubtitleDelayInput.Value})");
                await Delay("Reset subtitle delay", Subtitle, 0);
                Pass("subtitle delay −100, +100 and Reset");
                _presentation.DrawerTab = "Audio";
                await Settle();
                await Delay("Increase audio delay by 100 milliseconds", Audio, 100);
                await Delay("Reset audio delay", Audio, 0);
                Pass("audio delay Reset");
                int audioTracks = _presentation.AudioTracks.Count(track => !track.IsDisabled);
                _mediaPicker.PicksForSmoke.Enqueue(audio);
                await Press(Named(QueueDrawer, "Add audio file"), "Add audio file");
                await WaitFor(() => _mediaPicker.RequestsForSmoke[^1] == "audio" && _presentation.MediaControlsNotice.StartsWith("Added for the next media open", StringComparison.Ordinal), $"Add audio file takes the picked file for the next open ({_presentation.MediaControlsNotice})", 5);
                // The next open of this item (Stop, then Play restarts it) has the file as a track.
                _presentation.StopCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Stopped, "stopped");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.IsPlaying && _presentation.AudioTracks.Count(track => !track.IsDisabled) == audioTracks + 1,
                    $"the next open has one more audio track ({audioTracks} -> {_presentation.AudioTracks.Count(track => !track.IsDisabled)}: {string.Join(", ", _presentation.AudioTracks.Select(track => track.Label))})");
                Pass("Add audio file, as a track of the next open");
                _presentation.IsQueueOpen = false;

                await Open(chapters);
                await WaitFor(() => _presentation.Chapters.Count == 3, $"chapters.mkv lists three chapters ({_presentation.Chapters.Count})");
                _presentation.ToggleMediaControlsCommand.Execute("Chapters");
                await Settle();
                Button ChapterRow(int index) => ChapterScroll.GetVisualDescendants().OfType<Button>().Single(button => button.DataContext is MediaChapter chapter && chapter.Id == _presentation.Chapters[index].Id);
                await Press(ChapterRow(1), "the chapter row Part 2");
                await WaitFor(() => Elapsed() >= 9.5 && Elapsed() < 16 && _presentation.Chapters[1].IsSelected, $"Part 2 plays from 10 s ({Elapsed():0.0} s, selected {string.Join(", ", _presentation.Chapters.Where(c => c.IsSelected).Select(c => c.Label))})", 5);
                await Press(ChapterRow(2), "the chapter row Part 3");
                await WaitFor(() => Elapsed() >= 19.5 && _presentation.Chapters[2].IsSelected, $"Part 3 plays from 20 s ({Elapsed():0.0} s)", 5);
                await Press(ChapterRow(0), "the chapter row Part 1");
                await WaitFor(() => Elapsed() < 6 && _presentation.Chapters[0].IsSelected, $"Part 1 plays from the start ({Elapsed():0.0} s)", 5);
                Pass("chapter rows");
                // A file has one title at most; selecting it keeps it.
                Console.WriteLine($"[interactions-smoke] chapters.mkv titles: {string.Join(", ", _presentation.Titles.Select(title => $"{title.Label}{(title.IsSelected ? "*" : "")}"))}");
                if (_presentation.Titles.Count > 0)
                {
                    MediaTitle title = _presentation.Titles[0];
                    await Press(ChapterScroll.GetVisualDescendants().OfType<Button>().Single(button => button.DataContext is MediaTitle row && row.Id == title.Id), $"the title row {title.Label}");
                    await WaitFor(() => _presentation.Titles.Any(row => row.Id == title.Id && row.IsSelected) && _presentation.IsPlaying, "the selected title stays selected and plays", 5);
                    Pass($"the title row ({_presentation.Titles.Count} title)");
                }
                _presentation.IsQueueOpen = false;

                _presentation.StopCommand.Execute(null);
                _settings.ResetAll();
                Console.WriteLine($"[interactions-smoke] PASS {passed.Count} checks: {string.Join("; ", passed)}");
                desktop.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[interactions-smoke] FAIL after {passed.Count} checks: {error}");
                desktop.Shutdown(1);
            }
        };
    }
}
#endif // SMOKE

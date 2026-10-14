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
            // Shortcuts dropped because a button had the focus (ShortcutFocusPolicy).
            List<string> buttonFindings = [];
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
                // The queue holds file URIs; the checks compare paths.
                static string Local(string source) => Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : source;
                string? CurrentSource() => _presentation.QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source is { } source ? Local(source) : null;
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
                // A shortcut pressed where the focus is. ShortcutFocusPolicy keeps
                // shortcuts away from a focused button: such a miss is a finding,
                // and the key is pressed again on the video area to check its effect.
                async Task Shortcut(PlayerCommandId command, Func<Task<bool>> effect, string what)
                {
                    string gesture = EffectiveShortcuts()[command];
                    KeyGesture key = MenuShortcuts.ToKeyGesture(gesture) ?? throw new InvalidOperationException($"{gesture} has no key");
                    await Settle();
                    Control focus = Focused() ?? VideoArea;
                    await Key(key.Key, key.KeyModifiers);
                    if (await effect()) return;
                    if (!ShortcutFocusPolicy.Suppresses(focus)) { KeyCheck(false, $"{gesture} {what} (focus {Describe(focus)})"); return; }
                    buttonFindings.Add($"{gesture} ({what}) on {focus.GetType().Name}#{focus.Name}{(OpenMenu.IsVisible ? ", which opened the More menu" : "")}");
                    Console.WriteLine($"[interactions-smoke] finding: {gesture} does nothing with the focus on {Describe(focus)}");
                    if (OpenMenu.IsVisible) CloseOpenMenu(restoreFocus: false);
                    await Settle();
                    VideoArea.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key.Key, KeyModifiers = key.KeyModifiers, Source = VideoArea });
                    KeyCheck(await effect(), $"{gesture} {what}, pressed on the video area");
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

                // Drag and drop on the window: local files and text (a link from a browser) are accepted, nothing else is.
                DragEventArgs over = Drag(DragDrop.DragOverEvent, await Files(dark));
                VideoArea.RaiseEvent(over);
                DataTransfer text = new();
                text.Add(DataTransferItem.CreateText("https://example.invalid/clip.mp4"));
                DragEventArgs overText = Drag(DragDrop.DragOverEvent, text);
                VideoArea.RaiseEvent(overText);
                DragEventArgs overEmpty = Drag(DragDrop.DragOverEvent, new DataTransfer());
                VideoArea.RaiseEvent(overEmpty);
                Require(over.Handled && over.DragEffects == DragDropEffects.Copy && overText.Handled && overText.DragEffects == DragDropEffects.Copy && overEmpty.Handled && overEmpty.DragEffects == DragDropEffects.None, $"file and text drags are accepted as a copy, an empty drag is refused ({over.DragEffects}, {overText.DragEffects}, {overEmpty.DragEffects})");
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
                    .First(button => button.IsEffectivelyVisible && button.DataContext is QueueItem item && string.Equals(Local(item.Source), source, StringComparison.OrdinalIgnoreCase) && ToolTip.GetTip(button) as string == tip);
                bool InOrder(params string[] paths) => _presentation.QueueItems.Select(item => Local(item.Source)).SequenceEqual(paths, StringComparer.OrdinalIgnoreCase);
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
                await Shortcut(PlayerCommandId.TogglePause, () => Within(() => _presentation.IsPlaying, 3), "plays");
                await Shortcut(PlayerCommandId.TogglePause, () => Within(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, 3), "pauses");
                foreach ((PlayerCommandId command, int seconds) in new[]
                {
                    (PlayerCommandId.SkipForward, 5), (PlayerCommandId.SkipBackward, -4), (PlayerCommandId.SkipForwardVeryShort, 2), (PlayerCommandId.SkipBackwardVeryShort, -2),
                    (PlayerCommandId.SkipForwardMedium, 6), (PlayerCommandId.SkipBackwardMedium, -6), (PlayerCommandId.SkipForwardLong, 8), (PlayerCommandId.SkipBackwardLong, -8),
                })
                {
                    double before = Elapsed();
                    await Shortcut(command, () => Within(() => Math.Abs(Elapsed() - before - seconds) < 1.2, 5), $"moves {seconds} s from {before:0.0} s");
                }
                bool muted = _presentation.IsMuted;
                await Shortcut(PlayerCommandId.ToggleMute, () => Within(() => _presentation.IsMuted != muted, 3), "mutes");
                await Shortcut(PlayerCommandId.ToggleMute, () => Within(() => _presentation.IsMuted == muted, 3), "unmutes");
                await Shortcut(PlayerCommandId.ToggleFullscreen, () => Within(() => WindowState == WindowState.FullScreen, 3), "goes fullscreen");
                await Shortcut(PlayerCommandId.ToggleFullscreen, () => Within(() => WindowState != WindowState.FullScreen, 3), "leaves fullscreen");
                if (WindowState == WindowState.FullScreen) ToggleFullscreen();
                await Shortcut(PlayerCommandId.ToggleFullscreen, () => Within(() => WindowState == WindowState.FullScreen, 3), "goes fullscreen again");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(await Within(() => WindowState != WindowState.FullScreen, 3), $"Escape leaves fullscreen (focus {Describe(Focused())})");
                if (WindowState == WindowState.FullScreen) ToggleFullscreen();
                int requests = _mediaPicker.RequestsForSmoke.Count;
                await Shortcut(PlayerCommandId.OpenMedia, () => Within(() => _mediaPicker.RequestsForSmoke.Count > requests && _mediaPicker.RequestsForSmoke[^1] == "media", 3), "asks for a file");
                requests = _mediaPicker.RequestsForSmoke.Count;
                await Shortcut(PlayerCommandId.OpenFolder, () => Within(() => _mediaPicker.RequestsForSmoke.Count > requests && _mediaPicker.RequestsForSmoke[^1] == "folder", 3), "asks for a folder");
                Require(Plays(gop) || _presentation.PlaybackStatus == PlaybackStatus.Paused, "cancelled pickers leave gop.mp4 open");
                await Shortcut(PlayerCommandId.OpenSettings, () => Task.FromResult(SettingsPanel.IsVisible), "opens Settings");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!SettingsPanel.IsVisible, $"Escape closes Settings (focus {Describe(Focused())})");
                if (SettingsPanel.IsVisible) CloseSettings();
                await Shortcut(PlayerCommandId.OpenLibrary, () => Task.FromResult(_presentation.IsLibraryOpen), "opens the library drawer");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!_presentation.IsLibraryOpen, $"Escape closes the library drawer (focus {Describe(Focused())})");
                if (_presentation.IsLibraryOpen) CloseLibrary();
                await Shortcut(PlayerCommandId.ToggleQueue, () => Task.FromResult(_presentation.IsQueueOpen && _presentation.DrawerTab == "Queue"), "opens the queue");
                await Key(Avalonia.Input.Key.Escape);
                KeyCheck(!_presentation.IsQueueOpen, $"Escape closes the queue (focus {Describe(Focused())})");
                if (_presentation.IsQueueOpen) CloseDrawer();
                await Shortcut(PlayerCommandId.MediaInformation, () => Task.FromResult(_presentation.IsQueueOpen && _presentation.DrawerTab == "Information"), "opens Media Information");
                await Key(Avalonia.Input.Key.Escape);
                if (_presentation.IsQueueOpen) CloseDrawer();
                await Shortcut(PlayerCommandId.Snapshot, () => Task.FromResult(_presentation.MediaControlsNotice.Contains("Snapshot", StringComparison.Ordinal)), "answers with the snapshot notice");
                await Shortcut(PlayerCommandId.TogglePictureInPicture, () => Within(() => _presentation.IsPictureInPicture, 3), "enters picture in picture");
                await Shortcut(PlayerCommandId.TogglePictureInPicture, () => Within(() => !_presentation.IsPictureInPicture, 3), "leaves picture in picture");
                if (_presentation.IsPictureInPicture) ExitPictureInPicture();
                await Shortcut(PlayerCommandId.ShowMessages, () => Within(() => _messagesWindow?.IsVisible == true, 3), "opens Messages");
                _messagesWindow?.Close();
                Activate();
                await Shortcut(PlayerCommandId.ShowHome, () => Task.FromResult(_presentation.IsHomeOpen), "shows Home");
                _presentation.ShowPlayerCommand.Execute(null);
                await Settle();

                // Open Network Stream: Enter in the URL field opens it, or keeps the field with the error.
                using (MediaServer server = new(gop))
                {
                    await Shortcut(PlayerCommandId.OpenUrl, async () => { await Settle(); return UrlPanel.IsVisible && UrlTextBox.IsFocused; }, "opens Open Network Stream with the focus in the address");
                    if (!UrlPanel.IsVisible) ShowUrlPanel();
                    await Settle();
                    UrlTextBox.Focus();
                    UrlTextBox.Text = "gopher://example.invalid/clip.mp4";
                    await Key(Avalonia.Input.Key.Enter);
                    Require(UrlPanel.IsVisible && _presentation.HasOpenError && UrlTextBox.IsFocused, $"Enter on an unsupported address keeps the dialog with its error ({_presentation.OpenError})");
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
                    // Stop once the player shows the new position, as a user would.
                    bool AtMiddle() => Math.Abs(Elapsed() - 15) < 1.5 && Math.Abs(_presentation.Position - .5) < .05;
                    Require(await Within(AtMiddle, 15), $"the player shows 15 s ({_presentation.ElapsedText}, engine at {Elapsed():0.0} s, position {_presentation.Position:0.00}, {_presentation.PlaybackStatus}, {Engine().Source})");
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
                Pass($"every default shortcut, Escape on Settings, the library and the drawer, Enter and Escape on the resume prompt ({buttonFindings.Count} presses dropped on a focused button, then run from the video area)");

                // ---- The mouse on the video (gap 4), default settings ----
                _settings.ResetAll();
                _settings.Update(s => s with { ResumePlayback = false });
                await Open(gop);
                Click(1);
                await Task.Delay(800);
                Require(_presentation.IsPlaying && WindowState != WindowState.FullScreen, $"a single click does nothing by default (status {_presentation.PlaybackStatus}, window {WindowState}, chrome fullscreen {_chromeVisibility.IsFullscreen})");
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
                // While the media plays, the file opens at once as one more audio track, as in VLC.
                int Tracks() => _presentation.AudioTracks.Count(track => !track.IsDisabled);
                string TrackList() => string.Join(", ", _presentation.AudioTracks.Select(track => track.Label));
                await WaitFor(() => _mediaPicker.RequestsForSmoke[^1] == "audio" && _presentation.MediaControlsNotice == "Added to the playing media.", $"Add audio file adds the picked file to the playing media ({_presentation.MediaControlsNotice})", 5);
                await WaitFor(() => _presentation.IsPlaying && Tracks() == audioTracks + 1, $"the playing media has one more audio track ({audioTracks} -> {Tracks()}: {TrackList()})");
                Pass("Add audio file, as a live track");
                // Stop starts a new VLC core on Play, without the item's added track: say what it does.
                _presentation.StopCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Stopped, "stopped");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.IsPlaying && _presentation.CanAdjustDelays && _presentation.AudioTracks.Count > 0, "playing again after Stop");
                await Task.Delay(1000);
                Console.WriteLine($"[interactions-smoke] finding: after Stop and Play, languages.mkv has {Tracks()} audio tracks ({TrackList()}); the added file is {(Tracks() == audioTracks + 1 ? "kept" : "gone")}");
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
                Console.WriteLine($"[interactions-smoke] button focus finding: {buttonFindings.Count} shortcut presses did nothing with the focus on a button: {string.Join("; ", buttonFindings)}");
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

#if SMOKE
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Interop.Windows;
using Sample = Conic.Interop.Windows.ScreenSampler.Sample;

namespace Conic;

// --interface-settings-smoke (phase 9d): the Subtitles, Controls, Interface
// and Library settings take effect in a real VLC session. Each case changes
// settings through the settings service, as the Settings page does, and
// plays a fixture from artifacts/settings-fixtures
// (eng/make-settings-fixtures.ps1). Subtitles are checked on screen (black
// video, so the text is all that shows), through the playing core's
// variables and VLC's log; the window, mouse, keyboard and queue rows
// through the window, the engine state and the queue. Keep window above
// other windows is on in every case, so the pixel checks see the window.
// Windows only.
public partial class MainWindow
{
    internal void EnableInterfaceSettingsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> passed = [];
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The interface settings smoke samples the Windows desktop.");
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string Fixture(string name)
                {
                    string path = Path.Combine(fixtures, name);
                    Require(File.Exists(path), $"fixture {name} exists (run eng/make-settings-fixtures.ps1)");
                    return path;
                }
                string logs = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-interface");
                if (Directory.Exists(logs)) Directory.Delete(logs, true);
                Directory.CreateDirectory(logs);

                void Pass(string what) { passed.Add(what); Console.WriteLine($"[interface-settings-smoke] ok: {what}"); }
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                void Settings(Func<PlayerSettings, PlayerSettings> change) => _settings.Update(change);
                MediaEngineState Engine() => VideoSurface.Player!.State;
                VlcCorePlayer Core() => (VlcCorePlayer)VideoSurface.Player!;
                bool HasSubtitleTrack() => VideoSurface.MediaCapabilities.SubtitleTracks.Any(track => !track.IsDisabled && track.Id != "-1");

                // Every case starts from the defaults, resuming off, the window on top.
                string? currentLog = null, closedLog = null;
                async Task Open(string name, Func<PlayerSettings, PlayerSettings> change, string media)
                {
                    _presentation.StopCommand.Execute(null);
                    _presentation.IsHomeOpen = false;
                    _settings.ResetAll();
                    Settings(s => change(s with { ResumePlayback = false, KeepOnTop = true }));
                    currentLog = Path.Combine(logs, name + ".log");
                    closedLog = null;
                    VideoSurface.ExtraOpenArgumentsForSmoke = ["--file-logging", $"--logfile={currentLog}", "--log-verbose=2"];
                    _presentation.OpenSource(media, MediaSourceKind.File);
                    await Task.Delay(300);
                    await WaitFor(() => _presentation.IsPlaying, $"{name}: plays");
                }
                string Log()
                {
                    if (currentLog is null || !File.Exists(currentLog)) return "";
                    using FileStream stream = new(currentLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return new StreamReader(stream).ReadToEnd();
                }
                // VLC writes its log when the core stops: log checks come last in a case.
                async Task<string> ClosedLog()
                {
                    if (closedLog is not null) return closedLog;
                    if (_presentation.StopCommand.CanExecute(null)) _presentation.StopCommand.Execute(null);
                    else VideoSurface.Stop();
                    await WaitFor(() => Log().Contains("-- logger module stopped --", StringComparison.Ordinal), $"VLC closes its log {currentLog}");
                    return closedLog = Log();
                }
                async Task RequireLog(string pattern, string message) =>
                    Require(Regex.IsMatch(await ClosedLog(), pattern), $"{message} (log {currentLog} has no /{pattern}/)");
                async Task RequireNoLog(string pattern, string message) =>
                    Require(!Regex.IsMatch(await ClosedLog(), pattern), $"{message} (log {currentLog} has /{pattern}/)");

                // A horizontal band of the video on screen, from top to bottom (fractions of the picture).
                async Task<Sample> SampleBand(double top, double bottom, int delay = 600)
                {
                    await Task.Delay(delay);
                    Rect destination = VideoSurface.CurrentVideoLayout.Destination;
                    Rect band = new(destination.X, destination.Y + destination.Height * top, destination.Width, destination.Height * (bottom - top));
                    PixelPoint topLeft = VideoSurface.PointToScreen(band.TopLeft), bottomRight = VideoSurface.PointToScreen(band.BottomRight);
                    PixelRect area = new(topLeft, bottomRight);
                    Require(area.Width > 50 && area.Height > 10, $"video band on screen ({area})");
                    return ScreenSampler.Capture(area.X, area.Y, area.Width, area.Height, 160, Math.Max(4, (int)(40 * (bottom - top))));
                }
                // On black video: the cells the text lights, and how much light in all.
                static int TextCells(Sample sample) => sample.Cells.Count(c => .299 * c.R + .587 * c.G + .114 * c.B > 40);
                static double Light(Sample sample) => sample.Cells.Sum(c => .299 * c.R + .587 * c.G + .114 * c.B);
                static int ColorCells(Sample sample, Func<(double R, double G, double B), bool> match) => sample.Cells.Count(match);
                // The mean row of the lit cells (0 at the top of the band).
                static double TextRow(Sample sample)
                {
                    double weight = 0, sum = 0;
                    for (int i = 0; i < sample.Cells.Length; i++)
                    {
                        (double r, double g, double b) = sample.Cells[i];
                        double luma = .299 * r + .587 * g + .114 * b;
                        weight += luma; sum += luma * (i / sample.Columns);
                    }
                    return weight == 0 ? double.NaN : sum / weight;
                }

                string dark = Fixture("dark.mp4");
                Fixture("dark.srt");

                // ---- Subtitles › Loading and Appearance (live) ----
                await Open("subtitles-default", s => s, dark);
                await WaitFor(HasSubtitleTrack, "dark.srt next to dark.mp4 is loaded automatically");
                Sample plain = await SampleBand(.6, 1, 1500);
                Sample plainTall = await SampleBand(.4, 1, 0);
                Console.WriteLine($"[interface-settings-smoke] default subtitles {plain} text cells {TextCells(plain)}");
                Require(TextCells(plain) > 10, $"the subtitle is drawn ({TextCells(plain)} lit cells)");
                Pass("Load subtitle files automatically (on by default) loads and draws dark.srt");
                // The live style reaches the playing core from its next line (one a second).
                Settings(s => s with { SubtitleColor = "#FFFF00" });
                Sample yellow = await SampleBand(.6, 1, 1800);
                int yellowCells = ColorCells(yellow, c => c.R > 60 && c.G > 60 && c.B < c.R / 2);
                Require(Core().ReadSubtitleVariablesForSmoke()["freetype-color"] == 0xFFFF00 && yellowCells > 10, $"Text color turns the playing subtitles yellow ({yellowCells} yellow cells)");
                Pass($"Text color, live ({yellowCells} yellow cells)");
                Settings(s => s with { SubtitleTextScale = 200 });
                Sample large = await SampleBand(.4, 1, 1800);
                Sample normalSize = plainTall;
                Require(Core().ReadSubtitleVariablesForSmoke()["sub-text-scale"] == 200 && Light(large) > Light(normalSize) * 1.5,
                    $"Font size 200 % enlarges the playing subtitles ({Light(normalSize):0} -> {Light(large):0})");
                Pass($"Font size, live ({Light(normalSize):0} -> {Light(large):0})");
                Settings(s => s with { SubtitleBackground = true, SubtitleBackgroundColor = "#0000FF", SubtitleBackgroundOpacity = 100 });
                Sample boxed = await SampleBand(.4, 1, 1800);
                int blueCells = ColorCells(boxed, c => c.B > 100 && c.B > c.R + 50 && c.B > c.G + 50);
                IReadOnlyDictionary<string, long> live = Core().ReadSubtitleVariablesForSmoke();
                Require(live["freetype-background-opacity"] == 255 && live["freetype-background-color"] == 0x0000FF && blueCells > 10, $"Background puts a blue box behind the playing subtitles ({blueCells} blue cells)");
                Pass($"Background, color and opacity, live ({blueCells} blue cells)");
                Settings(s => s with { SubtitleOutline = "Thick" });
                await Task.Delay(1200);
                Require(Core().ReadSubtitleVariablesForSmoke()["freetype-outline-thickness"] == SubtitleEngineOptions.OutlineThickness("Thick"), "Outline thickness reaches the playing core");
                Pass("Outline thickness, live");

                // ---- Subtitles › Appearance (next open) ----
                await Open("subtitles-half", s => s with { SubtitleOpacity = 50 }, dark);
                Sample half = await SampleBand(.6, 1, 1500);
                Require(Light(half) < Light(plain) * .75 && TextCells(half) > 0, $"Text opacity 50 % dims the subtitles ({Light(plain):0} -> {Light(half):0})");
                Pass($"Text opacity ({Light(plain):0} -> {Light(half):0})");
                await Open("subtitles-bold", s => s with { SubtitleBold = true }, dark);
                Sample bold = await SampleBand(.6, 1, 1500);
                Require(Light(bold) > Light(plain) * 1.1, $"Bold thickens the subtitles ({Light(plain):0} -> {Light(bold):0})");
                Pass($"Bold ({Light(plain):0} -> {Light(bold):0})");
                await Open("subtitles-font", s => s with { SubtitleFont = "Courier New" }, dark);
                Sample courier = await SampleBand(.6, 1, 1500);
                Require(courier.Difference(plain) > 1, $"Font Courier New changes the subtitles (difference {courier.Difference(plain):0.00})");
                await RequireLog("(?i)courier new", "VLC's text renderer is asked for Courier New");
                Pass($"Font (difference {courier.Difference(plain):0.00})");
                await Open("subtitles-margin", s => s with { SubtitleMargin = 120 }, dark);
                Sample raised = await SampleBand(.4, 1, 1500);
                Require(TextRow(raised) < TextRow(plainTall) - 1, $"Distance from the bottom raises the subtitles (row {TextRow(plainTall):0.0} -> {TextRow(raised):0.0})");
                Pass($"Distance from the bottom (row {TextRow(plainTall):0.0} -> {TextRow(raised):0.0})");
                // The rest of the style: the options the new core starts with, as its text renderer inherits them.
                await Open("subtitles-style", s => s with
                {
                    SubtitleOutlineColor = "#FF0000", SubtitleShadowColor = "#00FF00", SubtitleShadowDistance = "Large", SubtitleOpacity = 80,
                }, dark);
                IReadOnlyDictionary<string, long> style = Core().ReadIntegerVariablesForSmoke("freetype-outline-color", "freetype-shadow-color", "freetype-opacity", "freetype-shadow-opacity");
                Require(style["freetype-outline-color"] == 0xFF0000 && style["freetype-shadow-color"] == 0x00FF00 && style["freetype-opacity"] == SubtitleEngineOptions.Opacity(80),
                    $"Outline color, shadow color and opacity reach the new core ({string.Join(", ", style.Select(pair => $"{pair.Key}={pair.Value}"))})");
                Pass("Outline color and shadow color");
                await Open("subtitles-no-shadow", s => s with { SubtitleShadow = false }, dark);
                Require(Core().ReadIntegerVariablesForSmoke("freetype-shadow-opacity")["freetype-shadow-opacity"] == 0, "Shadow off sets the shadow's opacity to 0");
                Pass("Shadow");

                // ---- Subtitles › Tracks and Loading ----
                await Open("subtitles-disabled", s => s with { SubtitlesEnabled = false }, dark);
                Sample none = await SampleBand(.6, 1, 2000);
                Require(TextCells(none) == 0 && !VideoSurface.MediaCapabilities.SubtitleTracks.Any(track => track.IsSelected && track.Id != "-1"), $"Enable subtitles off shows none ({TextCells(none)} lit cells)");
                Pass("Enable subtitles");
                await Open("subtitles-no-autoload", s => s with { SubtitleAutoload = false }, dark);
                await Task.Delay(1500);
                Require(!HasSubtitleTrack(), "Load subtitle files automatically off loads no dark.srt");
                Pass("Load subtitle files automatically off");
                string film = Fixture(Path.Combine("matching", "film.mp4"));
                await Open("matching-any", s => s with { SubtitleMatching = 1 }, film);
                await WaitFor(HasSubtitleTrack, "File name matching \"Any subtitle file\" loads unrelated.srt");
                await Open("matching-exact", s => s with { SubtitleMatching = 4 }, film);
                await Task.Delay(1500);
                Require(!HasSubtitleTrack(), "File name matching \"Exact title\" does not load unrelated.srt");
                Pass("File name matching");
                string cyrillic = Fixture("cyrillic.mp4");
                await Open("encoding-automatic", s => s, cyrillic);
                await Task.Delay(1500);
                await RequireLog("invalid UTF-8 sequence", "Automatic tries UTF-8 on the Windows-1251 file first");
                await Open("encoding-1251", s => s with { SubtitleEncoding = "Windows-1251" }, cyrillic);
                await Task.Delay(1500);
                await RequireLog("trying configured character encoding: Windows-1251", "Text encoding Windows-1251 reaches the subtitle decoder");
                await RequireNoLog("failed to convert subtitle encoding|invalid UTF-8 sequence", "Windows-1251 reads the file without errors");
                Pass("Text encoding");

                // ---- Subtitles › Timing and On-Screen Messages ----
                await Open("subtitle-delay", s => s with { SubtitleDelay = 2000 }, dark);
                await WaitFor(() => VideoSurface.MediaCapabilities.Delays.SubtitleMilliseconds == 2000, $"Subtitle delay 2000 ms is the new media's delay ({VideoSurface.MediaCapabilities.Delays.SubtitleMilliseconds} ms)");
                Pass("Default subtitle delay");
                await Open("file-name-off", s => s with { SubtitleAutoload = false }, dark);
                Sample untitled = await SampleBand(0, 1, 500);
                await Open("file-name-on", s => s with { SubtitleAutoload = false, ShowFileNameOnStart = true, FileNameSeconds = 2 }, dark);
                Sample titled = await SampleBand(0, 1, 300);
                Sample later = await SampleBand(0, 1, 3000);
                Console.WriteLine($"[interface-settings-smoke] file name: off {TextCells(untitled)}, on {TextCells(titled)}, after 2 s {TextCells(later)} lit cells");
                Require(TextCells(untitled) == 0 && TextCells(titled) > 5 && TextCells(later) == 0, "Show file name when a video starts shows the title for the time set");
                Pass("Show file name when a video starts, for 2 s");

                // ---- Controls › Skip Amounts and On-Screen Messages › shortcut feedback ----
                string gop = Fixture("gop.mp4");
                await Open("skips", s => s with { VeryShortSkipSeconds = 3, MediumSkipSeconds = 7, LongSkipSeconds = 12 }, gop);
                await Task.Delay(1000);
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "skips: paused");
                async Task Key(Key key, KeyModifiers modifiers, double expected, string label)
                {
                    double before = Engine().Elapsed.TotalSeconds;
                    OnPlayerKeyDown(this, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = VideoSurface });
                    await WaitFor(() => Math.Abs(Engine().Elapsed.TotalSeconds - before - expected) < 1.5, $"{label} moves {expected} s ({before:0.0} -> {Engine().Elapsed.TotalSeconds:0.0})", 5);
                }
                await Key(Avalonia.Input.Key.Right, KeyModifiers.Control, 7, "Medium skip (Ctrl+Right)");
                Require(ShortcutFeedback.IsVisible && ShortcutFeedbackText.Text == "+7 s", $"the skip is shown over the video ({ShortcutFeedbackText.Text})");
                await WaitFor(() => !ShortcutFeedback.IsVisible, "the feedback goes after its time", 3);
                await Key(Avalonia.Input.Key.Left, KeyModifiers.Shift, -3, "Very short skip (Shift+Left)");
                await Key(Avalonia.Input.Key.Right, KeyModifiers.Control | KeyModifiers.Alt, 12, "Long skip (Ctrl+Alt+Right)");
                Pass("Very short, medium and long skips, with their feedback");
                Settings(s => s with { ShortcutFeedback = false });
                await Key(Avalonia.Input.Key.Left, KeyModifiers.Control, -7, "Medium skip back");
                Require(!ShortcutFeedback.IsVisible, "Show feedback for shortcuts off shows nothing");
                Pass("Show feedback for shortcuts off");

                // ---- Controls › Mouse ----
                Point VideoCenter() => VideoSurface.TranslatePoint(new Point(VideoSurface.Bounds.Width / 2, VideoSurface.Bounds.Height / 2), this)!.Value;
                void Wheel(double x, double y)
                {
                    Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    VideoSurface.RaiseEvent(new PointerWheelEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(x, y)));
                }
                void Click(int count)
                {
                    Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    VideoSurface.RaiseEvent(new PointerPressedEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, count));
                    VideoSurface.RaiseEvent(new PointerReleasedEventArgs(VideoSurface, pointer, this, VideoCenter(), 0,
                        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
                    pointer.Capture(null);
                }
                Settings(s => s with { VolumeStep = 5 });
                double volume = _presentation.Volume;
                Wheel(0, 1);
                await WaitFor(() => Math.Abs(_presentation.Volume - (volume + 5)) < .5, $"Scroll wheel up raises the volume by the volume step ({volume} -> {_presentation.Volume})", 3);
                Settings(s => s with { ReverseWheel = true });
                Wheel(0, 1);
                await WaitFor(() => Math.Abs(_presentation.Volume - volume) < .5, $"Reverse scroll direction: wheel up lowers it ({_presentation.Volume})", 3);
                Pass("Scroll wheel: volume, and reversed");
                Settings(s => s with { ReverseWheel = false, WheelAction = "Seek", HorizontalWheelAction = "Volume" });
                double position = Engine().Elapsed.TotalSeconds;
                Wheel(0, 1);
                await WaitFor(() => Math.Abs(Engine().Elapsed.TotalSeconds - position - 3) < 1.5, $"Scroll wheel Seek moves by the very short skip ({position:0.0} -> {Engine().Elapsed.TotalSeconds:0.0})", 5);
                volume = _presentation.Volume;
                // Avalonia reports a move to the right as a negative X.
                Wheel(-1, 0);
                await WaitFor(() => Math.Abs(_presentation.Volume - (volume + 5)) < .5, $"Horizontal scroll right raises the volume ({volume} -> {_presentation.Volume})", 3);
                Pass("Scroll wheel Seek and horizontal scroll");
                Settings(s => s with { ClickAction = "PlayPause", DoubleClickAction = "None" });
                Click(1);
                await WaitFor(() => _presentation.IsPlaying, "a single click plays at once without a double-click action", 3);
                Settings(s => s with { DoubleClickAction = "Fullscreen" });
                Click(1);
                await Task.Delay(100);
                Require(_presentation.IsPlaying, "with a double-click action, a single click waits out the double-click time");
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "then pauses", 3);
                Click(1);
                Click(2);
                await WaitFor(() => _chromeVisibility.IsFullscreen, "a double click goes fullscreen", 3);
                await Task.Delay(1000);
                Require(_presentation.PlaybackStatus == PlaybackStatus.Paused, "the double click's first click does not play");
                ToggleFullscreen();
                await WaitFor(() => !_chromeVisibility.IsFullscreen, "fullscreen left");
                Pass("Single and double click on the video");

                // ---- Interface › Window ----
                Require(Topmost && _presentation.IsAlwaysOnTop, "Keep window above other windows on keeps the window on top");
                _presentation.IsAlwaysOnTop = false;
                Require(!Topmost && !_settings.Current.KeepOnTop, "Always on Top in the More menu writes the setting back");
                Settings(s => s with { KeepOnTop = true });
                Require(Topmost, "Keep window above other windows applies live");
                Pass("Keep window above other windows, live and from the More menu");
                Require(_presentation.TitleBarText == _presentation.MediaTitle && _presentation.TitleBarText.Contains("gop", StringComparison.OrdinalIgnoreCase), $"the title bar shows the file name ({_presentation.TitleBarText})");
                Settings(s => s with { ShowFileNameInTitle = false });
                Require(_presentation.TitleBarText == "Conic", $"Show the file name in the title bar off shows the app's name ({_presentation.TitleBarText})");
                Pass("Show the file name in the title bar");
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.IsPlaying, "playing again");
                Settings(s => s with { PauseWhenMinimized = true });
                WindowState = WindowState.Minimized;
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "Pause video when window is minimized pauses it");
                WindowState = WindowState.Normal;
                await WaitFor(() => _presentation.IsPlaying, "and plays again when the window comes back");
                Settings(s => s with { PauseWhenMinimized = false });
                WindowState = WindowState.Minimized;
                await Task.Delay(1500);
                Require(_presentation.IsPlaying, "off, a minimized window keeps playing");
                WindowState = WindowState.Normal;
                await Task.Delay(500);
                Pass("Pause video when window is minimized");

                // ---- Interface › Playback Controls, in fullscreen ----
                Settings(s => s with { ControlTimeoutSeconds = 1, AutoHideControls = false });
                ToggleFullscreen();
                await WaitFor(() => _chromeVisibility.IsFullscreen, "fullscreen");
                WakeChrome();
                await Task.Delay(2500);
                Require(PlayerDock.IsVisible, "Auto-hide the controls off keeps the bars up in fullscreen");
                Settings(s => s with { AutoHideControls = true });
                WakeChrome();
                await WaitFor(() => !PlayerDock.IsVisible, "Auto-hide on hides them after the timeout", 4);
                Settings(s => s with { ShowControlsInFullscreen = false });
                WakeChrome();
                await Task.Delay(200);
                Require(!PlayerDock.IsVisible && !TopChrome.IsVisible, "Show controls in fullscreen off keeps the bars away even on input");
                ToggleFullscreen();
                await WaitFor(() => !_chromeVisibility.IsFullscreen, "fullscreen left");
                await WaitFor(() => PlayerDock.IsVisible, "in a window the bars are back");
                Pass("Auto-hide the controls and Show controls in fullscreen");

                // ---- Interface › Resize window to fit video (640x360) ----
                await Open("resize-to-video", s => s with { ResizeToVideo = true }, gop);
                double expected = Math.Max(MinWidth, Math.Round(640 / RenderScaling));
                await WaitFor(() => Math.Abs(Width - expected) <= 1, $"Resize window to fit video gives the window the video's width ({Width} for {expected})");
                Pass($"Resize window to fit video ({Width:0} DIP wide)");
                Width = 1280; Height = 720;

                // ---- Library › Opening Files ----
                await Open("enqueue", s => s with { EnqueueOnOpen = true }, gop);
                await WaitFor(() => _presentation.QueueItems.Count == 1, "one item queued");
                _presentation.OpenSource(Fixture("short-1.mp4"), MediaSourceKind.File);
                await WaitFor(() => _presentation.QueueItems.Count == 2, "Add to the queue: the file joins the queue");
                Require(_presentation.QueueItems.Single(item => item.IsCurrent).Source.Contains("gop", StringComparison.OrdinalIgnoreCase) && _presentation.IsPlaying, "what played keeps playing");
                Settings(s => s with { EnqueueOnOpen = false });
                _presentation.OpenSource(Fixture("short-2.mp4"), MediaSourceKind.File);
                await WaitFor(() => _presentation.QueueItems.Count == 1 && _presentation.QueueItems[0].Source.Contains("short-2", StringComparison.OrdinalIgnoreCase), "off, the file replaces the queue");
                Pass("Add to the queue instead of replacing it");
                string folder = Path.Combine(logs, "folder");
                Directory.CreateDirectory(Path.Combine(folder, "sub"));
                File.Copy(Fixture("short-1.mp4"), Path.Combine(folder, "a.mp4"));
                File.Copy(Fixture("short-2.mp4"), Path.Combine(folder, "sub", "b.mp4"));
                string hidden = Path.Combine(folder, "c.mp4");
                File.Copy(Fixture("short-1.mp4"), hidden);
                File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
                async Task Folder(Func<PlayerSettings, PlayerSettings> change, int count, string label)
                {
                    _presentation.StopCommand.Execute(null);
                    _settings.ResetAll();
                    Settings(s => change(s with { ResumePlayback = false, KeepOnTop = true }));
                    _presentation.OpenDroppedSources([folder]);
                    await WaitFor(() => _presentation.QueueItems.Count == count, $"{label}: {count} items queued ({_presentation.QueueItems.Count})");
                }
                await Folder(s => s, 2, "a folder with its subfolders, hidden files left out");
                await Folder(s => s with { FolderOpening = "FolderOnly" }, 1, "This folder only");
                await Folder(s => s with { ShowHiddenFiles = true }, 3, "Show hidden files");
                Pass("When opening a folder and Show hidden files");

                _presentation.StopCommand.Execute(null);
                _settings.ResetAll();
                Console.WriteLine($"[interface-settings-smoke] PASS {passed.Count} checks: {string.Join("; ", passed)}");
                desktop.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[interface-settings-smoke] FAIL after {passed.Count} checks: {error}");
                desktop.Shutdown(1);
            }
        };
    }
}
#endif // SMOKE

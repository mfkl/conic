#if SMOKE
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Interop.Linux;
using Conic.Interop.MacOS;
using Conic.Interop.Windows;
using Sample = Conic.Interop.ScreenSample;

namespace Conic;

// --engine-settings-smoke (phase 9b): the Playback, Video, Performance and
// Network settings take effect in a real VLC session. Each case changes
// settings through the Settings view model, opens a fixture from
// artifacts/settings-fixtures (eng/make-settings-fixtures.ps1) and checks
// the result where it shows: VLC's own log of that core (--file-logging),
// the engine state, the window, or the pixels on screen (the composed GPU
// 11 decoding, the display request and the DPAPI proxy password are Windows
// only.
public partial class MainWindow
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    internal void EnableEngineSettingsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> passed = [];
            try
            {
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string Fixture(string name)
                {
                    string path = Path.Combine(fixtures, name);
                    Require(File.Exists(path), $"fixture {name} exists (run eng/make-settings-fixtures.ps1)");
                    return path;
                }
                string logs = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-engine");
                if (Directory.Exists(logs)) Directory.Delete(logs, true);
                Directory.CreateDirectory(logs);
                Topmost = true;

                void Pass(string what) { passed.Add(what); Console.WriteLine($"[engine-settings-smoke] ok: {what}"); }
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                void Settings(Func<PlayerSettings, PlayerSettings> change) => _settings.Update(change);
                MediaEngineState Engine() => VideoSurface.Player!.State;

                // Every case starts from the defaults, with resuming off so
                // positions saved by one case do not move the next one.
                string? currentLog = null, closedLog = null;
                string[] caseArguments = [];
                async Task Open(string name, Func<PlayerSettings, PlayerSettings> change, params string[] media)
                {
                    _presentation.StopCommand.Execute(null);
                    _presentation.IsHomeOpen = false;
                    _settings.ResetAll();
                    Settings(s => change(s with { ResumePlayback = false }));
                    currentLog = Path.Combine(logs, name + ".log");
                    closedLog = null;
                    VideoSurface.ExtraOpenArgumentsForSmoke = ["--file-logging", $"--logfile={currentLog}", "--log-verbose=2", .. caseArguments];
                    caseArguments = [];
                    if (media.Length == 1 && media[0].Contains("://", StringComparison.Ordinal)) _presentation.OpenSource(media[0], MediaSourceKind.NetworkUrl);
                    else if (media.Length == 1) _presentation.OpenSource(media[0], MediaSourceKind.File);
                    else _presentation.OpenDroppedSources(media);
                    await Task.Delay(300);
                }
                async Task OpenPlaying(string name, Func<PlayerSettings, PlayerSettings> change, string media)
                {
                    await Open(name, change, media);
                    await WaitFor(() => _presentation.IsPlaying, $"{name}: plays");
                }
                string Log()
                {
                    if (currentLog is null || !File.Exists(currentLog)) return "";
                    using FileStream stream = new(currentLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return new StreamReader(stream).ReadToEnd();
                }
                // VLC's file logger writes the log out when the core stops:
                // the log checks of a case come last and stop playback.
                async Task<string> ClosedLog()
                {
                    if (closedLog is not null) return closedLog;
                    if (_presentation.StopCommand.CanExecute(null)) _presentation.StopCommand.Execute(null);
                    else VideoSurface.Stop();
                    await WaitFor(() => Log().Contains("-- logger module stopped --", StringComparison.Ordinal), $"VLC closes its log {currentLog}", 15);
                    return closedLog = Log();
                }
                async Task WaitForLog(string pattern, string message) =>
                    Require(Regex.IsMatch(await ClosedLog(), pattern), $"{message} (log {currentLog} has no /{pattern}/)");
                async Task RequireNoLog(string pattern, string message) =>
                    Require(!Regex.IsMatch(await ClosedLog(), pattern), $"{message} (log {currentLog} has /{pattern}/)");

                // The video on screen: the middle 60 % of the picture.
                PixelRect VideoArea()
                {
                    Rect destination = VideoSurface.CurrentVideoLayout.Destination;
                    Rect inner = destination.Deflate(new Thickness(destination.Width * .2, destination.Height * .2));
                    PixelPoint topLeft = VideoSurface.PointToScreen(inner.TopLeft);
                    PixelPoint bottomRight = VideoSurface.PointToScreen(inner.BottomRight);
                    return new PixelRect(topLeft, bottomRight);
                }
                async Task<Sample> SampleVideo(int columns = 64, int rows = 36)
                {
                    await Task.Delay(600);
                    PixelRect area = VideoArea();
                    Require(area.Width > 50 && area.Height > 50, $"video area on screen ({area})");
                    return OperatingSystem.IsMacOS() ? MacScreenSampler.Capture(area, RenderScaling, columns, rows)
                        : OperatingSystem.IsWindows() ? ScreenSampler.Capture(area.X, area.Y, area.Width, area.Height, columns, rows)
                        : OperatingSystem.IsLinux() ? X11ScreenSampler.Capture(area.X, area.Y, area.Width, area.Height, columns, rows)
                        : throw new PlatformNotSupportedException("The engine settings smoke samples the screen on Windows, macOS and Linux (X11).");
                }
                // How much media VLC buffered before it started, as its log
                // reports it: "Stream buffering done (4250 ms in 3 ms)". It is
                // the caching (the input's PTS delay) plus a few frames.
                async Task<int> BufferedMilliseconds(string name, Func<PlayerSettings, PlayerSettings> change, string media)
                {
                    await OpenPlaying(name, change, media);
                    await Task.Delay(1000);
                    Match match = Regex.Match(await ClosedLog(), "Stream buffering done [(]([0-9]+) ms in");
                    Require(match.Success, $"{name}: VLC reports its buffering (log {currentLog})");
                    return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                bool windows = OperatingSystem.IsWindows();
                bool linux = OperatingSystem.IsLinux();
                // VideoToolbox is a decoder module of its own on macOS, ahead of avcodec.
                // A Mac without it (an Intel CI runner) names the one VLC uses instead.
                string macDecoder = Environment.GetEnvironmentVariable("CONIC_HARDWARE_DECODER") is { Length: > 0 } configured ? configured : "videotoolbox";
                string? linuxDecoder = Environment.GetEnvironmentVariable("CONIC_HARDWARE_DECODER") is { Length: > 0 } linuxConfigured
                    ? $"using hw decoder module \"{linuxConfigured}\"" : null;
                string? HardwareDecoder = windows ? "using hw decoder module \"d3d11va\"" : linux ? linuxDecoder : $"using video decoder module \"{macDecoder}\"";
                // VLC logs pointers as "0x11ef77460" on macOS, "000001F3A2B4" on Windows.
                const string Pointer = "[(](0x)?[0-9a-fA-F]+[)]";
                string still = Fixture("still-lowq.mp4");

                // ---- Video › Picture, with hardware decoding (Direct3D 11 adjust filter, zero copy kept) ----
                await OpenPlaying("picture-default", s => s, still);
                Sample neutral = await SampleVideo();
                if (HardwareDecoder is not null) await WaitForLog(Regex.Escape(HardwareDecoder), "hardware decoding is on by default");
                Console.WriteLine($"[engine-settings-smoke] neutral picture {neutral}");
                Require(neutral.Chroma > 25, "the fixture is colorful");
                await OpenPlaying("picture-brightness", s => s with { Brightness = 1.5 }, still);
                Sample bright = await SampleVideo();
                if (HardwareDecoder is not null) await WaitForLog(Regex.Escape(HardwareDecoder), "brightness keeps hardware decoding");
                await WaitForLog($"Filter 'adjust' {Pointer} appended to chain", "brightness adds the adjust filter to the chain");
                // On Windows the adjust filter runs on the GPU (Direct3D 11), so frames stay there.
                // On macOS VLC's GPU filters (ci_filters) need VLC.app's OpenGL
                // output: the VideoToolbox frames go through I420 to the CPU filter.
                if (windows) await WaitForLog("using video filter module \"d3d11_filters\"", "the adjust filter runs on the GPU (Direct3D 11), so frames stay there");
                else await WaitForLog("using video filter module \"adjust\"", "the adjust filter runs (on the CPU)");
                Console.WriteLine($"[engine-settings-smoke] brightness 1.5 {bright}");
                Require(bright.Luma > neutral.Luma + 15, $"Brightness 1.5 brightens the picture ({neutral.Luma:0.0} -> {bright.Luma:0.0})");
                Pass($"Brightness ({neutral.Luma:0.0} -> {bright.Luma:0.0}){(HardwareDecoder is null ? "" : " with hardware decoding")}");
                await OpenPlaying("picture-saturation", s => s with { Saturation = 0 }, still);
                Sample grey = await SampleVideo();
                Require(grey.Chroma < neutral.Chroma / 4, $"Saturation 0 removes the color ({neutral.Chroma:0.0} -> {grey.Chroma:0.0})");
                Pass($"Saturation ({neutral.Chroma:0.0} -> {grey.Chroma:0.0} chroma)");
                await OpenPlaying("picture-contrast", s => s with { Contrast = .3 }, still);
                Sample flat = await SampleVideo();
                Require(flat.Difference(neutral) > 15, $"Contrast 0.3 changes the picture ({flat.Difference(neutral):0.0})");
                Pass($"Contrast (difference {flat.Difference(neutral):0.0})");
                await OpenPlaying("picture-hue", s => s with { Hue = 120 }, still);
                Sample turned = await SampleVideo();
                double hueShift = turned.ColorDifference(neutral);
                Require(hueShift > 20 && Math.Abs(turned.Luma - neutral.Luma) < 10, $"Hue 120 turns the colors, not the brightness ({neutral} -> {turned}, color difference {hueShift:0.0})");
                Pass($"Hue (color shift {hueShift:0})");

                // ---- Performance: hardware decoding Off and Direct3D 11 ----
                await OpenPlaying("decoding-off", s => s with { HardwareDecoding = "Off" }, still);
                Sample software = await SampleVideo();
                Sample softwareFine = await SampleVideo(320, 180);
                await WaitForLog("allowing \\d+ thread\\(s\\) for decoding", "software decoding starts");
                if (windows) await WaitForLog("no hw decoder modules matched", "Off turns the hardware decoder away");
                await RequireNoLog(windows || linux ? "using hw decoder module" : "using video decoder module \"videotoolbox\"", "Off uses no hardware decoder");
                Pass("Hardware decoding Off decodes in software");
                if (windows)
                {
                    await OpenPlaying("decoding-d3d11", s => s with { HardwareDecoding = "Direct3D11" }, still);
                    await Task.Delay(1500);
                    await WaitForLog(Regex.Escape(HardwareDecoder!), "Direct3D 11 uses d3d11va");
                    Pass("Hardware decoding Direct3D 11 uses d3d11va");
                }

                // Gamma: the CPU adjust filter, so with software decoding.
                await OpenPlaying("picture-gamma", s => s with { HardwareDecoding = "Off", Gamma = 2.5 }, still);
                Sample gamma = await SampleVideo();
                Require(gamma.Difference(software) > 10, $"Gamma 2.5 changes the picture ({software.Luma:0.0} -> {gamma.Luma:0.0})");
                Pass($"Gamma with software decoding ({software.Luma:0.0} -> {gamma.Luma:0.0})");

                // Decoding threads and the loop filter (software decoding).
                await OpenPlaying("decoding-threads", s => s with { HardwareDecoding = "Off", DecodingThreads = 3 }, still);
                await Task.Delay(1500);
                await WaitForLog("allowing 3 thread\\(s\\) for decoding", "Decoding threads 3");
                Pass("Decoding threads");
                await OpenPlaying("software-again", s => s with { HardwareDecoding = "Off" }, still);
                Sample softwareAgain = await SampleVideo(320, 180);
                double noise = softwareAgain.Difference(softwareFine);
                await OpenPlaying("loop-filter", s => s with { HardwareDecoding = "Off", SkipLoopFilter = 4 }, still);
                Sample blocky = await SampleVideo(320, 180);
                Console.WriteLine($"[engine-settings-smoke] loop filter: same settings differ by {noise:0.00}, skipping it by {blocky.Difference(softwareFine):0.00}");
                Require(blocky.Difference(softwareFine) > Math.Max(.05, noise * 4), $"Skipping the loop filter changes the decoded picture ({blocky.Difference(softwareFine):0.00} vs noise {noise:0.00})");
                Pass($"Skip the H.264 loop filter (difference {blocky.Difference(softwareFine):0.00}, noise {noise:0.00})");

                // Drop late frames: at 4x with one software thread, 4K 60 fps
                // pictures reach the output late; VLC drops them only when on.
                string heavy = Fixture("heavy.mp4");
                async Task<int> LateDrops(string name, bool drop)
                {
                    caseArguments = ["--rate=4", "--no-avcodec-hurry-up"];
                    await OpenPlaying(name, s => s with { HardwareDecoding = "Off", DecodingThreads = 1, DropLateFrames = drop }, heavy);
                    await Task.Delay(3000);
                    return Regex.Matches(await ClosedLog(), "picture is too late to be displayed").Count;
                }
                int droppedOn = await LateDrops("late-frames-on", true);
                int droppedOff = await LateDrops("late-frames-off", false);
                Require(droppedOn > 10 && droppedOff == 0, $"late pictures are dropped only with Drop late frames on ({droppedOn} on, {droppedOff} off)");
                Pass($"Drop frames when the system can't keep up ({droppedOn} dropped on, {droppedOff} off)");

                // ---- Video › Deinterlacing ----
                await OpenPlaying("deinterlace-on", s => s with { Deinterlace = "On", DeinterlaceMethod = "blend" }, still);
                await Task.Delay(1500);
                await WaitForLog("deinterlace 1, mode blend", "Deinterlacing On with Blend reaches the output");
                await WaitForLog($"Filter 'deinterlace' {Pointer} appended to chain", "Deinterlacing On adds the deinterlace filter");
                if (windows) await WaitForLog("using video filter module \"d3d11_filters\"", "the deinterlacer runs on the GPU (Direct3D 11)");
                Pass("Deinterlacing On with a method");
                await OpenPlaying("deinterlace-off", s => s with { Deinterlace = "Off" }, still);
                await Task.Delay(1500);
                await WaitForLog("deinterlace 0, mode auto", "Deinterlacing Off reaches the output");
                await RequireNoLog("Adding 'deinterlace'", "Deinterlacing Off adds no deinterlacer");
                Pass("Deinterlacing Off");

                // ---- Video › Framing ----
                await OpenPlaying("aspect", s => s with { DefaultAspectRatio = "4:3" }, still);
                await WaitFor(() => _presentation.VideoAdjustments.Aspect?.Value == "4:3" && Math.Abs(VideoSurface.CurrentVideoLayout.Destination.Width / VideoSurface.CurrentVideoLayout.Destination.Height - 4d / 3) < .01, "default aspect 4:3 reaches the output");
                Pass("Default aspect ratio");
                await OpenPlaying("crop", s => s with { DefaultCrop = "1:1" }, still);
                await WaitFor(() => _presentation.VideoAdjustments.Crop?.Value == "1:1" && Math.Abs(VideoSurface.CurrentVideoLayout.Destination.Width / VideoSurface.CurrentVideoLayout.Destination.Height - 1) < .01, "default crop 1:1 reaches the output");
                Pass("Default crop");
                await OpenPlaying("zoom", s => s with { DefaultZoom = "0.5" }, still);
                await WaitFor(() => _presentation.VideoAdjustments.Zoom?.Value == "0.5" && VideoSurface.GeometryForSmoke.AutoScale == 0 && Math.Abs(VideoSurface.GeometryForSmoke.Zoom - .5) < .01, "default zoom 1:2 reaches the output");
                Pass("Default zoom");

                // ---- Video › Output ----
                await OpenPlaying("video-off", s => s with { VideoEnabled = false }, Fixture("languages.mkv"));
                await Task.Delay(1500);
                Require(_presentation.IsPlaying && !_presentation.VideoAdjustments.Available && VideoSurface.GeometryForSmoke.Active == 0, "Enable video off plays without a video output");
                Pass("Enable video off");
                await OpenPlaying("fullscreen", s => s with { StartFullscreen = true }, still);
                await WaitFor(() => WindowState == WindowState.FullScreen, "Start videos in fullscreen");
                if (windows)
                {
                    Require(DisplayRequest.DisplayRequiredForSmoke(), "the display stays on while video plays");
                    _settingsViewModel.StopScreensaver = false;
                    Require(!DisplayRequest.DisplayRequiredForSmoke(), "turning Stop the screensaver off releases the display at once");
                    _settingsViewModel.StopScreensaver = true;
                    Require(DisplayRequest.DisplayRequiredForSmoke(), "and turning it on holds it again");
                    _presentation.TogglePauseCommand.Execute(null);
                    await WaitFor(() => !_presentation.IsPlaying, "pause");
                    Require(!DisplayRequest.DisplayRequiredForSmoke(), "a paused video lets the screensaver run");
                }
                ToggleFullscreen();
                await WaitFor(() => WindowState != WindowState.FullScreen, "leave fullscreen");
                Pass(windows ? "Start videos in fullscreen; Stop the screensaver (live, released on pause)" : "Start videos in fullscreen");

                // ---- Playback › Startup ----
                await Open("start-paused", s => s with { AutoPlay = false }, still);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "Start playing off opens paused");
                await Task.Delay(1000);
                Require(_presentation.PlaybackStatus == PlaybackStatus.Paused && Engine().Elapsed < TimeSpan.FromSeconds(1), "and stays paused at the start");
                Pass("Start playing as soon as a file opens (off opens paused)");

                string gop = Fixture("gop.mp4");
                async Task<TimeSpan> SeekToMiddle(string name, Func<PlayerSettings, PlayerSettings> change)
                {
                    await OpenPlaying(name, change, gop);
                    await WaitFor(() => Engine().CanSeek && Engine().Duration > TimeSpan.Zero, $"{name}: seekable");
                    VideoSurface.Seek(.5);
                    await Task.Delay(800);
                    return Engine().Elapsed;
                }
                TimeSpan precise = await SeekToMiddle("seek-precise", s => s);
                TimeSpan fast = await SeekToMiddle("seek-fast", s => s with { SeekPrecisely = false });
                Console.WriteLine($"[engine-settings-smoke] seek to 15 s: precise {precise.TotalSeconds:0.0} s, fast {fast.TotalSeconds:0.0} s");
                Require(Math.Abs(precise.TotalSeconds - 15.5) < 1.5, $"a precise seek lands at the time asked ({precise.TotalSeconds:0.0} s)");
                Require(Math.Abs(fast.TotalSeconds - 15.5) > 3, $"a fast seek lands on a key frame ({fast.TotalSeconds:0.0} s)");
                Pass($"Seek precisely (on {precise.TotalSeconds:0.0} s, off {fast.TotalSeconds:0.0} s for 15 s)");

                // Continue where you left off: a position saved in one open.
                async Task<(TimeSpan Elapsed, bool Prompt)> Reopen(string mode)
                {
                    await OpenPlaying($"resume-save-{mode.ToLowerInvariant()}", s => s, gop);
                    Settings(s => s with { ResumePlayback = true, ResumeMinimumElapsedSeconds = 5, ResumeMaximumRemainingSeconds = 5 });
                    await WaitFor(() => Engine().CanSeek && Engine().Duration > TimeSpan.Zero, "seekable");
                    VideoSurface.Seek(.5);
                    await WaitFor(() => Engine().Elapsed > TimeSpan.FromSeconds(14), "at 15 s");
                    // Stop saves the position the player shows; let it catch up with the seek.
                    await WaitFor(() => _presentation.Position > .45, "the player shows 15 s");
                    _presentation.StopCommand.Execute(null);
                    await Task.Delay(500);
                    _settings.ResetAll();
                    _settingsViewModel.ResumeMode = _settingsViewModel.ResumeModes.Single(option => option.Value == mode);
                    Settings(s => s with { ResumeMinimumElapsedSeconds = 5, ResumeMaximumRemainingSeconds = 5 });
                    currentLog = Path.Combine(logs, $"resume-{mode.ToLowerInvariant()}.log"); closedLog = null;
                    _presentation.OpenSource(gop, MediaSourceKind.File);
                    await WaitFor(() => _presentation.IsPlaying && Engine().Duration > TimeSpan.Zero, "reopened");
                    await Task.Delay(1500);
                    return (Engine().Elapsed, _presentation.HasPendingResume);
                }
                var always = await Reopen("Always");
                Require(always.Elapsed > TimeSpan.FromSeconds(12) && !always.Prompt, $"Always resumes without asking ({always.Elapsed.TotalSeconds:0.0} s, prompt {always.Prompt})");
                _presentation.DeclineResumeCommand.Execute(null);
                var ask = await Reopen("Ask");
                Require(ask.Elapsed < TimeSpan.FromSeconds(8) && ask.Prompt, $"Ask shows the prompt ({ask.Elapsed.TotalSeconds:0.0} s, prompt {ask.Prompt})");
                _presentation.DeclineResumeCommand.Execute(null);
                var never = await Reopen("Never");
                Require(never.Elapsed < TimeSpan.FromSeconds(8) && !never.Prompt, $"Never starts from the beginning ({never.Elapsed.TotalSeconds:0.0} s, prompt {never.Prompt})");
                Pass("Continue where you left off: Always, Ask, Never");

                // ---- Playback › Tracks ----
                string languages = Fixture("languages.mkv");
                async Task<(int Audio, int Subtitle)> SelectedTracks(string name, Func<PlayerSettings, PlayerSettings> change)
                {
                    await OpenPlaying(name, change, languages);
                    await WaitFor(() => _presentation.AudioTracks.Count(t => !t.IsDisabled) == 2 && _presentation.SubtitleTracks.Count(t => !t.IsDisabled) == 2, $"{name}: two audio and two subtitle tracks");
                    await Task.Delay(500);
                    MediaTrack[] audio = _presentation.AudioTracks.Where(t => !t.IsDisabled).ToArray(), subtitles = _presentation.SubtitleTracks.Where(t => !t.IsDisabled).ToArray();
                    Console.WriteLine($"[engine-settings-smoke] {name}: audio {string.Join(", ", audio.Select(t => $"{t.Label}{(t.IsSelected ? "*" : "")}"))}; subtitles {string.Join(", ", subtitles.Select(t => $"{t.Label}{(t.IsSelected ? "*" : "")}"))}");
                    return (Array.FindIndex(audio, t => t.IsSelected), Array.FindIndex(subtitles, t => t.IsSelected));
                }
                var defaultTracks = await SelectedTracks("tracks-default", s => s);
                _settingsViewModel.AudioLanguage = "fr";
                _settingsViewModel.SubtitleLanguage = "FR, en";
                Require(_settings.Current.SubtitleLanguage == "fr,en", "language codes are normalized");
                var french = await SelectedTracks("tracks-french", s => s with { AudioLanguage = "fr", SubtitleLanguage = "fr,en" });
                Require(defaultTracks.Audio == 0 && french == (1, 1), $"Preferred languages pick the French tracks (default {defaultTracks}, French {french})");
                Pass("Preferred audio and subtitle languages");

                // ---- Playback › Order ----
                string shortA = Fixture("short-1.mp4"), shortB = Fixture("short-2.mp4");
                bool homeBeforeEnd = false;
                await Open("queue-home", s => s with { PlaybackFinished = "Home" }, shortA, shortB);
                await WaitFor(() => _presentation.QueueState.CurrentIndex == 1 && _presentation.IsPlaying, "the queue moves on to its second item", 20);
                homeBeforeEnd = _presentation.IsHomeOpen;
                await WaitFor(() => _presentation.IsHomeOpen, "When playback finishes: Go to Home, at the end of the queue", 20);
                Require(!homeBeforeEnd, "Home only opens once the whole queue has played");
                Pass("When playback finishes: Go to Home");
                await Open("stop-after-each", s => s with { StopAfterEachItem = true }, shortA, shortB);
                await WaitFor(() => _presentation.IsPlaying, "plays");
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Stopped, "Stop after each item stops after the first item", 20);
                await Task.Delay(1000);
                Require(_presentation.QueueState.CurrentIndex == 0 && !_presentation.IsPlaying && !_presentation.IsHomeOpen, $"and does not play the next one (index {_presentation.QueueState.CurrentIndex})");
                Pass("Stop after each item");
                bool quitRequested = false;
                void CancelClose(object? sender, WindowClosingEventArgs e) { e.Cancel = true; quitRequested = true; }
                Closing += CancelClose;
                await Open("quit", s => s with { PlaybackFinished = "Quit" }, shortA);
                await WaitFor(() => quitRequested, "When playback finishes: Quit VLC closes the window", 20);
                Closing -= CancelClose;
                Pass("When playback finishes: Quit VLC");

                await Open("repeat", s => s with { QueueRepeat = "All", QueueShuffle = true }, shortA, shortB);
                await WaitFor(() => _presentation.QueueState.Repeat == QueueRepeatMode.All && _presentation.QueueState.Order == QueueOrder.Random, "a new queue starts in the remembered repeat and shuffle modes");
                _settingsViewModel.QueueRepeat = _settingsViewModel.RepeatModes.Single(option => option.Value == "One");
                _settingsViewModel.QueueShuffle = false;
                await WaitFor(() => _presentation.QueueState.Repeat == QueueRepeatMode.CurrentItem && _presentation.QueueState.Order == QueueOrder.Normal, "Settings change the playing queue at once");
                _presentation.CycleRepeatCommand.Execute(null);
                _presentation.ToggleShuffleCommand.Execute(null);
                await WaitFor(() => _presentation.QueueState.Repeat == QueueRepeatMode.All && _settings.Current.QueueRepeat == "All" && _settings.Current.QueueShuffle, "the player's choice is remembered");
                Pass("Repeat and shuffle remembered (new queue, live from Settings, saved from the player)");

                // ---- Playback › Files ----
                string avi = Fixture("broken.avi");
                await OpenPlaying("avi-never", s => s, avi);
                await Task.Delay(1500);
                await WaitForLog("broken or missing index", "the fixture's index is broken");
                await RequireNoLog("Fixing AVI index", "Never plays the file as is");
                await OpenPlaying("avi-repair", s => s with { AviRepair = "WhenNeeded" }, avi);
                await Task.Delay(1500);
                await WaitForLog("Fixing AVI index", "When the index is broken repairs it");
                Pass("Repair damaged AVI files");

                // ---- Network › Buffering ----
                static bool Near(int buffered, int caching) => buffered >= caching && buffered <= caching + 600;
                int fileDefault = await BufferedMilliseconds("file-caching-normal", s => s, gop);
                int fileLowest = await BufferedMilliseconds("file-caching-lowest", s => s with { Buffering = "Lowest" }, gop);
                int fileHigher = await BufferedMilliseconds("file-caching-higher", s => s with { Buffering = "Higher" }, gop);
                int fileCustom = await BufferedMilliseconds("file-caching-custom", s => s with { Buffering = "Custom", FileCaching = 3000, NetworkCaching = 500 }, gop);
                Console.WriteLine($"[engine-settings-smoke] local files buffered: normal {fileDefault} ms, lowest {fileLowest} ms, higher {fileHigher} ms, custom 3000 {fileCustom} ms");
                Require(Near(fileDefault, 1000) && Near(fileLowest, 100) && Near(fileHigher, 5000) && Near(fileCustom, 3000), "Local files buffering follows the preset or the Custom value");
                Pass($"Buffering presets and Local files (buffered {fileLowest}/{fileDefault}/{fileHigher}/{fileCustom} ms)");
                using (MediaServer server = new(gop))
                {
                    int networkDefault = await BufferedMilliseconds("network-caching-normal", s => s, server.Url);
                    int networkCustom = await BufferedMilliseconds("network-caching-custom", s => s with { Buffering = "Custom", FileCaching = 300, NetworkCaching = 3000 }, server.Url);
                    Console.WriteLine($"[engine-settings-smoke] network streams buffered: normal {networkDefault} ms, custom 3000 {networkCustom} ms");
                    Require(Near(networkDefault, 1000) && Near(networkCustom, 3000), "Network streams buffering follows the Custom value");
                    Pass($"Network streams (buffered {networkDefault}/{networkCustom} ms)");
                }

                // ---- Network › Proxy (the password through DPAPI) ----
                if (windows)
                using (ProxyRecorder proxy = new())
                {
                    _settings.ResetAll();
                    _settingsViewModel.HttpProxyPassword = "smoke-secret";
                    string settingsFile = Services.PlayerProfile.FilePath("settings.json");
                    Require(_settings.Current.HttpProxyPasswordProtected is { Length: > 0 } && !File.ReadAllText(settingsFile).Contains("smoke-secret", StringComparison.Ordinal), "the proxy password is stored protected, not in clear text");
                    string stored = _settings.Current.HttpProxyPasswordProtected!;
                    await Open("proxy", s => s with { HttpProxy = $"smokeuser@127.0.0.1:{proxy.Port}", HttpProxyPasswordProtected = stored }, "http://media.example.invalid/clip.mp4");
                    await WaitFor(() => proxy.Request is not null, "VLC sends the request through the proxy", 15);
                    string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("smokeuser:smoke-secret"));
                    Require(proxy.Request!.Contains("http://media.example.invalid", StringComparison.Ordinal) && proxy.Request.Contains(expected, StringComparison.Ordinal), $"the request goes to the proxy with its password: {proxy.Request.Split('\n')[0]}");
                    Pass("HTTP proxy and password");
                }

                // ---- Network › Security ----
                // GnuTLS options: VLC on macOS uses securetransport, and Settings hides them.
                if (OperatingSystem.IsMacOS())
                {
                    await Open("tls-macos", s => s, "https://127.0.0.1:9/clip.mp4");
                    await Task.Delay(1500);
                    await WaitForLog("using tls client module \"securetransport\"", "VLC uses Secure Transport on macOS");
                    Require(!_settingsViewModel.ShowsSecurity, "Settings hides the GnuTLS security options");
                    Pass("Security: Secure Transport, GnuTLS options hidden");
                }
                else
                {
                    await Open("tls-trust-on", s => s, "https://127.0.0.1:9/clip.mp4");
                    await Task.Delay(1500);
                    await WaitForLog("loaded \\d+ trusted CAs from system", "certificates are verified against the system store");
                    await Open("tls-trust-off", s => s with { VerifyCertificates = false }, "https://127.0.0.1:9/clip.mp4");
                    await Task.Delay(1500);
                    await WaitForLog("tls client", "a TLS client starts");
                    await RequireNoLog("trusted CAs from system", "the system store is not loaded when off");
                    Pass("Verify certificates against the system trust store");

                    // Cipher preference: the order of the cipher suites VLC offers in its TLS ClientHello.
                    async Task<ushort[]> OfferedCiphers(string name, string preference)
                    {
                        using ClientHelloRecorder hello = new();
                        await Open(name, s => s with { CipherPreference = preference }, $"https://127.0.0.1:{hello.Port}/clip.mp4");
                        await WaitFor(() => hello.Ciphers is not null, $"{name}: VLC sends a ClientHello", 15);
                        return hello.Ciphers!;
                    }
                    ushort[] performance = await OfferedCiphers("cipher-performance", "Performance");
                    ushort[] balanced = await OfferedCiphers("cipher-balanced", "Normal");
                    ushort[] secure128 = await OfferedCiphers("cipher-secure128", "Secure128");
                    ushort[] secure = await OfferedCiphers("cipher-secure256", "Secure256");
                    string Suites(ushort[] suites) => string.Join(" ", suites.Take(8).Select(c => c.ToString("X4")));
                    Console.WriteLine($"[engine-settings-smoke] ciphers offered: performance {Suites(performance)}; balanced {Suites(balanced)}; secure 128 {Suites(secure128)}; secure 256 {Suites(secure)}");
                    const ushort Aes128Gcm = 0x1301, Aes256Gcm = 0x1302; // TLS 1.3 suites
                    Require(performance[0] == Aes128Gcm && balanced[0] == Aes256Gcm, "Performance offers AES-128 first, Balanced AES-256");
                    Require(secure[0] == Aes256Gcm && !secure.Contains(Aes128Gcm), "Secure (256-bit) offers no 128-bit cipher");
                    Require(new[] { performance, balanced, secure128, secure }.Select(list => string.Join(",", list)).Distinct().Count() == 4, "each preference offers its own list");
                    Pass("Cipher preference (the TLS ClientHello's cipher order follows it)");
                }

                _presentation.StopCommand.Execute(null);
                _settings.ResetAll();
                Topmost = false;
                Console.WriteLine($"[engine-settings-smoke] PASS {passed.Count} checks: {string.Join("; ", passed)}");
                desktop.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[engine-settings-smoke] FAIL after {passed.Count} checks: {error}");
                desktop.Shutdown(1);
            }
        };
    }

    /// <summary>Serves one file over HTTP/1.0 on a local port, for the network caching check.</summary>
    private sealed class MediaServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _content;
        private readonly CancellationTokenSource _stop = new();

        public MediaServer(string path)
        {
            _content = File.ReadAllBytes(path);
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/gop.mp4";

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            await ReadHeadersAsync(stream);
                            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.0 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: {_content.Length}\r\nAccept-Ranges: none\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(header);
                            await stream.WriteAsync(_content);
                        }
                        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException) { }
                    }
                });
            }
        }

        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }

    /// <summary>A proxy that records the first request VLC sends it and answers 404.</summary>
    private sealed class ProxyRecorder : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public volatile string? Request;

        public ProxyRecorder()
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch { return; }
                    using (client)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            Request ??= await ReadHeadersAsync(stream);
                            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                        }
                        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException) { }
                    }
                }
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }

    /// <summary>Reads the cipher suites of the first TLS ClientHello sent to it, then closes the connection.</summary>
    private sealed class ClientHelloRecorder : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public volatile ushort[]? Ciphers;

        public ClientHelloRecorder()
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested && Ciphers is null)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch { return; }
                    using (client)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            byte[] header = await ReadExactlyAsync(stream, 5);
                            if (header[0] != 0x16) continue; // not a TLS handshake record
                            byte[] body = await ReadExactlyAsync(stream, (header[3] << 8) | header[4]);
                            // Handshake type (1 = ClientHello) and length, client version, random, session id.
                            if (body[0] != 1) continue;
                            int offset = 4 + 2 + 32;
                            offset += 1 + body[offset];
                            int length = (body[offset] << 8) | body[offset + 1];
                            offset += 2;
                            Ciphers = Enumerable.Range(0, length / 2).Select(i => (ushort)((body[offset + 2 * i] << 8) | body[offset + 2 * i + 1])).ToArray();
                        }
                        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or IndexOutOfRangeException or EndOfStreamException or OperationCanceledException) { }
                    }
                }
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await stream.ReadExactlyAsync(buffer, timeout.Token);
            return buffer;
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream)
    {
        StringBuilder text = new();
        byte[] buffer = new byte[1];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(buffer, timeout.Token) == 1)
            text.Append((char)buffer[0]);
        return text.ToString();
    }
}
#endif // SMOKE

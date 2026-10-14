#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Player.Application;
using Player.Contracts;
using Conic.Interop;

namespace Conic;

// --parity-smoke: the VLC 3 features added for parity, in a real VLC
// session with real media. Next Frame, Jump to Time, the A-B loop, Take
// Snapshot (a file appears), the live video settings (filters reach the
// output, rotation turns the picture, frames keep coming), the pixel crop,
// live deinterlacing, the logo and marquee, the stereo mode, a subtitle
// added while playing, chapter and program steps, Hide UI, opening from the
// clipboard and sorting the queue. Every check runs; the run fails when any
// does. Fixtures come from artifacts/settings-fixtures
// (eng/make-settings-fixtures.ps1). Run it with CONIC_PROFILE set, as it
// changes settings.
public partial class MainWindow
{
    internal void EnableParitySmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> passed = [], failed = [];
            try
            {
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string Fixture(string name)
                {
                    string path = Path.Combine(fixtures, name);
                    Require(File.Exists(path), $"fixture {name} exists (run eng/make-settings-fixtures.ps1)");
                    return path;
                }
                string work = Path.Combine(RepositoryPaths.ArtifactsDirectory, "parity");
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string snapshots = Directory.CreateDirectory(Path.Combine(work, "snapshots")).FullName;
                string gop = Fixture("gop.mp4"), chapters = Fixture("chapters.mkv"), programs = Fixture("programs.ts"), still = Fixture("still.png");
                string shortOne = Fixture("short-1.mp4"), shortTwo = Fixture("short-2.mp4");
                string subtitle = Path.Combine(work, "added.srt");
                File.WriteAllText(subtitle, "1\n00:00:00,000 --> 00:01:00,000\nAdded while playing\n");

                void Check(bool condition, string what)
                {
                    if (condition) { passed.Add(what); Console.WriteLine($"[parity-smoke] ok: {what}"); }
                    else { failed.Add(what); Console.Error.WriteLine($"[parity-smoke] FAILED: {what}"); }
                }
                async Task<bool> Within(Func<bool> condition, double seconds)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    return condition();
                }
                async Task Settle()
                {
                    await Task.Delay(100);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                }
                VlcCorePlayer Core() => (VlcCorePlayer)VideoSurface.Player!;
                MediaEngineState Engine() => VideoSurface.Player!.State;
                double Elapsed() => VideoSurface.Player?.State.Elapsed.TotalSeconds ?? 0;
                int frames = 0;
                VideoSurface.GPUFramePresentedForSmoke += (_, _, _) => Interlocked.Increment(ref frames);
                async Task<int> FramesIn(double seconds)
                {
                    int start = Volatile.Read(ref frames);
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    return Volatile.Read(ref frames) - start;
                }
                async Task<bool> Open(string path)
                {
                    _presentation.OpenSource(path, MediaSourceKind.File);
                    return await Within(() => _presentation.IsPlaying && Engine().Source is { } source && source.EndsWith(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase), 15);
                }
                void Run(PlayerCommandId command) => _presentation.Registry.Execute(command);
                async Task Set(Func<PlayerSettings, PlayerSettings> update)
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try { _settings.Update(update); return; }
                        catch (IOException) when (attempt < 10) { await Task.Delay(100); }
                    }
                }
                async Task<bool> Applied() => await Within(() => Core().LiveVideoAppliedForSmoke, 10);
                (string? Filters, string? Sources, long? Deinterlace, string? Crop) Output() => Core().ReadVideoOutputForSmoke();

                // Snapshots go to the work folder: the core starts with the option.
                await Set(s => s with { SnapshotFolder = snapshots, SnapshotFormat = "png", ResumePlayback = false });

                // ---- gop.mp4: 30 s, 640x360 ----
                Check(await Open(gop), "gop.mp4 plays");
                Check(await FramesIn(1) > 5, "the video presents frames");

                // Next Frame: the first press pauses, the next ones step.
                Run(PlayerCommandId.NextFrame);
                Check(await Within(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, 3), "Next Frame pauses playing media (E)");
                await Task.Delay(400);
                // VLC 3 keeps the input's time while it steps, so the check is the frames shown.
                int pausedFrames = await FramesIn(.6);
                int stepStart = Volatile.Read(ref frames);
                for (int step = 0; step < 6; step++) { Run(PlayerCommandId.NextFrame); await Task.Delay(200); }
                await Task.Delay(300);
                int stepped = Volatile.Read(ref frames) - stepStart;
                Check(stepped >= 3 && stepped > pausedFrames && _presentation.PlaybackStatus == PlaybackStatus.Paused, $"each Next Frame while paused shows the next frame ({pausedFrames} frames paused, {stepped} for 6 steps)");
                // chapters.mkv for the seeks: a key frame every second. Its
                // item repeats, so the longer checks never reach its end.
                Check(await Open(chapters), "chapters.mkv opens");
                if (_presentation.QueueState.Repeat != QueueRepeatMode.CurrentItem) Run(PlayerCommandId.QueueRepeat);
                Check(await Within(() => _presentation.QueueState.Repeat == QueueRepeatMode.CurrentItem, 3), "its item repeats");

                // A-B loop: A now, B 2 s later; playback goes back to A.
                await Within(() => _presentation.IsPlaying && Elapsed() > 1, 6);
                Run(PlayerCommandId.ToggleABLoop);
                double a = Elapsed();
                await Within(() => Elapsed() >= a + 2, 5);
                // B is where playback is when pressed; the press itself goes back to A.
                double b = Elapsed();
                Run(PlayerCommandId.ToggleABLoop);
                bool loopedOnce = await Within(() => Elapsed() < b - 1, 4);
                bool cameBack = loopedOnce && await Within(() => Elapsed() >= b - .3, 4) && await Within(() => Elapsed() < b - 1, 5);
                Check(loopedOnce && cameBack, $"the A-B loop goes back to A from B, again and again (A {a:0.0} s, B {b:0.0} s)");
                Run(PlayerCommandId.ToggleABLoop);
                Check(!_presentation.IsABLoopActive, "a third press clears the loop");

                // Jump to Time.
                Check(_presentation.JumpToTime("0:20", out string? jumpError), $"Jump to Time takes 0:20 ({jumpError})");
                Check(await Within(() => Math.Abs(Elapsed() - 20) < 1.5, 6), $"Jump to Time reaches 20 s ({Elapsed():0.0} s)");

                // Take Snapshot: VLC writes a PNG in Settings › Video › Snapshots' folder.
                Run(PlayerCommandId.Snapshot);
                Check(await Within(() => Directory.EnumerateFiles(snapshots, "*.png").Any(file => new FileInfo(file).Length > 1000), 6), "Take Snapshot writes a PNG in the snapshot folder (Shift+S)");
                string? shot = Directory.EnumerateFiles(snapshots, "*.png").FirstOrDefault();
                if (shot is not null)
                {
                    byte[] header = new byte[8];
                    using (FileStream stream = File.OpenRead(shot)) stream.ReadExactly(header);
                    Check(header[1] == 'P' && header[2] == 'N' && header[3] == 'G', $"the snapshot is a PNG ({Path.GetFileName(shot)})");
                }

                // Live video settings: the filters reach the output and the
                // picture keeps coming. gop.mp4 (640x360) repeats meanwhile.
                Check(await Open(gop), "gop.mp4 opens for the video settings");
                Check(await Within(() => _presentation.QueueState.Repeat == QueueRepeatMode.CurrentItem, 3), "its item repeats too");
                Check(await FramesIn(1) > 5, "and presents frames");
                Size landscape = VideoSurface.NaturalVideoSize;
                await Set(s => s with { Sepia = true, VideoTransform = "90" });
                Check(await Applied(), "the playing core takes the video settings");
                Check(await Within(() => Output().Filters is { } chain && chain.Contains("sepia", StringComparison.Ordinal) && chain.Contains("transform", StringComparison.Ordinal), 5), $"the output's filters are sepia and transform ({Output().Filters})");
                Check(await Within(() => VideoSurface.NaturalVideoSize.Height > VideoSurface.NaturalVideoSize.Width, 6), $"Rotate 90° turns the picture ({landscape} to {VideoSurface.NaturalVideoSize})");
                Check(await FramesIn(1) > 5, "frames keep coming with CPU filters");
                await Set(s => s with { Sepia = false, VideoTransform = "None" });
                Check(await Applied() && await Within(() => string.IsNullOrEmpty(Output().Filters), 5), $"turning them off empties the chain ({Output().Filters})");
                Check(await Within(() => VideoSurface.NaturalVideoSize.Width > VideoSurface.NaturalVideoSize.Height, 6), $"the picture turns back ({VideoSurface.NaturalVideoSize})");
                Check(await FramesIn(1) > 5, "frames keep coming without filters");
                await Set(s => s with { Contrast = 1.5, Saturation = .5 });
                Check(await Applied() && await Within(() => Output().Filters == "adjust", 5), $"the picture applies live ({Output().Filters})");
                Check(await FramesIn(1) > 5, "frames keep coming with the picture adjusted");
                await Set(s => s with { Contrast = 1, Saturation = 1, InvertColors = true, Posterize = true, Sharpen = true });
                Check(await Applied() && await Within(() => Output().Filters == "invert:posterize:sharpen", 5), $"invert, posterize and sharpen apply live ({Output().Filters})");
                Check(await FramesIn(1) > 5, "frames keep coming with invert, posterize and sharpen");
                await Set(s => s with { Contrast = 1.5, InvertColors = true });
                Check(await Applied() && await Within(() => Output().Filters == "invert:posterize:sharpen:adjust", 5), $"the picture goes after the CPU filters ({Output().Filters})");
                Check(await FramesIn(1.5) > 5 && _presentation.IsPlaying, $"frames keep coming with the picture and CPU filters together ({Output().Filters})");
                await Set(s => s with { Contrast = 1, InvertColors = false, Posterize = false, Sharpen = false });
                Check(await Applied() && await Within(() => string.IsNullOrEmpty(Output().Filters), 5), "turning the effects off empties the chain");

                // Pixel crop.
                await Set(s => s with { CropLeft = 100, CropRight = 100 });
                Check(await Applied() && await Within(() => Output().Crop == "100+0+100+0", 5), $"the pixel crop reaches the output ({Output().Crop})");
                Check(await Within(() => VideoSurface.NaturalVideoSize.Width < landscape.Width - 100, 5), $"the crop narrows the picture ({VideoSurface.NaturalVideoSize})");
                await Set(s => s with { CropLeft = 0, CropRight = 0 });
                Check(await Applied() && await Within(() => string.IsNullOrEmpty(Output().Crop), 5), "no crop removes it");

                // Live deinterlacing.
                await Set(s => s with { Deinterlace = "On", DeinterlaceMethod = "blend" });
                Check(await Applied() && await Within(() => Output().Deinterlace == 1, 5), $"deinterlacing turns on live ({Output().Deinterlace})");
                await Set(s => s with { Deinterlace = "Automatic", DeinterlaceMethod = "Automatic" });
                Check(await Applied() && await Within(() => Output().Deinterlace == -1, 5), "deinterlacing goes back to automatic");

                // Logo and marquee.
                await Set(s => s with { MarqueeText = "Conic parity", LogoFile = still, LogoOpacity = 128 });
                Check(await Applied() && await Within(() => Output().Sources is { } sources && sources.Contains("marq", StringComparison.Ordinal) && sources.Contains("logo", StringComparison.Ordinal), 5), $"the logo and marquee are subpicture sources ({Output().Sources})");
                Check(await FramesIn(1) > 5, "frames keep coming with the overlays");
                await Set(s => s with { MarqueeText = "", LogoFile = "" });
                Check(await Applied() && await Within(() => string.IsNullOrEmpty(Output().Sources), 5), "no overlay text or file removes them");

                // Stereo mode on the playing output.
                await Set(s => s with { StereoMode = "Mono" });
                Check(await Within(() => Core().ReadStereoModeForSmoke() == 7, 5), $"Stereo mode Mono reaches the audio output ({Core().ReadStereoModeForSmoke()})");
                await Set(s => s with { StereoMode = "Unset" });

                // A subtitle file added while playing.
                int subtitlesBefore = _presentation.SubtitleTracks.Count(track => !track.IsDisabled);
                _presentation.OpenDroppedSources([subtitle]);
                Check(_presentation.MediaControlsNotice == "Added to the playing media.", $"a dropped subtitle is added to the playing media ({_presentation.MediaControlsNotice})");
                Check(await Within(() => _presentation.SubtitleTracks.Count(track => !track.IsDisabled) > subtitlesBefore && _presentation.SubtitleTracks.Any(track => !track.IsDisabled && track.IsSelected), 8),
                    $"its track appears and is selected ({string.Join(", ", _presentation.SubtitleTracks.Select(track => track.Label + (track.IsSelected ? "*" : "")))})");

                // Volume up and down.
                double volume = _presentation.Volume;
                Run(PlayerCommandId.VolumeUp);
                Check(await Within(() => _presentation.Volume > volume, 3), $"Volume Up raises the volume ({volume:0} to {_presentation.Volume:0})");
                Run(PlayerCommandId.VolumeDown);
                Check(await Within(() => Math.Abs(_presentation.Volume - volume) < .6, 3), "Volume Down lowers it back");

                // Faster, slower, normal.
                Run(PlayerCommandId.PlaybackFaster);
                Check(await Within(() => Math.Abs(_presentation.PlaybackRate - 1.1) < .01, 3), $"] plays faster ({_presentation.PlaybackRate})");
                Run(PlayerCommandId.PlaybackNormal);
                Check(await Within(() => Math.Abs(_presentation.PlaybackRate - 1) < .01, 3), "= plays at normal speed");

                // Hide UI: the bars overlay the video and hide while it plays.
                Run(PlayerCommandId.ToggleMinimalInterface);
                await Settle();
                Check(Grid.GetRowSpan(VideoSurface) == 3, "Hide UI puts the video under the bars");
                Check(await Within(() => !TopChrome.IsVisible && !PlayerDock.IsVisible, 7), "Hide UI hides the bars while the video plays");
                Run(PlayerCommandId.ToggleMinimalInterface);
                await Settle();
                Check(Grid.GetRowSpan(VideoSurface) == 1 && TopChrome.IsVisible && PlayerDock.IsVisible, "Hide UI again docks the bars");

                // ---- chapters.mkv: three 10 s chapters ----
                Check(await Open(chapters), "chapters.mkv plays");
                Check(await Within(() => _presentation.Chapters.Count == 3, 8), $"its three chapters are listed ({_presentation.Chapters.Count})");
                Run(PlayerCommandId.NextChapter);
                Check(await Within(() => _presentation.Chapters.Count == 3 && _presentation.Chapters[1].IsSelected && Elapsed() >= 9.5, 6), $"Next Chapter goes to the second (Shift+N, {Elapsed():0.0} s)");
                Run(PlayerCommandId.PreviousChapter);
                Check(await Within(() => _presentation.Chapters.Count == 3 && _presentation.Chapters[0].IsSelected && Elapsed() < 9.5, 6), $"Previous Chapter goes back right after the start of one (Shift+P, {Elapsed():0.0} s)");

                // ---- programs.ts: two programs ----
                Check(await Open(programs), "programs.ts plays");
                Check(await Within(() => _presentation.Programs.Count == 2, 10), $"its two programs are listed ({string.Join(", ", _presentation.Programs.Select(program => program.Label))})");
                string? before = _presentation.Programs.FirstOrDefault(program => program.IsSelected)?.Id;
                Run(PlayerCommandId.CycleProgram);
                Check(await Within(() => _presentation.Programs.FirstOrDefault(program => program.IsSelected)?.Id is { } now && now != before, 8), $"Next Program selects the other (X, {before} to {_presentation.Programs.FirstOrDefault(program => program.IsSelected)?.Id})");
                int programFrames = Volatile.Read(ref frames);
                Check(await Within(() => Volatile.Read(ref frames) - programFrames > 10, 8), $"the other program's video plays ({Volatile.Read(ref frames) - programFrames} frames)");

                // ---- the clipboard and the queue ----
                IClipboard? clipboard = Clipboard;
                if (clipboard is not null)
                {
                    // Another program can hold the clipboard for a moment.
                    bool copied = false;
                    for (int attempt = 0; attempt < 20 && !copied; attempt++)
                    {
                        try { await clipboard.SetTextAsync("\"" + gop + "\""); copied = true; }
                        catch (System.Runtime.InteropServices.COMException) { await Task.Delay(250); }
                    }
                    // Some sessions refuse the clipboard (CLIPBRD_E_CANT_OPEN): reported, not failed.
                    if (!copied) Console.WriteLine("[parity-smoke] skipped: this session cannot write the clipboard");
                    else Run(PlayerCommandId.OpenFromClipboard);
                    if (copied) Check(await Within(() => _presentation.IsPlaying && Engine().Source?.EndsWith("gop.mp4", StringComparison.OrdinalIgnoreCase) == true, 10), "Open Location from Clipboard opens the copied path (Ctrl+V)");
                }
                else Check(false, "the window has a clipboard");

                _presentation.OpenDroppedSources([shortTwo, gop, shortOne]);
                Check(await Within(() => _presentation.QueueItems.Count == 3, 10), "three dropped files make the queue");
                Run(PlayerCommandId.SortQueue);
                Check(await Within(() => _presentation.QueueItems.Select(item => item.Name).SequenceEqual(["gop.mp4", "short-1.mp4", "short-2.mp4"]), 6),
                    $"Sort by name orders the queue ({string.Join(", ", _presentation.QueueItems.Select(item => item.Name))})");
                _presentation.QueueSearch = "short";
                Check(_presentation.VisibleQueueItems.Count == 2, "the queue search shows the matching items");
                _presentation.QueueSearch = "";

                Console.WriteLine($"[parity-smoke] {(failed.Count == 0 ? "PASS" : "FAIL")} {passed.Count} checks passed, {failed.Count} failed");
                foreach (string failure in failed) Console.WriteLine($"[parity-smoke] failed: {failure}");
                desktop.Shutdown(failed.Count == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[parity-smoke] FAIL after {passed.Count} checks ({failed.Count} failed): {error}");
                desktop.Shutdown(1);
            }
        };
    }
}
#endif // SMOKE

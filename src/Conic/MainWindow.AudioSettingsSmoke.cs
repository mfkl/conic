#if SMOKE
using System.Text.RegularExpressions;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Interop.Windows;

namespace Conic;

// --audio-settings-smoke (phase 9c): the Audio settings take effect in a
// real VLC session. Each case changes settings through the Settings view
// model and plays a fixture from artifacts/settings-fixtures
// (eng/make-settings-fixtures.ps1). The sound goes to VLC's file output
// (afile, 32-bit float WAV) instead of a device, so the smoke measures what
// VLC played: levels, the stereo image, the tones in it. The live rows are
// changed while the fixture plays, and the first part of the file is
// compared with the last. VLC's log (written when the core stops) and the
// engine state cover the rest. The output module and device cases are
// Windows only (DirectSound, MMDevice endpoints).
public partial class MainWindow
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    internal void EnableAudioSettingsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Opened += async (_, _) =>
        {
            List<string> passed = [];
            try
            {
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The audio settings smoke runs on Windows, macOS and Linux.");
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string Fixture(string name)
                {
                    string path = Path.Combine(fixtures, name);
                    Require(File.Exists(path), $"fixture {name} exists (run eng/make-settings-fixtures.ps1)");
                    return path;
                }
                string logs = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-audio");
                if (Directory.Exists(logs)) Directory.Delete(logs, true);
                Directory.CreateDirectory(logs);

                void Pass(string what) { passed.Add(what); Console.WriteLine($"[audio-settings-smoke] ok: {what}"); }
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                VlcCorePlayer Core() => (VlcCorePlayer)VideoSurface.Player!;
                // The playing core's audio output; empty while the core is still starting.
                (string? Filters, string Device, bool Passthrough) Output()
                {
                    try { return Core().ReadAudioOutputForSmoke(); }
                    catch (InvalidOperationException) { return (null, "unavailable", false); }
                }

                // Every case starts from the defaults (resuming off). With
                // afile, VLC writes the sound it plays to <name>.wav (32-bit
                // float, or 16-bit with sixteenBits). The cases that play on
                // the real device are silent (--gain=0).
                string? currentLog = null, currentWav = null, closedLog = null;
                async Task Open(string name, Func<PlayerSettings, PlayerSettings> change, string media, bool toFile = true, params string[] extra) =>
                    await OpenAs(name, change, media, toFile, false, extra);
                async Task OpenAs(string name, Func<PlayerSettings, PlayerSettings> change, string media, bool toFile, bool sixteenBits, params string[] extra)
                {
                    _presentation.StopCommand.Execute(null);
                    _presentation.IsHomeOpen = false;
                    _settings.ResetAll();
                    _settings.Update(s => change(s with { ResumePlayback = false }));
                    currentLog = Path.Combine(logs, name + ".log");
                    currentWav = toFile ? Path.Combine(logs, name + ".wav") : null;
                    closedLog = null;
                    string[] output = currentWav is null ? ["--gain=0"] : ["--aout=afile", $"--audiofile-file={currentWav}", "--audiofile-wav", $"--audiofile-format={(sixteenBits ? "s16" : "float32")}"];
                    VideoSurface.ExtraOpenArgumentsForSmoke = ["--file-logging", $"--logfile={currentLog}", "--log-verbose=2", .. output, .. extra];
                    long previous = VideoSurface.Player?.State.Generation ?? 0;
                    _presentation.OpenSource(media, MediaSourceKind.File);
                    // The new core plays (not the state the last one left). The
                    // first core of a run can take longer than 15 s after a
                    // fresh build (VLC loads its plugins without a cache, from
                    // cold files).
                    bool Plays() => VideoSurface.Player is { } player && player.State.Generation != previous && player.State.Status == PlaybackStatus.Playing && _presentation.IsPlaying;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(previous == 0 ? 60 : 15);
                    while (!Plays() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(Plays(), $"{name}: plays (engine {VideoSurface.Player?.State.Status}: {VideoSurface.Player?.State.Error})");
                }
                string Log()
                {
                    if (currentLog is null || !File.Exists(currentLog)) return "";
                    using FileStream stream = new(currentLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return new StreamReader(stream).ReadToEnd();
                }
                // Stopping the core closes VLC's log and its WAV file.
                async Task<string> Close()
                {
                    if (closedLog is not null) return closedLog;
                    if (_presentation.StopCommand.CanExecute(null)) _presentation.StopCommand.Execute(null);
                    else VideoSurface.Stop();
                    await WaitFor(() => Log().Contains("-- logger module stopped --", StringComparison.Ordinal), $"VLC closes its log {currentLog}", 15);
                    return closedLog = Log();
                }
                async Task RequireLog(string pattern, string message) =>
                    Require(Regex.IsMatch(await Close(), pattern), $"{message} (log {currentLog} has no /{pattern}/)");
                async Task<Sound> Played(string name, Func<PlayerSettings, PlayerSettings> change, string media, double seconds = 2.5, params string[] extra)
                {
                    await Open(name, change, media, true, extra);
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    await Close();
                    return Sound.Read(currentWav!);
                }
                // A live case: plays, changes a setting while playing, plays on.
                // A changed filter chain restarts VLC's output, and afile then
                // starts its file again: the sound before the change is a copy
                // of the file taken just before it, the sound after is what the
                // file gained once the core thread applied the change (it runs
                // later on a busy machine).
                async Task<(Sound Before, Sound After)> Changed(string name, Func<PlayerSettings, PlayerSettings> start, Action change, string media)
                {
                    await Open(name, start, media);
                    await Task.Delay(2500);
                    string before = Path.Combine(logs, name + "-before.wav");
                    using (FileStream playing = new(currentWav!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (FileStream copy = File.Create(before))
                        playing.CopyTo(copy);
                    change();
                    await WaitFor(() => Core().LiveAudioAppliedForSmoke, $"{name}: the playing core applies the change", 5);
                    long applied = new FileInfo(currentWav!).Length;
                    await Task.Delay(3000);
                    await Close();
                    // A restarted output started a new, shorter file: all of it
                    // comes after the change. Otherwise what the file gained,
                    // less a quarter for the samples already in the output.
                    long final = new FileInfo(currentWav!).Length;
                    double from = final > applied ? (double)applied / final : 0;
                    return (Sound.Read(before).Part(.3, .95), Sound.Read(currentWav!).Part(Math.Max(.3, from + (1 - from) / 4), .97));
                }

                string mix = Fixture("audio-mix.wav"), stereo = Fixture("audio-lr.wav"), dynamics = Fixture("audio-dynamics.wav");

                // ---- Effects: equalizer (next open, then live) ----
                Sound flat = await Played("eq-off", s => s, mix);
                Console.WriteLine($"[audio-settings-smoke] mix: {flat}");
                Require(Math.Abs(flat.Tone(80) - flat.Tone(10000)) < 2, $"the mix has its low and high tones at one level ({flat})");
                EqualizerPreset fullTreble = AudioEngineOptions.EqualizerPresets.Single(p => p.Value == "fulltreble");
                Sound treble = await Played("eq-fulltreble", s => s with { Equalizer = true, EqualizerPreset = "fulltreble", EqualizerPreamp = fullTreble.Preamp, EqualizerBands = AudioEngineOptions.FormatBands(fullTreble.Bands) }, mix);
                double tilt = (treble.Tone(10000) - treble.Tone(80)) - (flat.Tone(10000) - flat.Tone(80));
                Require(tilt > 15, $"Full treble raises 10 kHz against 80 Hz ({tilt:0.0} dB)");
                await RequireLog("using audio filter module \"equalizer\"", "the equalizer runs");
                Pass($"Equalizer and presets at the next open (Full treble tilts 10 kHz/80 Hz by {tilt:0.0} dB)");
                // Live: Full treble chosen, the equalizer turned on while playing; then two bands cut while it plays.
                var eqLive = await Changed("eq-live", s => s with { EqualizerPreset = "fulltreble", EqualizerPreamp = fullTreble.Preamp, EqualizerBands = AudioEngineOptions.FormatBands(fullTreble.Bands) }, () => _settingsViewModel.Equalizer = true, mix);
                double liveTilt = (eqLive.After.Tone(10000) - eqLive.After.Tone(80)) - (eqLive.Before.Tone(10000) - eqLive.Before.Tone(80));
                Console.WriteLine($"[audio-settings-smoke] equalizer turned on while playing: {eqLive.Before} -> {eqLive.After}");
                Require(liveTilt > 15, $"turned on while playing, Full treble raises 10 kHz against 80 Hz ({liveTilt:0.0} dB)");
                var bandLive = await Changed("eq-band-live", s => s with { Equalizer = true }, () => { _settingsViewModel.EqualizerBands[6].Gain = -20; _settingsViewModel.EqualizerBands[7].Gain = -20; }, mix);
                double cut = bandLive.Before.Tone(10000) - bandLive.After.Tone(10000);
                Require(cut > 10 && Math.Abs(bandLive.Before.Tone(80) - bandLive.After.Tone(80)) < 2, $"the 6 and 12 kHz bands cut while playing lower 10 kHz only ({cut:0.0} dB)");
                Require(_settings.Current.EqualizerPreset == AudioEngineOptions.CustomEqualizerPreset, "a changed band makes the preset Custom");
                Pass($"Equalizer live: turned on while playing ({liveTilt:0.0} dB tilt), bands cut while playing ({cut:0.0} dB at 10 kHz)");

                // ---- Effects: karaoke, stereo widening, spatial audio, pitch ----
                Sound karaoke = await Played("karaoke", s => s with { Karaoke = true }, mix);
                Require(karaoke.Rms < flat.Rms - 30, $"Karaoke removes the center ({flat.Rms:0.0} -> {karaoke.Rms:0.0} dB)");
                var karaokeLive = await Changed("karaoke-live", s => s, () => _settingsViewModel.Karaoke = true, mix);
                Require(karaokeLive.After.Rms < karaokeLive.Before.Rms - 30, $"and turns on while playing ({karaokeLive.Before.Rms:0.0} -> {karaokeLive.After.Rms:0.0} dB)");
                Pass($"Karaoke ({flat.Rms:0.0} -> {karaoke.Rms:0.0} dB; live too)");

                Sound wide = await Played("stereo-off", s => s, stereo);

                Sound headphones = await Played("spatial-headphones", s => s with { SpatialAudio = true, SpatialAudioMode = "Headphones" }, mix);
                Sound room = await Played("spatial-room", s => s with { SpatialAudio = true, SpatialAudioMode = "Room" }, mix);
                Console.WriteLine($"[audio-settings-smoke] spatial: headphones {headphones}; room {room}");
                double headphonesCut = OperatingSystem.IsLinux() && !File.Exists("/usr/share/vlc/hrtfs/dodeca_and_7channel_3DSL_HRTF.sofa") ? 3 : 6;
                Require(flat.Side < -100 && headphones.Tone(10000) < flat.Tone(10000) - headphonesCut && room.Side > -80, $"Spatial audio renders the centered mix for headphones (10 kHz {flat.Tone(10000):0.0} -> {headphones.Tone(10000):0.0} dB) and spreads it in a room (side {room.Side:0.0} dB)");
                await RequireLog("using audio filter module \"spatializer\"", "Room uses VLC's spatializer");
                var spatialLive = await Changed("spatial-live", s => s with { SpatialAudioMode = "Room" }, () => _settingsViewModel.SpatialAudio = true, mix);
                Require(spatialLive.Before.Side < -100 && spatialLive.After.Side > -80, $"and turns on while playing (side {spatialLive.Before.Side:0.0} -> {spatialLive.After.Side:0.0} dB)");
                Pass("Spatial audio: Headphone virtualization and Room (live too)");

                Sound pitched = await Played("pitch", s => s with { PitchShift = 12 }, stereo);
                Require(pitched.Tone(880) > pitched.Tone(440) + 20 && wide.Tone(440) > wide.Tone(880) + 20, $"Pitch shift +12 moves 440 Hz to 880 Hz ({pitched})");
                var pitchLive = await Changed("pitch-live", s => s, () => _settingsViewModel.PitchShift = -12, stereo);
                Require(pitchLive.After.Tone(220) > pitchLive.After.Tone(440) + 20 && pitchLive.Before.Tone(440) > pitchLive.Before.Tone(220) + 20, "and -12 while playing moves it to 220 Hz");
                Pass("Pitch shift (+12 at the next open, -12 live)");

                // ---- Effects: compressor; Loudness: normalize ----
                Sound loose = await Played("dynamics-off", s => s, dynamics, 4.5);
                Sound compressed = await Played("compressor", s => s with { Compressor = true }, dynamics, 4.5);
                Console.WriteLine($"[audio-settings-smoke] dynamics: loud/quiet {loose.Loud:0.0}/{loose.Quiet:0.0} dB, compressed {compressed.Loud:0.0}/{compressed.Quiet:0.0} dB");
                Require(compressed.Loud - compressed.Quiet < loose.Loud - loose.Quiet - 4, "the compressor narrows the gap between loud and quiet");
                var compressorLive = await Changed("compressor-live", s => s, () => _settingsViewModel.Compressor = true, mix);
                Require(compressorLive.After.Rms > compressorLive.Before.Rms + 3, $"and turns on while playing ({compressorLive.Before.Rms:0.0} -> {compressorLive.After.Rms:0.0} dB with its make-up gain)");
                Pass($"Compressor (gap {loose.Loud - loose.Quiet:0.0} -> {compressed.Loud - compressed.Quiet:0.0} dB; live too)");
                Sound normalized = await Played("normalize", s => s with { NormalizationMaximumLevel = .5 }, dynamics, 4.5);
                Require(normalized.Loud < loose.Loud - 3, $"Normalize with target 0.5 brings the loud passages down ({loose.Loud:0.0} -> {normalized.Loud:0.0} dB)");
                var normalizeLive = await Changed("normalize-target-live", s => s with { NormalizationMaximumLevel = 10 }, () => _settingsViewModel.NormalizationTarget = .5m, mix);
                Require(normalizeLive.After.Rms < normalizeLive.Before.Rms - 3, $"a lower target while playing lowers the level ({normalizeLive.Before.Rms:0.0} -> {normalizeLive.After.Rms:0.0} dB)");
                Pass($"Normalize loudness and target level ({loose.Loud:0.0} -> {normalized.Loud:0.0} dB; live target too)");

                // ---- Loudness: replay gain (the fixture's tags: track -10 dB, album -4 dB) ----
                string tagged = Fixture("audio-replaygain.ogg");
                Sound untouched = await Played("replaygain-off", s => s, tagged);
                Sound track = await Played("replaygain-track", s => s with { ReplayGain = "track" }, tagged);
                Sound album = await Played("replaygain-album", s => s with { ReplayGain = "album" }, tagged);
                Sound preamp = await Played("replaygain-preamp", s => s with { ReplayGain = "track", ReplayGainPreamp = 6 }, tagged);
                Console.WriteLine($"[audio-settings-smoke] replay gain: off {untouched.Rms:0.0}, track {track.Rms:0.0}, album {album.Rms:0.0}, track +6 {preamp.Rms:0.0} dB");
                Require(Near(untouched.Rms - track.Rms, 10) && Near(untouched.Rms - album.Rms, 4) && Near(untouched.Rms - preamp.Rms, 4), "replay gain follows the track and album tags and the preamp");
                var gainLive = await Changed("replaygain-live", s => s, () => _settingsViewModel.ReplayGain = _settingsViewModel.ReplayGainModes.Single(m => m.Value == "track"), tagged);
                Require(Near(gainLive.Before.Rms - gainLive.After.Rms, 10), $"and Track while playing ({gainLive.Before.Rms:0.0} -> {gainLive.After.Rms:0.0} dB)");
                Pass("Replay gain Track/Album and preamp (live too)");
                static bool Near(double value, double expected) => Math.Abs(value - expected) < 1.5;

                // ---- Advanced: Dolby Surround, resampling ----
                await Open("dolby-on", s => s with { DolbySurround = "On" }, stereo);
                await Task.Delay(1000);
                // The output logs its input format: "input 's16l' 48000 Hz Dolby" or "... Stereo".
                const string DolbyInput = "input '[^']+' \\d+ Hz Dolby";
                await RequireLog(DolbyInput, "Dolby Surround On marks stereo as Dolby Surround");
                string dolby = Fixture("audio-dolby.ac3");
                await Open("dolby-auto", s => s, dolby);
                await Task.Delay(1000);
                await RequireLog(DolbyInput, "Automatic detects the AC-3 track's Dolby Surround flag");
                await Open("dolby-off", s => s with { DolbySurround = "Off" }, dolby);
                await Task.Delay(1000);
                await RequireLog("input '[^']+' \\d+ Hz Stereo", "Off plays it as plain stereo");
                Require(!Regex.IsMatch(await Close(), DolbyInput), "and never as Dolby Surround");
                Pass("Detect Dolby Surround: On, Automatic, Off");

                // A rate change without time stretching resamples: Fast
                // (linear) leaves images of a 6 kHz tone, Best keeps a 21 kHz
                // one the default sinc cuts.
                string[] resample = ["--no-audio-time-stretch"];
                string tone6k = Fixture("audio-6k.wav"), tone20k = Fixture("audio-20k.wav");
                Sound balanced6k = await Played("resampler-balanced-6k", s => s, tone6k, 2.5, [.. resample, "--rate=1.5"]);
                Sound fast6k = await Played("resampler-fast-6k", s => s with { ResamplerQuality = "Fast" }, tone6k, 2.5, [.. resample, "--rate=1.5"]);
                Sound balanced20k = await Played("resampler-balanced-20k", s => s, tone20k, 2.5, [.. resample, "--rate=1.05"]);
                Sound best20k = await Played("resampler-best-20k", s => s with { ResamplerQuality = "Best" }, tone20k, 2.5, [.. resample, "--rate=1.05"]);
                double fastNoise = fast6k.Residual(9000), balancedNoise = balanced6k.Residual(9000);
                Console.WriteLine($"[audio-settings-smoke] resampling: 6 kHz at 1.5x residual balanced {balancedNoise:0.0}, fast {fastNoise:0.0} dB; 20 kHz at 1.05x balanced {balanced20k.Rms:0.0}, best {best20k.Rms:0.0} dB");
                Require(fastNoise > balancedNoise + 20, "Fast resampling leaves more images than Balanced");
                Require(best20k.Rms > balanced20k.Rms + 6, "Best keeps the highest frequencies that Balanced cuts");
                await RequireLog("using audio resampler module \"samplerate\"", "libsamplerate resamples");
                Pass($"Resampling quality (Fast residual {fastNoise - balancedNoise:0} dB above Balanced; Best +{best20k.Rms - balanced20k.Rms:0} dB at 21 kHz)");

                // ---- Output: enable audio, output module, device, passthrough ----
                await Open("audio-off", s => s with { AudioEnabled = false }, mix);
                await Task.Delay(1000);
                await RequireLog("audio is disabled, not selecting ES", "Enable audio off selects no audio track");
                Require(!File.Exists(currentWav) || new FileInfo(currentWav!).Length < 100, "and VLC plays no sound");
                Pass("Enable audio off");

                if (OperatingSystem.IsWindows())
                {
                await Open("module-directsound", s => s with { AudioOutputModule = "directsound" }, mix, toFile: false);
                await Task.Delay(1000);
                await RequireLog("using audio output module \"directsound\"", "Output module DirectSound");
                Pass("Output module");

                IReadOnlyList<SettingOption> devices = AudioEndpoints.List();
                Require(devices.Count >= 2, $"Windows lists a playback device ({devices.Count - 1})");
                string device = devices[^1].Value;
                await Open("device", s => s with { AudioDevice = device }, mix, toFile: false);
                await WaitFor(() => Output().Device == device, "the output uses the chosen device", 5);
                await RequireLog($"using selected device {Regex.Escape(device)}", "MMDevice opens the chosen device");
                await Open("device-live", s => s, mix, toFile: false);
                await WaitFor(() => Output().Device == "", "the output starts on the system default", 5);
                // As in Settings: the Audio page reads the device list when it opens.
                _settingsViewModel.SelectedPage = _settingsViewModel.Pages.Single(page => page.Name == "Audio");
                Require(_settingsViewModel.AudioDevices.Any(d => d.Value == device), $"Settings lists the device ({string.Join(", ", _settingsViewModel.AudioDevices.Select(d => d.Label))})");
                _settingsViewModel.AudioDevice = _settingsViewModel.AudioDevices.Single(d => d.Value == device);
                await WaitFor(() => Output().Device == device, "choosing a device while playing switches the output", 5);
                _settingsViewModel.AudioDevice = _settingsViewModel.AudioDevices[0];
                await WaitFor(() => Output().Device == "", "and System default switches it back", 5);
                await Close();
                Pass($"Output device \"{devices[^1].Label}\" (next open and live)");
                }

                await Open("passthrough", s => s with { AudioPassthrough = true }, mix, toFile: false);
                await WaitFor(() => Output().Passthrough, "the output is asked to pass compressed audio through", 5);
                await Open("passthrough-off", s => s, mix, toFile: false);
                await WaitFor(() => Output().Filters is not null && !Output().Passthrough, "and not by default", 5);
                await Close();
                Pass("Pass compressed audio to the receiver reaches the output (no receiver here to accept it)");

                // VLC 3 plays 16-bit sources through a 16-bit output (Windows;
                // macOS's auhal output is always float), where the equalizer
                // only runs because the gain filter ends its chain.
                static string[] Skipped(string log) => Regex.Matches(log, "cannot add user audio filter \"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
                await OpenAs("eq-16bit", s => s with { Equalizer = true, EqualizerPreset = "fulltreble", EqualizerPreamp = fullTreble.Preamp, EqualizerBands = AudioEngineOptions.FormatBands(fullTreble.Bands) }, mix, true, true);
                await Task.Delay(2500);
                if (OperatingSystem.IsWindows()) await RequireLog("main debug: output 's16l'", "a 16-bit WAV plays through a 16-bit output");
                Require(Skipped(await Close()).Length == 0, "the equalizer is not skipped there");
                Sound sixteen = Sound.Read(currentWav!);
                double tilt16 = (sixteen.Tone(10000) - sixteen.Tone(80)) - (flat.Tone(10000) - flat.Tone(80));
                Require(tilt16 > 15, $"and Full treble tilts the 16-bit output too ({tilt16:0.0} dB)");
                Pass($"Equalizer on a 16-bit output ({tilt16:0.0} dB tilt)");

                // Every effect at once on the real device (silent), in
                // whatever format it plays: a 44.1 kHz AAC track, which most
                // devices (48 kHz) resample.
                foreach ((string name, Func<PlayerSettings, PlayerSettings> effects, string[] filters) in new (string, Func<PlayerSettings, PlayerSettings>, string[])[]
                {
                    ("effects-device-room", s => s with { Karaoke = true, PitchShift = 1, Equalizer = true, Compressor = true, SpatialAudio = true, SpatialAudioMode = "Room", NormalizationMaximumLevel = 4 },
                        ["karaoke", "scaletempo_pitch", "equalizer", "compressor", "spatializer", "normvol"]),
                    ("effects-device-headphones", s => s with { SpatialAudio = true, SpatialAudioMode = "Headphones", Equalizer = true }, ["spatialaudio", "equalizer"]),
                })
                {
                    await Open(name, effects, Fixture("languages.mkv"), false);
                    await Task.Delay(1000);
                    string log = await Close();
                    Console.WriteLine($"[audio-settings-smoke] {name}: {Regex.Match(log, "main debug: input '[^\r\n]+").Value} -> {Regex.Match(log, "main debug: output '[^\r\n]+").Value}");
                    foreach (string filter in filters)
                        await RequireLog($"using audio filter module \"{filter}\"", $"{filter} runs on the device output");
                    string[] skipped = Skipped(log);
                    Require(skipped.Length == 0, $"no effect is skipped ({string.Join(", ", skipped)})");
                }
                Pass("Every effect on the device output (44.1 kHz AAC)");

                // ---- Tracks: default delay ----
                await Open("delay", s => s with { AudioDelay = 500 }, mix);
                await WaitFor(() => Core().MediaCapabilities.Delays.AudioMilliseconds == 500, $"new media starts with a 500 ms audio delay ({Core().MediaCapabilities.Delays.AudioMilliseconds} ms)");
                await Close();
                Pass("Default audio delay");

                // ---- Volume: maximum and step (live, the app's own) ----
                // The file output has no volume, so this case plays on the
                // device (silent) and leaves 100 %.
                await Open("volume", s => s, mix, false);
                _settingsViewModel.MaximumVolume = 300;
                Require(PlayerVolume.Maximum == 300, "Maximum volume 300% widens the volume slider");
                _presentation.Volume = 250;
                await WaitFor(() => Math.Abs(Core().State.Volume - 2.5) < .03, $"250% reaches VLC ({Core().State.Volume:0.00})");
                _settingsViewModel.MaximumVolume = 150;
                await WaitFor(() => PlayerVolume.Maximum == 150 && _presentation.Volume == 150 && Math.Abs(Core().State.Volume - 1.5) < .03, $"a lower maximum brings the volume down to it ({_presentation.Volume:0}%, VLC {Core().State.Volume:0.00})");
                _settingsViewModel.VolumeStep = 7;
                _presentation.Volume = 100;
                PlayerVolume.Focus();
                PlayerVolume.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Right, Source = PlayerVolume });
                Require(Math.Abs(_presentation.Volume - 107) < .01, $"the Right key moves the volume by the step ({_presentation.Volume:0}%)");
                _presentation.Volume = 100;
                await WaitFor(() => Math.Abs(Core().State.Volume - 1) < .03, "the volume goes back to 100%");
                await Close();
                Pass("Maximum volume and Volume step (live)");

                _presentation.StopCommand.Execute(null);
                _settings.ResetAll();
                VideoSurface.ExtraOpenArgumentsForSmoke = [];
                Console.WriteLine($"[audio-settings-smoke] PASS {passed.Count} checks: {string.Join("; ", passed)}");
                desktop.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[audio-settings-smoke] FAIL after {passed.Count} checks: {error}");
                desktop.Shutdown(1);
            }
        };
    }

    /// <summary>The sound VLC wrote to a 32-bit float WAV file (afile), and measures of it in dB.</summary>
    private sealed class Sound(float[] samples, int channels, int rate)
    {
        public static Sound Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int channels = BitConverter.ToInt16(bytes, 22), rate = BitConverter.ToInt32(bytes, 24), bits = BitConverter.ToInt16(bytes, 34);
            int data = 12;
            while (data + 8 <= bytes.Length && System.Text.Encoding.ASCII.GetString(bytes, data, 4) != "data")
                data += 8 + BitConverter.ToInt32(bytes, data + 4);
            Require(data + 8 <= bytes.Length && channels is 1 or 2, $"{path} is a WAV file with sound");
            int size = bits == 16 ? 2 : 4;
            int count = (bytes.Length - data - 8) / size / channels * channels;
            float[] samples = new float[count];
            if (size == 4) Buffer.BlockCopy(bytes, data + 8, samples, 0, count * 4);
            else for (int i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(bytes, data + 8 + 2 * i) / 32768f;
            Require(count > rate, $"{path} holds more than a second of sound");
            return new(samples, channels, rate);
        }

        private int Frames => samples.Length / channels;
        private double Left(int frame) => samples[frame * channels];
        private double Right(int frame) => samples[frame * channels + channels - 1];

        /// <summary>The frames from <paramref name="from"/> to <paramref name="to"/> (fractions of the file).</summary>
        public Sound Part(double from, double to)
        {
            int start = (int)(Frames * from), end = (int)(Frames * to);
            return new(samples[(start * channels)..(end * channels)], channels, rate);
        }

        private static double Db(double power) => 10 * Math.Log10(power + 1e-24);
        private double Power(Func<int, double> sample) { double sum = 0; for (int i = 0; i < Frames; i++) sum += sample(i) * sample(i); return sum / Frames; }
        public double Rms => Db(Power(i => (Left(i) + Right(i)) / 2) + Power(i => (Left(i) - Right(i)) / 2));
        public double Mid => Db(Power(i => (Left(i) + Right(i)) / 2));
        public double Side => Db(Power(i => (Left(i) - Right(i)) / 2));

        /// <summary>The level of a tone in the left channel (the amplitude it fits, as a sine's RMS).</summary>
        public double Tone(double frequency)
        {
            (double sine, double cosine) = Fit(frequency);
            return Db((sine * sine + cosine * cosine) / 2);
        }

        /// <summary>What is left of the left channel without its tone near <paramref name="frequency"/>, relative to it:
        /// in 0.1 s blocks, the tone's exact frequency (VLC's rate is not exactly the one asked) is the best fit within 3 %.</summary>
        public double Residual(double frequency)
        {
            int length = rate / 10;
            return Enumerable.Range(1, 5).Select(block =>
            {
                Sound part = new(samples[(block * Frames / 7 * channels)..((block * Frames / 7 + length) * channels)], channels, rate);
                double best = Enumerable.Range(-150, 301).Select(step => frequency * (1 + step / 5000d))
                    .MaxBy(candidate => { (double s, double c) = part.Fit(candidate); return s * s + c * c; });
                (double a, double b) = part.Fit(best);
                double w = 2 * Math.PI * best / rate;
                return part.Power(i => part.Left(i) - a * Math.Sin(w * i) - b * Math.Cos(w * i)) / ((a * a + b * b) / 2);
            }).Select(Db).Average();
        }

        private (double Sine, double Cosine) Fit(double frequency)
        {
            double w = 2 * Math.PI * frequency / rate, s = 0, c = 0;
            for (int i = 0; i < Frames; i++) { s += Left(i) * Math.Sin(w * i); c += Left(i) * Math.Cos(w * i); }
            return (2 * s / Frames, 2 * c / Frames);
        }

        // The fixture alternates a loud and a quiet second: the loudest and
        // quietest of its half-second windows.
        private IEnumerable<double> Windows() => Enumerable.Range(0, Frames / (rate / 2))
            .Select(index => Part(index * (rate / 2) / (double)Frames, (index + 1) * (rate / 2) / (double)Frames).Mid);
        public double Loud => Windows().Max();
        public double Quiet => Windows().Skip(1).SkipLast(1).Min();

        public override string ToString() => $"rms {Rms:0.0} mid {Mid:0.0} side {Side:0.0}, 80 Hz {Tone(80):0.0}, 440 Hz {Tone(440):0.0}, 880 Hz {Tone(880):0.0}, 1 kHz {Tone(1000):0.0}, 10 kHz {Tone(10000):0.0} dB";
    }
}
#endif // SMOKE

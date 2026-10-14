#if SMOKE
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Player.Application;
using Player.Contracts;
using Conic.Interop;

namespace Conic;

// --protocols-smoke: the network protocols Conic hands to VLC, each played
// from a local server: UDP and RTP (ffmpeg sends MPEG-TS), RTMP (ffmpeg
// serves FLV), FTP (Python's pyftpdlib serves the fixtures) and, on
// Windows, SMB (the drive's administrative share). A protocol whose server
// is missing is reported as skipped, not passed. RTSP, SFTP and MMS need
// servers this smoke does not start. Needs ffmpeg on PATH and the fixtures
// (eng/make-settings-fixtures.ps1).
public partial class MainWindow
{
    internal void EnableProtocolsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            List<string> passed = [], failed = [], skipped = [];
            try
            {
                string fixtures = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-fixtures");
                string gop = Path.Combine(fixtures, "gop.mp4");
                Require(File.Exists(gop), "fixture gop.mp4 exists (run eng/make-settings-fixtures.ps1)");
                int frames = 0;
                VideoSurface.GPUFramePresentedForSmoke += (_, _, _) => Interlocked.Increment(ref frames);
                async Task<bool> Within(Func<bool> condition, double seconds)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
                    return condition();
                }
                Process? Start(string file, string arguments)
                {
                    try
                    {
                        return Process.Start(new ProcessStartInfo(file, arguments)
                        {
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true,
                        });
                    }
                    catch (System.ComponentModel.Win32Exception) { return null; }
                }
                async Task Play(string name, string url, Func<Process?>? server)
                {
                    Process? process = server?.Invoke();
                    if (server is not null && process is null) { skipped.Add(name); Console.WriteLine($"[protocols-smoke] skipped: {name} (no server)"); return; }
                    try
                    {
                        // Servers start, and senders begin streaming, before VLC opens.
                        await Task.Delay(1500);
                        if (process is { HasExited: true }) { skipped.Add(name); Console.WriteLine($"[protocols-smoke] skipped: {name} (server exited: {process.StandardError.ReadToEnd().Trim()})"); return; }
                        int start = Volatile.Read(ref frames);
                        _presentation.OpenSource(url, MediaSourceKind.NetworkUrl);
                        bool plays = await Within(() => _presentation.IsPlaying && Volatile.Read(ref frames) - start > 15, 20);
                        string what = $"{name} plays ({url}, {Volatile.Read(ref frames) - start} frames, {_presentation.PlaybackStatus}{(_presentation.HasOpenError ? ", " + _presentation.OpenError : "")})";
                        if (plays) { passed.Add(what); Console.WriteLine($"[protocols-smoke] ok: {what}"); }
                        else { failed.Add(what); Console.Error.WriteLine($"[protocols-smoke] FAILED: {what}"); }
                    }
                    finally
                    {
                        if (_presentation.StopCommand.CanExecute(null)) _presentation.StopCommand.Execute(null);
                        await Within(() => !_presentation.IsPlaying, 5);
                        if (process is { HasExited: false }) { try { process.Kill(true); } catch (InvalidOperationException) { } }
                        process?.Dispose();
                    }
                }

                string loop = $"-hide_banner -loglevel error -re -stream_loop -1 -i \"{gop}\" -c copy";
                await Play("UDP", "udp://@:15555", () => Start("ffmpeg", $"{loop} -f mpegts \"udp://127.0.0.1:15555?pkt_size=1316\""));
                await Play("RTP", "rtp://@:15556", () => Start("ffmpeg", $"{loop} -f rtp_mpegts rtp://127.0.0.1:15556"));
                await Play("RTMP", "rtmp://127.0.0.1:19350/live/conic", () => Start("ffmpeg", $"{loop} -f flv -listen 1 rtmp://127.0.0.1:19350/live/conic"));
                await Play("FTP", "ftp://127.0.0.1:12121/gop.mp4", () => Start("python", $"-m pyftpdlib -i 127.0.0.1 -p 12121 -d \"{fixtures}\""));
                if (OperatingSystem.IsWindows() && Path.GetPathRoot(gop) is { Length: 3 } root && root[1] == ':')
                    await Play("SMB", $"smb://127.0.0.1/{root[0]}$/{gop[3..].Replace('\\', '/')}", null);
                else { skipped.Add("SMB"); Console.WriteLine("[protocols-smoke] skipped: SMB (no local share)"); }

                Console.WriteLine($"[protocols-smoke] {(failed.Count == 0 ? "PASS" : "FAIL")} {passed.Count} passed, {failed.Count} failed, {skipped.Count} skipped ({string.Join(", ", skipped)}); not checked: RTSP, SFTP, MMS (no local server)");
                desktop.Shutdown(failed.Count == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[protocols-smoke] FAIL {error}");
                desktop.Shutdown(1);
            }
        };
    }
}
#endif // SMOKE

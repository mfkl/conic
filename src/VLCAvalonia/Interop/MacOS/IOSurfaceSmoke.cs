#if SMOKE
using System.Diagnostics;
using System.Runtime.Versioning;
using static VLCAvalonia.Interop.EngineSmoke;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>Headless smokes for the macOS IOSurface video output.</summary>
[SupportedOSPlatform("macos")]
internal static class IOSurfaceSmoke
{
    private const int RequiredFrames = 10;

    /// <summary>
    /// Plays the test media with hardware and then software decoding and
    /// requires BGRA IOSurface frames from the video output each time, with
    /// every lent pixel buffer returned after the player is disposed.
    /// </summary>
    public static int Run()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine($"[iosurface-smoke] Smoke media not found: {mediaPath}");
            return 2;
        }
        try
        {
            using IOSurfacePluginBridge bridge = new(registerFrameCallbacks: false);
            bridge.EnableSurfaceFrames();
            foreach (bool hardwareDecoding in new[] { true, false })
            {
                string decoding = hardwareDecoding ? "hardware" : "software";
                IOSurfacePluginBridge.PoolTelemetry before = bridge.ReadPoolTelemetry();
                HashSet<uint> surfaces = [];
                int frames = 0;
                // Verbose logging records which decoder VLC chose (VideoToolbox or avcodec).
                using (VlcCorePlayer player = new(hardwareDecoding: hardwareDecoding, verboseLogging: hardwareDecoding))
                {
                    OpenAndWait(player, mediaPath, $"{decoding}-open");
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (frames < RequiredFrames && elapsed.Elapsed < PlaybackStageTimeout)
                    {
                        if (bridge.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? frame) && frame is not null)
                        {
                            try
                            {
                                Verify(frame, decoding);
                                surfaces.Add(frame.SurfaceId);
                                if (frames++ == 0)
                                    Console.WriteLine(
                                        $"[iosurface-smoke] {decoding} first-frame surface={frame.SurfaceWidth}x{frame.SurfaceHeight} " +
                                        $"visible={frame.X},{frame.Y} {frame.Width}x{frame.Height} pool-generation={frame.PoolGeneration}");
                            }
                            finally
                            {
                                bridge.Release(frame);
                            }
                        }
                        Thread.Sleep(10);
                    }
                    StopAndWait(player, $"{decoding}-stop");
                }
                bridge.Release(bridge.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? last) ? last : null);
                if (frames < RequiredFrames)
                    throw new TimeoutException($"{decoding} decoding presented {frames} IOSurface frames; expected {RequiredFrames}.");
                IOSurfacePluginBridge.PoolTelemetry after = WaitForReturned(bridge, before.LentBalance, decoding);
                Console.WriteLine(after.ToMachineReadableString());
                if (after.CpuCopies != before.CpuCopies)
                    throw new InvalidOperationException($"{decoding} decoding copied frames on the CPU.");
                Console.WriteLine(
                    $"[iosurface-smoke] PASS {decoding} frames={frames} distinct-surfaces={surfaces.Count} " +
                    $"pixel-buffer-pictures={after.PixelBufferPicturesReceived - before.PixelBufferPicturesReceived}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[iosurface-smoke] FAIL: {exception}");
            return 1;
        }
    }

    /// <summary>The engine lifecycle smoke with IOSurface frame and pixel-buffer balance checks.</summary>
    public static int RunEngine(int cycles) => EngineSmoke.RunEngine(cycles, () => new IOSurfaceEngineVideo());

    private static void Verify(IOSurfacePluginBridge.SurfaceFrame frame, string stage)
    {
        uint format = CoreVideo.IOSurfaceGetPixelFormat(frame.Surface);
        if (format != CoreVideo.PixelFormat32BGRA)
            throw new InvalidOperationException($"{stage}: IOSurface pixel format 0x{format:X8} is not BGRA.");
        if (frame.Width <= 0 || frame.Height <= 0 ||
            frame.X + frame.Width > frame.SurfaceWidth || frame.Y + frame.Height > frame.SurfaceHeight)
            throw new InvalidOperationException(
                $"{stage}: visible area {frame.X},{frame.Y} {frame.Width}x{frame.Height} " +
                $"does not fit the {frame.SurfaceWidth}x{frame.SurfaceHeight} surface.");
    }

    private static IOSurfacePluginBridge.PoolTelemetry WaitForReturned(IOSurfacePluginBridge bridge, long expectedBalance, string stage)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        IOSurfacePluginBridge.PoolTelemetry current;
        do
        {
            bridge.Release(bridge.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? late) ? late : null);
            current = bridge.ReadPoolTelemetry();
            if (current.LentBalance == expectedBalance)
                return current;
            Thread.Sleep(20);
        }
        while (elapsed.Elapsed < ControlStageTimeout);
        Console.Error.WriteLine(current.ToMachineReadableString());
        throw new InvalidOperationException(
            $"Stage '{stage}' left {current.LentBalance - expectedBalance} pixel buffers lent to the app.");
    }

    /// <summary>
    /// Requires IOSurface frames from a new display in every stage and every
    /// lent pixel buffer back once the stage's player is gone.
    /// </summary>
    private sealed class IOSurfaceEngineVideo : IEngineSmokeVideo
    {
        private readonly IOSurfacePluginBridge _monitor = new(registerFrameCallbacks: false);
        private readonly IOSurfacePluginBridge.PoolTelemetry _initial;
        private IOSurfacePluginBridge.PoolTelemetry _mark;

        public IOSurfaceEngineVideo()
        {
            _monitor.EnableSurfaceFrames();
            _initial = _mark = _monitor.ReadPoolTelemetry();
        }

        public string VideoOutput => VlcCorePlayer.DefaultVideoOutput;
        public bool HardwareDecoding => true;
        public string Description => "path=iosurface";

        public void Mark()
        {
            _monitor.Release(_monitor.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? frame) ? frame : null);
            _mark = _monitor.ReadPoolTelemetry();
        }

        public void WaitForFrames(string stage)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < PlaybackStageTimeout)
            {
                IOSurfacePluginBridge.PoolTelemetry current = _monitor.ReadPoolTelemetry();
                if (current.PixelBufferPoolGenerations > _mark.PixelBufferPoolGenerations &&
                    _monitor.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? frame) && frame is not null)
                {
                    try
                    {
                        Verify(frame, stage);
                        Console.WriteLine(
                            $"[engine-smoke] PASS {stage}: pixel-buffer-pictures=" +
                            $"{current.PixelBufferPicturesReceived - _mark.PixelBufferPicturesReceived} " +
                            $"pool-generation={frame.PoolGeneration} path=iosurface " +
                            $"surface={frame.SurfaceWidth}x{frame.SurfaceHeight}");
                        return;
                    }
                    finally
                    {
                        _monitor.Release(frame);
                    }
                }
                Thread.Sleep(20);
            }
            Console.Error.WriteLine(_monitor.ReadPoolTelemetry().ToMachineReadableString());
            throw new TimeoutException(
                $"Stage '{stage}' did not receive an IOSurface frame within {PlaybackStageTimeout.TotalSeconds:0.#} seconds.");
        }

        public void WaitForRelease(string stage) => WaitForReturned(_monitor, _initial.LentBalance, stage);

        public void Complete()
        {
            IOSurfacePluginBridge.PoolTelemetry final = _monitor.ReadPoolTelemetry();
            Console.WriteLine(final.ToMachineReadableString());
            if (final.CpuCopies != _initial.CpuCopies)
                throw new InvalidOperationException("The IOSurface lifecycle copied frames on the CPU.");
        }

        public void Dispose() => _monitor.Dispose();
    }
}
#endif // SMOKE

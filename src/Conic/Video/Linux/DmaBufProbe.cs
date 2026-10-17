using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Conic.Interop;
using Conic.Interop.Linux;

namespace Conic.Video.Linux;

/// <summary>
/// Checks the Linux GPU video route: VA-API converts YUV to BGRA on the GPU,
/// exports the result as a DMA-BUF, and Avalonia's compositor imports and
/// presents it. Without media the source is color bars, and the converted
/// colors are read back once and checked. With --media=, the distribution's
/// libvlc decodes the file in software and each frame is uploaded into the
/// source surface first. --resize walks the window through sizes and
/// fullscreen while frames are presented.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class DmaBufProbe
{
    private const int BarsWidth = 1280;
    private const int BarsHeight = 720;
    private const int RingSize = 2;
    private const int MinimumFrames = 240;

    private static readonly (byte Y, byte U, byte V, byte R, byte G, byte B, string Name)[] Bars =
    [
        (235, 128, 128, 255, 255, 255, "white"),
        (219, 16, 138, 255, 255, 0, "yellow"),
        (188, 154, 16, 0, 255, 255, "cyan"),
        (173, 42, 26, 0, 255, 0, "green"),
        (78, 214, 230, 255, 0, 255, "magenta"),
        (63, 102, 240, 255, 0, 0, "red"),
        (32, 240, 118, 0, 0, 255, "blue"),
        (16, 128, 128, 0, 0, 0, "black"),
    ];

    public static Window CreateWindow(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        string? Option(string name) => args.FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?[name.Length..];
        string path = Path.GetFullPath(Option("--report=") ?? "artifacts/dmabuf-probe.json");
        int hold = int.TryParse(Option("--hold="), out int ms) ? ms : 0;
        string? media = Option("--media=") is { } m ? Path.GetFullPath(m) : null;
        string chroma = Option("--chroma=")?.ToUpperInvariant() ?? "NV12";
        double seconds = double.TryParse(Option("--seconds="), System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 8;
        bool resize = args.Contains("--resize");

        Border host = new() { Background = Brushes.DimGray };
        Window window = new() { Title = "Conic DMA-BUF probe", Width = 960, Height = 540, Content = host };
        Report report = new() { Media = media, Chroma = media is null ? "NV12" : chroma };
        bool finished = false;

        void Finish(string? error)
        {
            if (finished) return;
            finished = true;
            report.Error ??= error;
            int exitCode = report.Error is null ? 0 : 2;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                Console.WriteLine(json);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not write the DMA-BUF report: {exception.Message}");
                exitCode = 1;
            }
            desktop.Shutdown(exitCode);
        }

        window.Closing += (_, _) => Finish("The probe window was closed before completion.");
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            VaapiConverter? converter = null;
            LibVlcFrameSource? source = null;
            List<ICompositionImportedGpuImage> images = [];
            try
            {
                report.Windowing = window.PlatformImpl?.GetType().FullName ?? window.TryGetPlatformHandle()?.HandleDescriptor;
                CompositionVisual visual = ElementComposition.GetElementVisual(host)
                    ?? throw new InvalidOperationException("No Avalonia compositor visual is available.");
                ICompositionGpuInterop interop = await visual.Compositor.TryGetCompositionGpuInterop()
                    ?? throw new InvalidOperationException("The compositor has no GPU interop.");
                const string handleType = KnownPlatformGraphicsExternalImageHandleTypes.DmaBufFileDescriptor;
                report.ImageHandleTypes = interop.SupportedImageHandleTypes.ToArray();
                if (!interop.SupportedImageHandleTypes.Contains(handleType))
                    throw new InvalidOperationException("The compositor cannot import DMA-BUFs; Linux needs Avalonia's EGL rendering.");
                report.Synchronization = interop.GetSynchronizationCapabilities(handleType).ToString();

                if (media is null)
                {
                    converter = VaapiConverter.Open(BarsWidth, BarsHeight, VaApi.FourCCNv12, RingSize);
                    FillBars(converter);
                    converter.Convert(0);
                    int[] columns = Enumerable.Range(0, Bars.Length).Select(i => (2 * i + 1) * BarsWidth / (2 * Bars.Length)).ToArray();
                    report.Bars = CheckBars(converter.ReadBackRow(0, BarsHeight / 2, columns));
                }
                else
                {
                    VlcRuntime.Initialize();
                    source = new LibVlcFrameSource(media, chroma);
                    source.Play();
                    Task first = await Task.WhenAny(source.Format, Task.Delay(TimeSpan.FromSeconds(10)));
                    if (first != source.Format) throw new TimeoutException("libvlc did not report a video format.");
                    (int width, int height) = await source.Format;
                    (int visibleWidth, int visibleHeight) = source.VisibleSize is { } v && v.Width <= width && v.Height <= height ? v : (width, height);
                    converter = VaapiConverter.Open(width, height, 0, 0, visibleWidth, visibleHeight,
                        chroma == "P010" ? VaApi.FourCCP010 : VaApi.FourCCNv12, RingSize);
                }
                report.RenderNode = converter.RenderNode;
                report.VaVendor = converter.Vendor;
                report.VideoSize = $"{converter.TargetWidth}x{converter.TargetHeight} (decoded {converter.Width}x{converter.Height})";

                CompositionDrawingSurface surface = visual.Compositor.CreateDrawingSurface();
                CompositionSurfaceVisual surfaceVisual = visual.Compositor.CreateSurfaceVisual();
                surfaceVisual.Surface = surface;
                surfaceVisual.Size = new Vector(host.Bounds.Width, host.Bounds.Height);
                host.SizeChanged += (_, e) => surfaceVisual.Size = new Vector(e.NewSize.Width, e.NewSize.Height);
                ElementComposition.SetElementChildVisual(host, surfaceVisual);

                IReadOnlyList<PlatformGraphicsDrmFormat>? importable = interop.SupportedDmaBufFormats;
                for (int index = 0; index < RingSize; index++)
                {
                    ExportedSurface exported = converter.Export(index);
                    report.Exports.Add(Describe(exported, importable));
                    ICompositionImportedGpuImage image = interop.ImportImage(
                        new PlatformHandle(exported.Fd, handleType),
                        new PlatformGraphicsExternalImageProperties
                        {
                            Width = converter.TargetWidth,
                            Height = converter.TargetHeight,
                            Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                            TopLeftOrigin = true,
                            MemorySize = exported.Size,
                            MemoryOffset = exported.Offset,
                            DmaBufProperties = new PlatformGraphicsExternalImageDmaBufProperties
                            {
                                DrmFormat = exported.DrmFormat,
                                DrmModifier = exported.Modifier,
                                PlaneCount = 1,
                                PlaneStrides = [exported.Pitch],
                                PlaneOffsets = [exported.Offset],
                            },
                        });
                    images.Add(image);
                    await image.ImportCompleted;
                }

                using CancellationTokenSource stop = new();
                Stopwatch clock = Stopwatch.StartNew();
                Task steps = resize ? WalkSizesAsync(window, host, report, clock, stop.Token) : Task.CompletedTask;
                List<double> uploads = [], converts = [], updates = [];
                int frame = 0;
                while (true)
                {
                    bool enough = media is null ? frame >= MinimumFrames : clock.Elapsed.TotalSeconds >= seconds;
                    if (enough && steps.IsCompleted) break;
                    int index = frame % RingSize;
                    if (source is not null)
                    {
                        int buffer;
                        using (CancellationTokenSource wait = new(TimeSpan.FromSeconds(5)))
                            buffer = await source.TakeAsync(wait.Token);
                        long uploadStart = Stopwatch.GetTimestamp();
                        unsafe { converter.Upload(source.Luma(buffer), source.LumaPitch, source.LumaLines, source.Chroma(buffer), source.ChromaPitch, source.LumaLines / 2); }
                        uploads.Add(Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds);
                        source.Release(buffer);
                    }
                    long convertStart = Stopwatch.GetTimestamp();
                    converter.Convert(index);
                    converts.Add(Stopwatch.GetElapsedTime(convertStart).TotalMilliseconds);
                    long updateStart = Stopwatch.GetTimestamp();
                    await surface.UpdateAsync(images[index]);
                    double updateMs = Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds;
                    updates.Add(updateMs);
                    report.FramesPresented = frame + 1;
                    if (updateMs > 100) report.Stalls.Add(new(Math.Round(clock.Elapsed.TotalSeconds, 2), Math.Round(updateMs)));
                    frame++;
                }
                await steps;
                report.PresentSeconds = clock.Elapsed.TotalSeconds;
                report.UploadMs = Summary.Of(uploads);
                report.ConvertMs = Summary.Of(converts);
                report.UpdateMs = Summary.Of(updates);
                if (source is not null)
                {
                    report.FramesDecoded = source.FramesDisplayed;
                    report.FramesDropped = source.FramesDropped;
                    report.BufferOverruns = source.Overruns;
                }
                if (report.Bars.Any(bar => !bar.Matches))
                    report.Error = "VA-API converted the color bars to unexpected colors.";
                if (hold > 0) await Task.Delay(hold);
                Finish(null);
            }
            catch (Exception exception)
            {
                Finish($"{exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                source?.Dispose();
                foreach (ICompositionImportedGpuImage image in images) await image.DisposeAsync();
                converter?.Dispose();
            }
        });
        return window;
    }

    /// <summary>
    /// Resizes, then fullscreen and back, holding each step so it can be
    /// captured from outside. Each step is announced on stdout.
    /// </summary>
    private static async Task WalkSizesAsync(Window window, Control host, Report report, Stopwatch clock, CancellationToken cancellation)
    {
        (string Name, double Width, double Height)[] sizes = [("small", 480, 270), ("tall", 540, 720), ("large", 1600, 900)];
        foreach (var (name, width, height) in sizes)
        {
            window.Width = width;
            window.Height = height;
            await HoldStepAsync(name, window, host, report, clock, cancellation);
        }
        window.WindowState = WindowState.FullScreen;
        await HoldStepAsync("fullscreen", window, host, report, clock, cancellation);
        window.WindowState = WindowState.Normal;
        await HoldStepAsync("restored", window, host, report, clock, cancellation);
    }

    private static async Task HoldStepAsync(string name, Window window, Control host, Report report, Stopwatch clock, CancellationToken cancellation)
    {
        double requested = Math.Round(clock.Elapsed.TotalSeconds, 2);
        await Task.Delay(700, cancellation);
        string bounds = $"{host.Bounds.Width:0}x{host.Bounds.Height:0}";
        report.ResizeSteps.Add(new(name, requested, window.WindowState.ToString(), bounds, window.RenderScaling));
        Console.WriteLine($"[step] {name} {bounds}");
        await Task.Delay(1300, cancellation);
    }

    private static unsafe void FillBars(VaapiConverter converter)
    {
        int width = converter.Width, height = converter.Height;
        using VaapiConverter.SourceView view = converter.MapSource();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                view.Luma[y * view.LumaPitch + x] = Bars[x * Bars.Length / width].Y;
        for (int y = 0; y < height / 2; y++)
            for (int x = 0; x < width / 2; x++)
            {
                var bar = Bars[x * 2 * Bars.Length / width];
                view.Chroma[y * view.ChromaPitch + x * 2] = bar.U;
                view.Chroma[y * view.ChromaPitch + x * 2 + 1] = bar.V;
            }
    }

    private static BarCheck[] CheckBars(byte[] bgra)
    {
        const int tolerance = 12;
        BarCheck[] checks = new BarCheck[Bars.Length];
        for (int i = 0; i < Bars.Length; i++)
        {
            byte b = bgra[i * 4], g = bgra[i * 4 + 1], r = bgra[i * 4 + 2];
            var bar = Bars[i];
            bool matches = Math.Abs(r - bar.R) <= tolerance && Math.Abs(g - bar.G) <= tolerance && Math.Abs(b - bar.B) <= tolerance;
            checks[i] = new(bar.Name, $"{bar.R},{bar.G},{bar.B}", $"{r},{g},{b}", matches);
        }
        return checks;
    }

    private static ExportDescription Describe(ExportedSurface exported, IReadOnlyList<PlatformGraphicsDrmFormat>? importable) => new(
        VaapiConverter.FourCCText(exported.DrmFormat), $"0x{exported.Modifier:X16}", exported.Pitch, exported.Offset, exported.Size,
        exported.Objects, exported.Layers, importable?.Contains(new PlatformGraphicsDrmFormat(exported.DrmFormat, exported.Modifier)));

    private sealed record BarCheck(string Bar, string ExpectedRgb, string ConvertedRgb, bool Matches);

    private sealed record ExportDescription(string DrmFormat, string Modifier, uint Pitch, uint Offset, ulong Size,
        int Objects, int Layers, bool? AdvertisedByCompositor);

    private sealed record ResizeStep(string Name, double AtSeconds, string WindowState, string Bounds, double Scaling);

    private sealed record Stall(double AtSeconds, double UpdateMs);

    private sealed record Summary(int Count, double Median, double P95, double Max)
    {
        public static Summary? Of(List<double> values)
        {
            if (values.Count == 0) return null;
            values.Sort();
            return new(values.Count, Math.Round(values[values.Count / 2], 2),
                Math.Round(values[(int)(values.Count * 0.95)], 2), Math.Round(values[^1], 2));
        }
    }

    private sealed record Report
    {
        public int SchemaVersion { get; init; } = 2;
        public string Os { get; init; } = RuntimeInformation.OSDescription;
        public string? SessionType { get; init; } = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        public string? Windowing { get; set; }
        public string AvaloniaVersion { get; init; } = typeof(Window).Assembly.GetName().Version?.ToString() ?? "unknown";
        public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
        public string[] ImageHandleTypes { get; set; } = [];
        public string? Synchronization { get; set; }
        public string? RenderNode { get; set; }
        public string? VaVendor { get; set; }
        public string? Media { get; init; }
        public string? Chroma { get; init; }
        public string? VideoSize { get; set; }
        public BarCheck[] Bars { get; set; } = [];
        public List<ExportDescription> Exports { get; } = [];
        public int FramesPresented { get; set; }
        public int? FramesDecoded { get; set; }
        public int? FramesDropped { get; set; }
        public int? BufferOverruns { get; set; }
        public double PresentSeconds { get; set; }
        public Summary? UploadMs { get; set; }
        public Summary? ConvertMs { get; set; }
        public Summary? UpdateMs { get; set; }
        public List<ResizeStep> ResizeSteps { get; } = [];
        public List<Stall> Stalls { get; } = [];
        public string? Error { get; set; }
        public string Qualification => "VA-API conversion, DMA-BUF export and compositor import. Media, if any, is decoded in software by the distribution's libvlc and uploaded.";
    }
}

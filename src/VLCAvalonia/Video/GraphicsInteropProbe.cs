using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace VLCAvalonia.Video;

/// <summary>Queries the actual compositor without initializing a VLC runtime or player.</summary>
internal static class GraphicsInteropProbe
{
    public static Window CreateWindow(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        string path = Path.GetFullPath(args.FirstOrDefault(x => x.StartsWith("--report=", StringComparison.Ordinal))?
            ["--report=".Length..] ?? "artifacts/graphics-probe.json");
        TextBlock content = new()
        {
            Text = "Checking graphics support…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Window window = new() { Title = "VLC Avalonia graphics diagnostic", Width = 480, Height = 200, Content = content };
        DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(15) };
        bool finished = false;

        void Finish(GraphicsProbeReport report)
        {
            if (finished) return;
            finished = true;
            timeout.Stop();
            int exitCode = report.InteropAvailable && !report.DeviceLost && report.ImageHandleTypes.Length > 0 && report.Error is null ? 0 : 2;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                Console.WriteLine(json);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not write graphics report: {exception.Message}");
                exitCode = 1;
            }
            desktop.Shutdown(exitCode);
        }

        timeout.Tick += (_, _) => Finish(new() { Error = "Timed out waiting for the compositor." });
        window.Closing += (_, _) =>
        {
            if (!finished) Finish(new() { Error = "The diagnostic window was closed before completion." });
        };
        // Post beyond Opened so even a synchronously completed compositor query
        // cannot shut down the desktop lifetime while it is still starting.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            if (finished) return;
            timeout.Start();
            try
            {
                CompositionVisual visual = ElementComposition.GetElementVisual(content)
                    ?? throw new InvalidOperationException("No Avalonia compositor visual is available.");
                ICompositionGpuInterop? interop = await visual.Compositor.TryGetCompositionGpuInterop();
                if (finished) return;
                Finish(new()
                {
                    InteropAvailable = interop is not null,
                    DeviceLost = interop?.IsLost ?? false,
                    DeviceLuidHex = interop?.DeviceLuid is { } luid ? Convert.ToHexString(luid) : null,
                    DeviceUuidHex = interop?.DeviceUuid is { } uuid ? Convert.ToHexString(uuid) : null,
                    ImageHandleTypes = interop?.SupportedImageHandleTypes.ToArray() ?? [],
                    SemaphoreTypes = interop?.SupportedSemaphoreTypes.ToArray() ?? [],
                    Synchronization = interop?.SupportedImageHandleTypes.ToDictionary(
                        type => type, type => interop.GetSynchronizationCapabilities(type).ToString()) ?? [],
                });
            }
            catch (Exception exception) { Finish(new() { Error = exception.Message }); }
        });
        return window;
    }

    private sealed record GraphicsProbeReport
    {
        public int SchemaVersion { get; init; } = 1;
        public string Os { get; init; } = RuntimeInformation.OSDescription;
        public string Architecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
        public string? SessionType { get; init; } = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        public string AvaloniaVersion { get; init; } = typeof(Window).Assembly.GetName().Version?.ToString() ?? "unknown";
        public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
        public bool InteropAvailable { get; init; }
        public bool DeviceLost { get; init; }
        public string? DeviceLuidHex { get; init; }
        public string? DeviceUuidHex { get; init; }
        public string[] ImageHandleTypes { get; init; } = [];
        public string[] SemaphoreTypes { get; init; } = [];
        public Dictionary<string, string> Synchronization { get; init; } = [];
        public string? Error { get; init; }
        public bool HardwarePlaybackQualified => false;
        public string Qualification => "Compositor capabilities only; no decoder, image import or presentation was tested.";
    }
}

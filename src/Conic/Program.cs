using Conic.Video;
using Conic.Interop.MacOS;
using Conic.Interop.Windows;
using Avalonia;
using Conic.Interop;

namespace Conic;

internal static class Program
{
    internal static PlaybackStartupFailure? StartupFailure { get; private set; }

    /// <summary>
    /// Review captures (--menu-review) draw popups (menus) inside the window,
    /// so a capture of the window shows them even while it is in the
    /// background, where a separate popup window would close.
    /// </summary>
    private static bool _overlayPopups;

    [STAThread]
    public static int Main(string[] args)
    {
#if SMOKE
        _overlayPopups = args.Any(arg => arg.StartsWith("--menu-review=", StringComparison.OrdinalIgnoreCase));
#endif
        if (args.Contains("--graphics-probe", StringComparer.OrdinalIgnoreCase))
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#if SMOKE
        if (IsSmokeRequested(args))
        {
            // Headless smokes check the VLC engine; window smokes (--ui-smoke,
            // --pip-smoke, ...) run inside the app and need video playback.
            bool headless = IsHeadlessSmoke(args);
            // Diagnostics must fail promptly instead of waiting for someone to
            // close a dialog, so smoke runs never reach the failure window.
            string? unavailable = headless ? NativeEngineSupport.CurrentUnavailableReason : NativePlaybackSupport.CurrentUnavailableReason;
            if (unavailable is not null)
            {
                Console.Error.WriteLine(unavailable);
                return 2;
            }
            StartupFailure = PlaybackStartup.TryInitialize(headless ? VlcRuntime.Initialize : VideoBackendFactory.InitializeRuntime);
            if (StartupFailure is { } startupFailure)
            {
                Console.Error.WriteLine($"[startup] {startupFailure.Message}\n{startupFailure.Detail}");
                return 2;
            }
            return headless ? RunSmoke(args) : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
#endif
        StartupFailure = PlaybackStartup.TryInitialize(VideoBackendFactory.InitializeRuntime);
        if (StartupFailure is { } failure)
        {
            Console.Error.WriteLine($"[startup] {failure.Message}\n{failure.Detail}");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 2;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

#if SMOKE
    private static readonly string[] SmokeFlags =
    [
        "--engine-smoke",
        "--queue-smoke",
        "--media-capabilities-smoke",
        "--player-capabilities-smoke",
        "--opaque-smoke",
        "--iosurface-smoke",
        "--smoke",
    ];

    private static bool IsSmokeRequested(string[] args) =>
        args.Any(arg =>
            SmokeFlags.Contains(arg, StringComparer.OrdinalIgnoreCase) ||
            arg.EndsWith("-smoke", StringComparison.OrdinalIgnoreCase));

    private static bool IsHeadlessSmoke(string[] args) =>
        args.Any(arg => SmokeFlags.Contains(arg, StringComparer.OrdinalIgnoreCase));

    private static int RunSmoke(string[] args)
    {
        if (!NativeEngineSupport.IsAvailable)
            return 2;
        int cycles = ParseCycles(args);
        if (args.Contains("--engine-smoke", StringComparer.OrdinalIgnoreCase))
            return OperatingSystem.IsWindows() ? CompositionSmoke.RunEngine(cycles)
                : OperatingSystem.IsMacOS() ? IOSurfaceSmoke.RunEngine(cycles)
                : EngineSmoke.RunEngine(cycles, () => new NoEngineSmokeVideo());
        if (args.Contains("--queue-smoke", StringComparer.OrdinalIgnoreCase))
            return EngineSmoke.RunQueue(
                args.Contains("--queue-smoke-verbose", StringComparer.OrdinalIgnoreCase));
        if (args.Contains("--media-capabilities-smoke", StringComparer.OrdinalIgnoreCase))
            return EngineSmoke.RunMediaCapabilities();
        if (args.Contains("--player-capabilities-smoke", StringComparer.OrdinalIgnoreCase))
            return EngineSmoke.RunPlayerCapabilities();
        if (args.Contains("--iosurface-smoke", StringComparer.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsMacOS())
                return IOSurfaceSmoke.Run();
            Console.Error.WriteLine("This smoke checks the IOSurface video output, which only exists on macOS.");
            return 2;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This smoke checks the D3D11 video output, which only exists on Windows.");
            return 2;
        }
        if (args.Contains("--opaque-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunOpaque();
        return CompositionSmoke.Run(ParseExpectedPath(args));
    }

    private static string? ParseExpectedPath(string[] args)
    {
        const string prefix = "--expect-path=";
        return args.FirstOrDefault(value =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            [prefix.Length..].ToLowerInvariant();
    }

    private static int ParseCycles(string[] args)
    {
        const string prefix = "--cycles=";
        string? argument = args.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
            return 1;
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int cycles) ||
            cycles is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                argument,
                "--cycles must be between 1 and 100.");
        }
        return cycles;
    }
#endif

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
        if (_overlayPopups)
            builder = builder.With(new Win32PlatformOptions { OverlayPopups = true });
        // Diagnostics: pick the X11 rendering backend (glx, egl, vulkan, software)
        // so --graphics-probe can report each one's GPU interop on Linux.
        if (Enum.TryParse(Environment.GetEnvironmentVariable("CONIC_X11_RENDERING"), ignoreCase: true, out X11RenderingMode mode))
            builder = builder.With(new X11PlatformOptions { RenderingMode = [mode] });
        return builder;
    }
}

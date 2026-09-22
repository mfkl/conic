using VLCAvalonia.Video;
using VLCAvalonia.Interop.Windows;
using Avalonia;
using VLCAvalonia.Interop;

namespace VLCAvalonia;

internal static class Program
{
    internal static PlaybackStartupFailure? StartupFailure { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--graphics-probe", StringComparer.OrdinalIgnoreCase))
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#if SMOKE
        if (IsSmokeRequested(args))
        {
            // Diagnostics must fail promptly instead of waiting for someone to
            // close a dialog, so smoke runs never reach the failure window.
            if (!NativePlaybackSupport.IsAvailable)
            {
                Console.Error.WriteLine(NativePlaybackSupport.CurrentUnavailableReason);
                return 2;
            }
            StartupFailure = PlaybackStartup.TryInitialize(VideoBackendFactory.InitializeRuntime);
            if (StartupFailure is { } startupFailure)
            {
                Console.Error.WriteLine($"[startup] {startupFailure.Message}\n{startupFailure.Detail}");
                return 2;
            }
            return RunSmoke(args);
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
        "--smoke",
    ];

    private static bool IsSmokeRequested(string[] args) =>
        args.Any(arg =>
            SmokeFlags.Contains(arg, StringComparer.OrdinalIgnoreCase) ||
            arg.EndsWith("-smoke", StringComparison.OrdinalIgnoreCase));

    private static int RunSmoke(string[] args)
    {
        int cycles = ParseCycles(args);
        if (args.Contains("--engine-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunEngine(cycles);
        if (args.Contains("--queue-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunQueue(
                args.Contains("--queue-smoke-verbose", StringComparer.OrdinalIgnoreCase));
        if (args.Contains("--media-capabilities-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunMediaCapabilities();
        if (args.Contains("--player-capabilities-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunPlayerCapabilities();
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
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

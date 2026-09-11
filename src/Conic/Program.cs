using Conic.Video;
using Conic.Interop.Windows;
using Avalonia;
using Conic.Interop;

namespace Conic;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--graphics-probe", StringComparer.OrdinalIgnoreCase))
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        bool runsWindowsSmoke = args.Any(arg => arg.Equals("--engine-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--queue-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--media-capabilities-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--player-capabilities-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--opaque-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        if (runsWindowsSmoke && !NativePlaybackSupport.IsAvailable)
        {
            Console.Error.WriteLine(NativePlaybackSupport.CurrentUnavailableReason);
            return 2;
        }
        VideoBackendFactory.InitializeRuntime();
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
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.Run(ParseExpectedPath(args));

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

using System.Runtime.InteropServices;
using Avalonia;
using VLCAvalonia.Interop;

namespace VLCAvalonia;

internal static class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectoryW(string lpPathName);

    private static void SetupVLCPath()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string VLCDirectory = RepositoryPaths.VLCDirectory;
        if (!Directory.Exists(VLCDirectory))
            return;

        SetDllDirectoryW(VLCDirectory);
        NativeLibrary.Load(Path.Combine(VLCDirectory, "libvlc.dll"));
        NativeLibrary.Load(Path.Combine(VLCDirectory, "libvlccore.dll"));
    }

    [STAThread]
    public static int Main(string[] args)
    {
        bool runsWindowsSmoke = args.Any(arg => arg.Equals("--engine-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--queue-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--opaque-smoke", StringComparison.OrdinalIgnoreCase) || arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        if (runsWindowsSmoke && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("VLC smoke modes are available on Windows only.");
            return 2;
        }
        SetupVLCPath();
        int cycles = ParseCycles(args);
        if (args.Contains("--engine-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunEngine(cycles);
        if (args.Contains("--queue-smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.RunQueue(
                args.Contains("--queue-smoke-verbose", StringComparer.OrdinalIgnoreCase));
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

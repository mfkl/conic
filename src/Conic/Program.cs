using System.Runtime.InteropServices;
using Avalonia;
using Conic.Interop;

namespace Conic;

internal static class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectoryW(string lpPathName);

    private static void SetupVLCPath()
    {
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
        SetupVLCPath();
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            return CompositionSmoke.Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

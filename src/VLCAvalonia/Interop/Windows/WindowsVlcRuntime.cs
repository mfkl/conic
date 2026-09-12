using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsVlcRuntime
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectoryW(string path);

    public static void Initialize()
    {
        NativePlaybackSupport.RequireAvailable();
        string directory = RepositoryPaths.VLCDirectory;
        _ = RepositoryPaths.PluginPath;
        if (!SetDllDirectoryW(directory))
            throw new IOException("Windows could not configure the VLC playback directory.");
        NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        NativeLibrary.Load(RepositoryPaths.PublicLibraryPath);
    }
}

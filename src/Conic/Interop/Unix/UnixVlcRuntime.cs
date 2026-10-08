using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace Conic.Interop.Unix;

/// <summary>Loads VLC 3 on Linux and macOS.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe class UnixVlcRuntime
{
    private const string PluginPathVariable = "VLC_PLUGIN_PATH";
    private const string DataPathVariable = "VLC_DATA_PATH";

    public static void Initialize()
    {
        NativeEngineSupport.RequireAvailable();
        string runtime = RepositoryPaths.VLCDirectory;
        string plugin = RepositoryPaths.PluginPath;

        // VLC reads VLC_PLUGIN_PATH and VLC_DATA_PATH from the native environment
        // during libvlc_InternalInit. Environment.SetEnvironmentVariable only
        // updates .NET's copy, so set them through libc.
        List<string> pluginDirectories = [];
        // On Linux VLC finds its own modules from libvlccore's location. On macOS
        // it does so only inside VLC.app's own executable, so name them.
        if (OperatingSystem.IsMacOS())
            pluginDirectories.Add(RepositoryPaths.ModuleDirectory);
        string pluginDirectory = Path.GetDirectoryName(plugin)!;
        bool scanned = OperatingSystem.IsLinux()
            ? IsWithin(pluginDirectory, runtime)
            : pluginDirectories.Any(directory => IsWithin(pluginDirectory, directory));
        if (!scanned)
            pluginDirectories.Add(pluginDirectory);
        if (pluginDirectories.Count != 0)
        {
            if (Environment.GetEnvironmentVariable(PluginPathVariable) is { Length: > 0 } existing)
                pluginDirectories.Add(existing);
            SetNativeEnvironment(PluginPathVariable, string.Join(':', pluginDirectories));
        }
        if (RepositoryPaths.DataDirectory is { } data && Environment.GetEnvironmentVariable(DataPathVariable) is null or "")
            SetNativeEnvironment(DataPathVariable, data);

        // Distribution packages ship versioned sonames only (libvlccore.so.9), and
        // VLC.app is outside every library search path, so load them by path.
        VLCCore.UseLibraries(
            NativeLibrary.Load(RepositoryPaths.CoreLibraryPath),
            NativeLibrary.Load(RepositoryPaths.PublicLibraryPath));
    }

    private static bool IsWithin(string directory, string root) =>
        Path.GetRelativePath(root, directory) is var relative &&
        relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(relative);

    private static void SetNativeEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        var setenv = (delegate* unmanaged[Cdecl]<byte*, byte*, int, int>)
            NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "setenv");
        nint nativeName = Marshal.StringToCoTaskMemUTF8(name);
        nint nativeValue = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            if (setenv((byte*)nativeName, (byte*)nativeValue, 1) != 0)
                throw new IOException($"Could not set {name} for VLC.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeName);
            Marshal.FreeCoTaskMem(nativeValue);
        }
    }
}

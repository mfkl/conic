using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace Conic.Interop.Linux;

[SupportedOSPlatform("linux")]
internal static unsafe class LinuxVlcRuntime
{
    private const string PluginPathVariable = "VLC_PLUGIN_PATH";

    public static void Initialize()
    {
        NativeEngineSupport.RequireAvailable();
        string runtime = RepositoryPaths.VLCDirectory;
        string plugin = RepositoryPaths.PluginPath;

        // VLC reads VLC_PLUGIN_PATH from the native environment during
        // libvlc_InternalInit. Environment.SetEnvironmentVariable only updates
        // .NET's copy, so set it through libc.
        string pluginDirectory = Path.GetDirectoryName(plugin)!;
        if (!IsWithin(pluginDirectory, runtime))
        {
            string? existing = Environment.GetEnvironmentVariable(PluginPathVariable);
            string value = string.IsNullOrEmpty(existing) ? pluginDirectory : pluginDirectory + ":" + existing;
            SetNativeEnvironment(PluginPathVariable, value);
        }

        // Distribution packages ship versioned sonames only (libvlccore.so.9),
        // which the default "libvlccore" probing does not find.
        nint core = NativeLibrary.Load(RepositoryPaths.CoreLibraryPath);
        nint library = NativeLibrary.Load(RepositoryPaths.PublicLibraryPath);
        NativeLibrary.SetDllImportResolver(typeof(VLCCore).Assembly, (name, _, _) => name switch
        {
            "libvlccore" => core,
            "libvlc" => library,
            _ => 0,
        });
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

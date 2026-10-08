using System.Runtime.InteropServices;

namespace VLCAvalonia.Interop;

internal enum VlcRuntimePlatform { Windows, Linux, MacOS }

/// <summary>Filesystem discovery only. Finding a runtime does not qualify its ABI or GPU backend.</summary>
internal sealed class VlcRuntimeLocator(
    string baseDirectory,
    VlcRuntimePlatform platform,
    string architecture,
    string? configuredRuntime = null,
    string? configuredFramework = null,
    IReadOnlyList<string>? systemDirectories = null)
{
    public static VlcRuntimePlatform CurrentPlatform => OperatingSystem.IsWindows() ? VlcRuntimePlatform.Windows
        : OperatingSystem.IsLinux() ? VlcRuntimePlatform.Linux
        : OperatingSystem.IsMacOS() ? VlcRuntimePlatform.MacOS
        : throw new PlatformNotSupportedException("VLC Avalonia targets Windows, Linux and macOS.");

    public static VlcRuntimeLocator FromEnvironment() => new(
        AppContext.BaseDirectory, CurrentPlatform,
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        Environment.GetEnvironmentVariable("VLC_DIRECTORY"),
        Environment.GetEnvironmentVariable("VLCLR_ROOT"));

    public string RuntimeIdentifier => $"{platform switch { VlcRuntimePlatform.Windows => "win", VlcRuntimePlatform.Linux => "linux", _ => "osx" }}-{architecture}";
    public string PluginFileName => platform switch
    {
        VlcRuntimePlatform.Windows => "libvlc_avalonia_plugin.dll",
        VlcRuntimePlatform.Linux => "libvlc_avalonia_plugin.so",
        _ => "libvlc_avalonia_plugin.dylib",
    };

    private string[] LibraryNames(bool core) => (platform, core) switch
    {
        (VlcRuntimePlatform.Windows, true) => ["libvlccore.dll"],
        (VlcRuntimePlatform.Windows, false) => ["libvlc.dll"],
        (VlcRuntimePlatform.Linux, true) => ["libvlccore.so.9", "libvlccore.so"],
        (VlcRuntimePlatform.Linux, false) => ["libvlc.so.5", "libvlc.so"],
        (VlcRuntimePlatform.MacOS, true) => ["libvlccore.9.dylib", "libvlccore.dylib"],
        _ => ["libvlc.5.dylib", "libvlc.dylib"],
    };

    public string? RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(Path.GetFullPath(baseDirectory)); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "vlc-avalonia.slnx"))) return directory.FullName;
            return null;
        }
    }

    public string FrameworkRoot
    {
        get
        {
            string? root = FrameworkCandidate;
            if (root is not null && (File.Exists(Path.Combine(root, "vlclr.sln")) || File.Exists(Path.Combine(root, "vlclr.slnx")))) return root;
            throw new DirectoryNotFoundException("Set VLCLR_ROOT to the pinned VLCLR checkout for development and smoke fixtures.");
        }
    }

    private string? FrameworkCandidate => !string.IsNullOrWhiteSpace(configuredFramework) ? Path.GetFullPath(configuredFramework)
        : RepositoryRoot is { } root ? Path.GetFullPath(Path.Combine(root, "..", "vlclr")) : null;

    private string? FindLibrary(string directory, bool core) => LibraryNames(core)
        .Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public string RuntimeDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(configuredRuntime))
            {
                string directory = Path.GetFullPath(configuredRuntime);
                return HasLibraries(directory) ? directory : throw new DirectoryNotFoundException(
                    $"VLC_DIRECTORY '{directory}' must contain the VLC 3 libraries for {RuntimeIdentifier}.");
            }
            List<string> candidates = [Path.Combine(baseDirectory, "vlc"), baseDirectory];
            if (platform == VlcRuntimePlatform.MacOS)
                candidates.Add(Path.GetFullPath(Path.Combine(baseDirectory, "..", "Frameworks", "vlc")));
            if (candidates.FirstOrDefault(HasLibraries) is { } bundled) return Path.GetFullPath(bundled);
            // Development fallback: the pinned runtime downloaded by eng/get-vlc.ps1.
            if (RepositoryRoot is { } repository) candidates.Add(Path.Combine(repository, "artifacts", "dependencies", "vlc-3.0.24"));
            // Linux: the distribution's VLC 3 packages, after any bundled or development runtime.
            candidates.AddRange(systemDirectories ?? SystemDirectories);
            return candidates.FirstOrDefault(HasLibraries) is { } found ? Path.GetFullPath(found)
                : throw new DirectoryNotFoundException($"Set VLC_DIRECTORY or bundle VLC 3 in the application's vlc directory for {RuntimeIdentifier}.");
        }
    }

    private string[] SystemDirectories => (platform, architecture) switch
    {
        (VlcRuntimePlatform.Linux, "x64") => ["/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib"],
        (VlcRuntimePlatform.Linux, "arm64") => ["/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib"],
        _ => [],
    };

    private bool HasLibraries(string directory) => FindLibrary(directory, true) is not null && FindLibrary(directory, false) is not null;
    public string CoreLibraryPath => FindLibrary(RuntimeDirectory, true)!;
    public string PublicLibraryPath => FindLibrary(RuntimeDirectory, false)!;

    public string PluginPath
    {
        get
        {
            // VLC 3 on Linux looks for plugins in <libvlccore directory>/vlc/plugins.
            string runtimePlugins = platform == VlcRuntimePlatform.Linux
                ? Path.Combine(RuntimeDirectory, "vlc", "plugins")
                : Path.Combine(RuntimeDirectory, "plugins");
            List<string> candidates = [Path.Combine(runtimePlugins, "video_output", PluginFileName)];
            // Packages that use the distribution's VLC ship the plugin beside the app.
            if (platform == VlcRuntimePlatform.Linux)
                candidates.Add(Path.Combine(baseDirectory, "plugins", PluginFileName));
            if (RepositoryRoot is { } root)
                foreach (string configuration in new[] { "Release", "Debug" })
                    candidates.Add(Path.Combine(root, "src", "VLCAvalonia.Vout", "bin", configuration, "net10.0", RuntimeIdentifier, "native", PluginFileName));
            return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(
                $"Install the {RuntimeIdentifier} video plugin in the VLC runtime. A compatible hardware backend is required.", candidates[0]);
        }
    }
}

using System.Runtime.InteropServices;

namespace Conic.Interop;

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
        : throw new PlatformNotSupportedException("Conic targets Windows, Linux and macOS.");

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
                if (File.Exists(Path.Combine(directory.FullName, "conic.slnx"))) return directory.FullName;
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
            {
                // Inside an app bundle, Contents/Frameworks/vlc. Either the
                // libraries sit in that directory, or it mirrors VLC.app's
                // Contents/MacOS: lib/ beside plugins/ and share/.
                string frameworks = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Frameworks", "vlc"));
                candidates.AddRange([frameworks, Path.Combine(frameworks, "lib"), Path.Combine(baseDirectory, "vlc", "lib")]);
            }
            if (candidates.FirstOrDefault(HasLibraries) is { } bundled) return Path.GetFullPath(bundled);
            // Development fallback: the pinned runtime downloaded by eng/get-vlc.ps1.
            if (RepositoryRoot is { } repository)
                candidates.Add(platform switch
                {
                    VlcRuntimePlatform.MacOS => Path.Combine(repository, "artifacts", "dependencies", $"vlc-3.0.24-macos-{architecture}", "lib"),
                    VlcRuntimePlatform.Linux => Path.Combine(repository, "artifacts", "dependencies", "vlc-3.0.24-linux"),
                    _ => Path.Combine(repository, "artifacts", "dependencies", "vlc-3.0.24"),
                });
            // Linux: the VLC snap, then the distribution's VLC 3 packages; macOS:
            // an installed VLC.app. Both come after any bundled or development runtime.
            candidates.AddRange(systemDirectories ?? SystemDirectories);
            return candidates.FirstOrDefault(HasLibraries) is { } found ? Path.GetFullPath(found)
                : throw new DirectoryNotFoundException($"Set VLC_DIRECTORY or bundle VLC 3 in the application's vlc directory for {RuntimeIdentifier}.");
        }
    }

    internal string[] SystemDirectories => (platform, architecture) switch
    {
        (VlcRuntimePlatform.Linux, "x64") => [SnapRuntime, "/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib"],
        (VlcRuntimePlatform.Linux, "arm64") => ["/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib"],
        (VlcRuntimePlatform.MacOS, _) =>
        [
            "/Applications/VLC.app/Contents/MacOS/lib",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "VLC.app", "Contents", "MacOS", "lib"),
        ],
        _ => [],
    };

    /// <summary>
    /// VideoLAN's VLC snap, preferred to the distribution's packages: it is
    /// VLC 3.0.24 with its VA-API decoder, which distribution builds before
    /// 3.0.22 lack.
    /// </summary>
    public const string SnapRuntime = "/snap/vlc/current/usr/lib";

    private bool HasLibraries(string directory) => FindLibrary(directory, true) is not null && FindLibrary(directory, false) is not null;
    public string CoreLibraryPath => FindLibrary(RuntimeDirectory, true)!;
    public string PublicLibraryPath => FindLibrary(RuntimeDirectory, false)!;

    /// <summary>
    /// macOS: the directory holding VLC's own modules, which the host passes to
    /// VLC. VLC 3 derives it from libvlccore's location only inside VLC.app
    /// itself (<c>src/darwin/dirs.c</c>); elsewhere it looks in
    /// <c>&lt;libvlccore directory&gt;/vlc/plugins</c>, as on Linux.
    /// </summary>
    public string ModuleDirectory => FindModuleDirectory() ?? throw new DirectoryNotFoundException(
        $"The VLC runtime in '{RuntimeDirectory}' has no plugins directory.");

    private string? FindModuleDirectory()
    {
        string runtime = RuntimeDirectory;
        string[] candidates = platform switch
        {
            VlcRuntimePlatform.Linux => [Path.Combine(runtime, "vlc", "plugins")],
            VlcRuntimePlatform.MacOS =>
            [
                Path.Combine(runtime, "plugins"),
                Path.GetFullPath(Path.Combine(runtime, "..", "plugins")),
                Path.Combine(runtime, "vlc", "plugins"),
            ],
            _ => [Path.Combine(runtime, "plugins")],
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>VLC's data directory (Lua scripts and other resources), when the runtime has its own.</summary>
    public string? DataDirectory => platform switch
    {
        VlcRuntimePlatform.MacOS => new[] { Path.Combine(RuntimeDirectory, "share"), Path.GetFullPath(Path.Combine(RuntimeDirectory, "..", "share")) }
            .FirstOrDefault(Directory.Exists),
        VlcRuntimePlatform.Linux => new[] { Path.Combine(RuntimeDirectory, "share", "vlc"), Path.GetFullPath(Path.Combine(RuntimeDirectory, "..", "share", "vlc")) }
            .FirstOrDefault(Directory.Exists),
        _ => null,
    };

    /// <summary>
    /// Linux: libraries a bundled runtime carries for VLC beyond what the
    /// desktop provides, in <c>deps/</c>, in the order <c>deps/load-order.txt</c>
    /// lists them (each after those it needs), as eng/get-vlc.ps1 assembles it.
    /// </summary>
    /// <remarks>Loaded by path before VLC, they satisfy VLC's dependencies by soname.</remarks>
    public IReadOnlyList<string> DependencyLibraries => platform == VlcRuntimePlatform.Linux && Path.Combine(RuntimeDirectory, "deps") is var deps
        && File.Exists(Path.Combine(deps, "load-order.txt"))
        ? File.ReadAllLines(Path.Combine(deps, "load-order.txt")).Where(name => name.Length > 0).Select(name => Path.Combine(deps, name)).ToArray()
        : [];

    /// <summary>
    /// Linux: libraries VLC's plugins link to that live beside them rather
    /// than in a library search path (<c>vlc/libvlc_pulse.so.0</c> and others).
    /// </summary>
    public IReadOnlyList<string> HelperLibraries => platform == VlcRuntimePlatform.Linux && Directory.Exists(Path.Combine(RuntimeDirectory, "vlc"))
        ? Directory.GetFiles(Path.Combine(RuntimeDirectory, "vlc"), "libvlc_*.so.*").Order(StringComparer.Ordinal).ToArray()
        : [];

    public string PluginPath
    {
        get
        {
            // VLC 3 on Linux looks for plugins in <libvlccore directory>/vlc/plugins.
            string runtimePlugins = platform == VlcRuntimePlatform.Linux
                ? Path.Combine(RuntimeDirectory, "vlc", "plugins")
                : Path.Combine(RuntimeDirectory, "plugins");
            List<string> candidates = [Path.Combine(runtimePlugins, "video_output", PluginFileName)];
            // macOS bundles that mirror VLC.app keep plugins/ beside lib/.
            if (platform == VlcRuntimePlatform.MacOS && FindModuleDirectory() is { } modules)
                candidates.Add(Path.Combine(modules, "video_output", PluginFileName));
            // Packages that use the distribution's VLC or an installed VLC.app,
            // which must not be modified, ship the plugin beside the app.
            if (platform is VlcRuntimePlatform.Linux or VlcRuntimePlatform.MacOS)
                candidates.Add(Path.Combine(baseDirectory, "plugins", PluginFileName));
            if (RepositoryRoot is { } root)
                foreach (string configuration in new[] { "Release", "Debug" })
                    candidates.Add(Path.Combine(root, "src", "Conic.Vout", "bin", configuration, "net10.0", RuntimeIdentifier, "native", PluginFileName));
            return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(
                $"Install the {RuntimeIdentifier} video plugin in the VLC runtime. A compatible hardware backend is required.", candidates[0]);
        }
    }
}

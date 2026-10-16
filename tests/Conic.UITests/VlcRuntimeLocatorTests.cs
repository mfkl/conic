using System.Runtime.InteropServices;
using Conic.Interop;
using Xunit;

namespace Conic.UITests;

public sealed class VlcRuntimeLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vlc-runtime-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Windows", "x64", "libvlccore.dll", "libvlc.dll", "libvlc_avalonia_plugin.dll")]
    [InlineData("Linux", "x64", "libvlccore.so.9", "libvlc.so.5", "libvlc_avalonia_plugin.so")]
    [InlineData("Linux", "arm64", "libvlccore.so", "libvlc.so", "libvlc_avalonia_plugin.so")]
    [InlineData("MacOS", "arm64", "libvlccore.9.dylib", "libvlc.5.dylib", "libvlc_avalonia_plugin.dylib")]
    [InlineData("MacOS", "x64", "libvlccore.dylib", "libvlc.dylib", "libvlc_avalonia_plugin.dylib")]
    public void InstalledRuntimeAndPluginNeedNoRepository(string platform, string architecture, string core, string library, string plugin)
    {
        string runtime = Path.Combine(_root, "vlc");
        Touch(runtime, core); Touch(runtime, library);
        string plugins = platform == "Linux" ? Path.Combine(runtime, "vlc", "plugins") : Path.Combine(runtime, "plugins");
        string pluginPath = Touch(Path.Combine(plugins, "video_output"), plugin);
        VlcRuntimeLocator locator = new(_root, Enum.Parse<VlcRuntimePlatform>(platform), architecture,
            configuredFramework: "unused\0invalid-checkout");
        Assert.Null(locator.RepositoryRoot);
        Assert.Equal(runtime, locator.RuntimeDirectory);
        Assert.Equal(Path.Combine(runtime, core), locator.CoreLibraryPath);
        Assert.Equal(Path.Combine(runtime, library), locator.PublicLibraryPath);
        Assert.Equal(pluginPath, locator.PluginPath);
    }

    [Fact]
    public void ExplicitRuntimeWinsAndInvalidOverrideDoesNotSilentlyUseBundle()
    {
        string bundled = Path.Combine(_root, "vlc");
        string configured = Path.Combine(_root, "custom");
        foreach (string directory in new[] { bundled, configured })
        {
            Touch(directory, "libvlccore.dll"); Touch(directory, "libvlc.dll");
        }
        Assert.Equal(configured, new VlcRuntimeLocator(_root, VlcRuntimePlatform.Windows, "x64", configured).RuntimeDirectory);
        Assert.Throws<DirectoryNotFoundException>(() => new VlcRuntimeLocator(_root, VlcRuntimePlatform.Windows, "x64", Path.Combine(_root, "missing")).RuntimeDirectory);
    }

    [Fact]
    public void MacBundleFrameworkLayoutIsResolved()
    {
        string executableDirectory = Path.Combine(_root, "Player.app", "Contents", "MacOS");
        string runtime = Path.Combine(_root, "Player.app", "Contents", "Frameworks", "vlc");
        Touch(runtime, "libvlccore.9.dylib"); Touch(runtime, "libvlc.5.dylib");
        Assert.Equal(runtime, new VlcRuntimeLocator(executableDirectory, VlcRuntimePlatform.MacOS, "arm64").RuntimeDirectory);
    }

    [Fact]
    public void DevelopmentPluginLookupUsesRequestedArchitecture()
    {
        Touch(_root, "conic.slnx");
        string runtime = Path.Combine(_root, "vlc");
        Touch(runtime, "libvlccore.dylib"); Touch(runtime, "libvlc.dylib");
        VlcRuntimeLocator locator = new(_root, VlcRuntimePlatform.MacOS, "arm64");
        string outputs = Path.Combine(_root, "src", "Conic.Vout", "bin", "Release", "net10.0");
        Touch(Path.Combine(outputs, "win-x64", "native"), "libvlc_avalonia_plugin.dll");
        Assert.Throws<FileNotFoundException>(() => locator.PluginPath);
        string expected = Touch(Path.Combine(outputs, "osx-arm64", "native"), "libvlc_avalonia_plugin.dylib");
        Assert.Equal(expected, locator.PluginPath);
    }

    [Fact]
    public void ExplicitRuntimeDoesNotNeedAFrameworkCheckout()
    {
        string runtime = Path.Combine(_root, "external-runtime");
        Touch(runtime, "libvlccore.so.9"); Touch(runtime, "libvlc.so.5");
        string plugin = Touch(Path.Combine(runtime, "vlc", "plugins", "video_output"), "libvlc_avalonia_plugin.so");
        VlcRuntimeLocator locator = new(Path.Combine(_root, "app"), VlcRuntimePlatform.Linux, "x64", runtime, "unused\0invalid-checkout");
        Assert.Equal(plugin, locator.PluginPath);
        Assert.Equal(runtime, locator.RuntimeDirectory);
    }

    [Theory]
    [InlineData("Linux", Architecture.Arm64)]
    [InlineData("Windows", Architecture.Arm64)]
    public void LibraryDiscoveryDoesNotEnableAnUnimplementedHardwareBackend(string platform, Architecture architecture)
    {
        Assert.NotNull(NativePlaybackSupport.UnavailableReason(Enum.Parse<VlcRuntimePlatform>(platform), architecture));
        Assert.Null(NativePlaybackSupport.UnavailableReason(VlcRuntimePlatform.Windows, Architecture.X64));
    }

    [Theory]
    [InlineData(Architecture.Arm64)]
    [InlineData(Architecture.X64)]
    public void MacOSHasTheIOSurfaceBackend(Architecture architecture) =>
        Assert.Null(NativePlaybackSupport.UnavailableReason(VlcRuntimePlatform.MacOS, architecture));

    [Fact]
    public void LinuxX64HasTheDmaBufBackend() =>
        Assert.Null(NativePlaybackSupport.UnavailableReason(VlcRuntimePlatform.Linux, Architecture.X64));

    [Fact]
    public void LinuxFallsBackToTheDistributionRuntimeAfterBundledOnes()
    {
        string system = Path.Combine(_root, "usr", "lib", "x86_64-linux-gnu");
        Touch(system, "libvlccore.so.9"); Touch(system, "libvlc.so.5");
        string app = Path.Combine(_root, "app");
        Assert.Equal(system, new VlcRuntimeLocator(app, VlcRuntimePlatform.Linux, "x64", systemDirectories: [system]).RuntimeDirectory);

        string bundled = Path.Combine(app, "vlc");
        Touch(bundled, "libvlccore.so.9"); Touch(bundled, "libvlc.so.5");
        Assert.Equal(bundled, new VlcRuntimeLocator(app, VlcRuntimePlatform.Linux, "x64", systemDirectories: [system]).RuntimeDirectory);
    }

    [Fact]
    public void LinuxPackagesUsingTheDistributionRuntimeShipThePluginBesideTheApp()
    {
        string system = Path.Combine(_root, "usr", "lib", "x86_64-linux-gnu");
        Touch(system, "libvlccore.so.9"); Touch(system, "libvlc.so.5");
        string app = Path.Combine(_root, "app");
        VlcRuntimeLocator locator = new(app, VlcRuntimePlatform.Linux, "x64", systemDirectories: [system]);
        Assert.Throws<FileNotFoundException>(() => locator.PluginPath);
        string plugin = Touch(Path.Combine(app, "plugins"), "libvlc_avalonia_plugin.so");
        Assert.Equal(plugin, locator.PluginPath);
    }

    [Theory]
    [InlineData("Windows", Architecture.X64, true)]
    [InlineData("Linux", Architecture.X64, true)]
    [InlineData("Linux", Architecture.Arm64, false)]
    [InlineData("MacOS", Architecture.Arm64, true)]
    [InlineData("MacOS", Architecture.X64, true)]
    [InlineData("Windows", Architecture.Arm64, false)]
    public void TheEngineRunsWhereItsNativeCallsAreImplemented(string platform, Architecture architecture, bool available)
    {
        Assert.Equal(available, NativeEngineSupport.UnavailableReason(Enum.Parse<VlcRuntimePlatform>(platform), architecture) is null);
    }

    [Fact]
    public void MacOSFallsBackToAnInstalledVlcAppAndShipsThePluginBesideTheApp()
    {
        string contents = Path.Combine(_root, "Applications", "VLC.app", "Contents", "MacOS");
        string library = Path.Combine(contents, "lib");
        Touch(library, "libvlccore.dylib"); Touch(library, "libvlc.dylib");
        Touch(Path.Combine(contents, "plugins"), "libavcodec_plugin.dylib");
        Directory.CreateDirectory(Path.Combine(contents, "share", "lua"));
        string app = Path.Combine(_root, "app");
        VlcRuntimeLocator locator = new(app, VlcRuntimePlatform.MacOS, "arm64", systemDirectories: [library]);
        Assert.Equal(library, locator.RuntimeDirectory);
        Assert.Equal(Path.Combine(contents, "plugins"), locator.ModuleDirectory);
        Assert.Equal(Path.Combine(contents, "share"), locator.DataDirectory);
        Assert.Throws<FileNotFoundException>(() => locator.PluginPath);
        string plugin = Touch(Path.Combine(app, "plugins"), "libvlc_avalonia_plugin.dylib");
        Assert.Equal(plugin, locator.PluginPath);
    }

    [Fact]
    public void MacOSBundlesMayMirrorVlcAppInsideFrameworks()
    {
        string executableDirectory = Path.Combine(_root, "Player.app", "Contents", "MacOS");
        string vlc = Path.Combine(_root, "Player.app", "Contents", "Frameworks", "vlc");
        string library = Path.Combine(vlc, "lib");
        Touch(library, "libvlccore.9.dylib"); Touch(library, "libvlc.5.dylib");
        string plugin = Touch(Path.Combine(vlc, "plugins", "video_output"), "libvlc_avalonia_plugin.dylib");
        VlcRuntimeLocator locator = new(executableDirectory, VlcRuntimePlatform.MacOS, "arm64", systemDirectories: []);
        Assert.Equal(library, locator.RuntimeDirectory);
        Assert.Equal(Path.Combine(vlc, "plugins"), locator.ModuleDirectory);
        Assert.Null(locator.DataDirectory);
        Assert.Equal(plugin, locator.PluginPath);
    }

    private static string Touch(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "filesystem fixture only; not a native binary");
        return path;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

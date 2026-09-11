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
        string pluginPath = Touch(Path.Combine(runtime, "plugins", "video_output"), plugin);
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
        string plugin = Touch(Path.Combine(runtime, "plugins", "video_output"), "libvlc_avalonia_plugin.so");
        VlcRuntimeLocator locator = new(Path.Combine(_root, "app"), VlcRuntimePlatform.Linux, "x64", runtime, "unused\0invalid-checkout");
        Assert.Equal(plugin, locator.PluginPath);
        Assert.Equal(runtime, locator.RuntimeDirectory);
    }

    [Theory]
    [InlineData("Linux", Architecture.X64)]
    [InlineData("MacOS", Architecture.Arm64)]
    [InlineData("Windows", Architecture.Arm64)]
    public void LibraryDiscoveryDoesNotEnableAnUnimplementedHardwareBackend(string platform, Architecture architecture)
    {
        Assert.NotNull(NativePlaybackSupport.UnavailableReason(Enum.Parse<VlcRuntimePlatform>(platform), architecture));
        Assert.Null(NativePlaybackSupport.UnavailableReason(VlcRuntimePlatform.Windows, Architecture.X64));
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

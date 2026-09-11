namespace Conic.Interop;

internal static class RepositoryPaths
{
    private static readonly VlcRuntimeLocator Locator = VlcRuntimeLocator.FromEnvironment();

    // Development-only paths are lazy so installed applications need no checkout.
    public static string Root => Locator.RepositoryRoot ?? throw new DirectoryNotFoundException(
        "This development smoke requires the Conic repository.");
    public static string VLCLRRoot => Locator.FrameworkRoot;
    public static string VLCDirectory => Locator.RuntimeDirectory;
    public static string CoreLibraryPath => Locator.CoreLibraryPath;
    public static string PublicLibraryPath => Locator.PublicLibraryPath;
    public static string PluginPath => Locator.PluginPath;
    public static string TestMediaPath => Environment.GetEnvironmentVariable("CONIC_TEST_MEDIA") is { Length: > 0 } configured
        ? Path.GetFullPath(configured) : Path.Combine(VLCLRRoot, "test_01.mkv");
}

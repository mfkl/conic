namespace Conic.Interop;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string VLCLRRoot { get; } = FindVLCLRRoot();

    public static string VLCDirectory { get; } = FindVLCDirectory();

    public static string TestMediaPath =>
        Environment.GetEnvironmentVariable("CONIC_TEST_MEDIA") is
            { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(VLCLRRoot, "test_01.mkv");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "conic.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Conic repository root.");
    }

    private static string FindVLCLRRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("VLCLR_ROOT");
        string candidate = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(Root, "..", "vlclr"))
            : Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(candidate, "vlclr.sln")))
        {
            throw new DirectoryNotFoundException(
                $"Could not locate the sibling VLCLR repository at {candidate}.");
        }

        return candidate;
    }

    private static string FindVLCDirectory()
    {
        string? configured =
            Environment.GetEnvironmentVariable("VLC_DIRECTORY");
        string candidate = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(VLCLRRoot, "vlc-3.0.24-beta1")
            : Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(candidate, "libvlccore.dll")))
        {
            throw new DirectoryNotFoundException(
                $"Could not locate the VLC runtime at {candidate}.");
        }

        return candidate;
    }
}

namespace Conic.Services;

internal static class PlayerProfile
{
    public static string FilePath(string name)
    {
        string? configured = Environment.GetEnvironmentVariable("CONIC_PROFILE");
        string directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Conic")
            : Path.GetFullPath(configured);
        return Path.Combine(directory, name);
    }
}

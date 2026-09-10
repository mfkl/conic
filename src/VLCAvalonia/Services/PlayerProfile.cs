namespace VLCAvalonia.Services;

internal static class PlayerProfile
{
    public static string FilePath(string name)
    {
        string? configured = Environment.GetEnvironmentVariable("VLC_AVALONIA_PROFILE");
        string directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VLCAvalonia")
            : Path.GetFullPath(configured);
        return Path.Combine(directory, name);
    }
}

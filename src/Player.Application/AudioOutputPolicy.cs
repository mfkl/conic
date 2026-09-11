namespace Player.Application;

public static class AudioOutputPolicy
{
    private static readonly IReadOnlyList<string> WindowsOutputs = Array.AsReadOnly(new[] { "Default", "mmdevice", "directsound", "waveout" });
    private static readonly IReadOnlyList<string> AutomaticOutput = Array.AsReadOnly(new[] { "Default" });

    // Add explicit Unix module choices only when the pinned runtime supports them.
    public static IReadOnlyList<string> Options(bool? windows = null) =>
        (windows ?? OperatingSystem.IsWindows()) ? WindowsOutputs : AutomaticOutput;

    public static string Effective(string configured, bool? windows = null) =>
        Options(windows).Contains(configured, StringComparer.Ordinal) ? configured : "Default";
}

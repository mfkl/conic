using System.Globalization;
using System.Text;

namespace Player.Application;

/// <summary>Jump to Time: a time typed as seconds, m:ss or h:mm:ss, as VLC 3's dialog takes it.</summary>
public static class PlaybackTime
{
    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Trim().Split(':');
        if (parts.Length > 3) return false;
        double seconds = 0;
        for (int index = 0; index < parts.Length; index++)
        {
            bool last = index == parts.Length - 1;
            // Only the seconds may have a fraction; minutes and seconds after a colon stay below 60.
            if (!double.TryParse(parts[index], last ? NumberStyles.AllowDecimalPoint : NumberStyles.None, CultureInfo.InvariantCulture, out double value) || value < 0) return false;
            if (index > 0 && value >= 60) return false;
            seconds = seconds * 60 + value;
        }
        if (!double.IsFinite(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds / 2) return false;
        time = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>How the dialog shows a time: h:mm:ss, or m:ss under an hour.</summary>
    public static string Format(TimeSpan time) => time.TotalHours >= 1
        ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + time.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
        : ((int)time.TotalMinutes).ToString(CultureInfo.InvariantCulture) + time.ToString(@"\:ss", CultureInfo.InvariantCulture);

    /// <summary>VLC 3's fine speed step (its "[" and "]" keys): the next tenth up or down, within VLC's range.</summary>
    public static double FineRate(double rate, int direction)
    {
        double next = Math.Floor(Math.Abs(rate) / 0.1 + direction + 0.05) * 0.1;
        return Math.Clamp(Math.Round(next, 2), 1d / 32, 31.25);
    }
}

/// <summary>Media named on the command line, as "conic movie.mkv" or a file manager's "Open with" passes it.</summary>
public static class CommandLineSources
{
    /// <summary>Every argument that isn't an option: options (--smoke, macOS's -psn_…) are skipped, and everything after "--" is media.</summary>
    public static IReadOnlyList<string> From(IEnumerable<string>? args)
    {
        List<string> sources = [];
        bool optionsEnded = false;
        foreach (string arg in args ?? [])
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (!optionsEnded && arg == "--") { optionsEnded = true; continue; }
            if (!optionsEnded && arg.StartsWith('-')) continue;
            sources.Add(arg);
        }
        return sources;
    }

    /// <summary>
    /// The files of an open-files event that the command line did not already open. macOS's
    /// AppKit hands the files named on the command line to the app again, in one or more
    /// open-files events; opening them twice leaves the window on an open error. Each file
    /// named on the command line is skipped once; <paramref name="pending"/> keeps the rest.
    /// </summary>
    public static IReadOnlyList<string> NotOnCommandLine(IEnumerable<string> opened, List<string> pending, string workingDirectory) =>
        opened.Where(file => !pending.Remove(SameFileKey(file, workingDirectory))).ToArray();

    /// <summary>The command line's files that AppKit will hand over again: existing files and folders, not URLs.</summary>
    public static List<string> EchoedByMacOS(IEnumerable<string> commandLine, string workingDirectory) =>
        commandLine.Where(source => !NetworkSources.TryGetScheme(source, out _))
            .Select(source => SameFileKey(source, workingDirectory))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();

    private static string SameFileKey(string source, string workingDirectory)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile && !Path.IsPathRooted(source)) return source;
        string path = uri is { IsFile: true } ? uri.LocalPath : source;
        try { path = Path.GetFullPath(path, workingDirectory); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        return path.TrimEnd(Path.DirectorySeparatorChar).Normalize(NormalizationForm.FormC);
    }
}

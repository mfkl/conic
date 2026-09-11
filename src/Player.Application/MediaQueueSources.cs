using System.Text;

namespace Player.Application;

public sealed record MediaSourceExpansion(IReadOnlyList<MediaSource> Sources, IReadOnlyList<string> Failures, bool WasBounded);

/// <summary>Bounded, cancellable source expansion. It deliberately flattens folders and playlists.</summary>
public sealed class MediaSourceExpander
{
    public const int DefaultMaximumItems = 2_000;
    private readonly MediaSourceValidator _validator;
    public MediaSourceExpander(MediaSourceValidator validator) => _validator = validator;

    public async Task<MediaSourceExpansion> ExpandAsync(string value, MediaSourceKind kind, int maximumItems = DefaultMaximumItems, CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > DefaultMaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        if (kind != MediaSourceKind.Folder) {
            MediaOpenResult single = _validator.Validate(value, kind);
            return single.Succeeded && single.Source is not null ? new([single.Source], [], false) : new([], [single.UserMessage ?? "Media could not be opened."], false);
        }

        List<MediaSource> sources = []; List<string> failures = []; bool bounded = false;
        try
        {
            foreach (string file in Directory.EnumerateFiles(value, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sources.Count == maximumItems) { bounded = true; break; }
                MediaOpenResult result = _validator.Validate(file, MediaSourceKind.File);
                if (result.Succeeded && result.Source is not null) sources.Add(result.Source); else failures.Add(result.UserMessage ?? file);
                await Task.Yield();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { failures.Add("Some folder items could not be read."); }
        return new(sources, failures, bounded);
    }
}

/// <summary>Strict, bounded M3U/M3U8 reader/writer; entries are flattened and never interpreted as nested playlists.</summary>
public static class M3uPlaylistDocument
{
    public const int MaximumEntries = 2_000;
    public static IReadOnlyList<string> Parse(string content, int maximumEntries = MaximumEntries)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (content.Length > 2_000_000 || maximumEntries is < 1 or > MaximumEntries) throw new InvalidDataException("Playlist is too large.");
        List<string> entries = [];
        using StringReader reader = new(content);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Length > 16_384 || line.IndexOf('\0') >= 0) throw new InvalidDataException("Playlist contains an invalid entry.");
            if (entries.Count >= maximumEntries) throw new InvalidDataException("Playlist has too many entries.");
            entries.Add(line);
        }
        return entries;
    }

    public static string Write(IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        string[] values = sources.Where(x => !string.IsNullOrWhiteSpace(x)).Take(MaximumEntries + 1).ToArray();
        if (values.Length > MaximumEntries || values.Any(x => x.IndexOfAny(['\r', '\n', '\0']) >= 0)) throw new InvalidDataException("Playlist entries are invalid or too numerous.");
        StringBuilder builder = new("#EXTM3U\n"); foreach (string value in values) builder.AppendLine(value); return builder.ToString();
    }
}

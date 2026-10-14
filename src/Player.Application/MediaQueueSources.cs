using System.Text;

namespace Player.Application;

public sealed record MediaSourceExpansion(IReadOnlyList<MediaSource> Sources, IReadOnlyList<string> Failures, bool WasBounded);

/// <summary>How a folder is read (Settings › Library › Opening Files):
/// with its subfolders or not, with hidden and system files or not.</summary>
public sealed record MediaFolderScan(bool Subfolders, bool Hidden)
{
    public static MediaFolderScan Everything { get; } = new(true, true);

    public static MediaFolderScan For(PlayerSettings settings) => new(settings.FolderOpening != "FolderOnly", settings.ShowHiddenFiles);
}

/// <summary>Bounded, cancellable source expansion. It deliberately flattens folders and playlists.</summary>
public sealed class MediaSourceExpander
{
    public const int DefaultMaximumItems = 2_000;
    private readonly MediaSourceValidator _validator;
    public MediaSourceExpander(MediaSourceValidator validator) => _validator = validator;

    public Task<MediaSourceExpansion> ExpandAsync(string value, MediaSourceKind kind, int maximumItems = DefaultMaximumItems, CancellationToken cancellationToken = default) =>
        ExpandAsync(value, kind, MediaFolderScan.Everything, maximumItems, cancellationToken);

    public async Task<MediaSourceExpansion> ExpandAsync(string value, MediaSourceKind kind, MediaFolderScan scan, int maximumItems = DefaultMaximumItems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (maximumItems is < 1 or > DefaultMaximumItems) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        if (kind != MediaSourceKind.Folder) {
            MediaOpenResult single = _validator.Validate(value, kind);
            return single.Succeeded && single.Source is not null ? new([single.Source], [], false) : new([], [single.UserMessage ?? "Media could not be opened."], false);
        }

        List<MediaSource> sources = []; List<string> failures = []; bool bounded = false;
        try
        {
            // As SearchOption did (inaccessible folders are reported), plus the
            // scan's choices; a hidden folder's files are skipped with it.
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = scan.Subfolders, IgnoreInaccessible = false, MatchType = MatchType.Win32,
                AttributesToSkip = scan.Hidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            };
            foreach (string file in Directory.EnumerateFiles(value, "*", options).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
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

/// <summary>
/// The playlist formats Load and Save Playlist take, as VLC 3 offers them:
/// M3U and M3U8, XSPF (VLC's own) and, to read, PLS. Entries are flattened
/// and bounded like <see cref="M3uPlaylistDocument"/>'s.
/// </summary>
public static class PlaylistDocument
{
    public static IReadOnlyList<string> Parse(string content, string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xspf" => ParseXspf(content),
        ".pls" => ParsePls(content),
        _ => M3uPlaylistDocument.Parse(content),
    };

    public static string Write(IEnumerable<string> sources, string path) =>
        Path.GetExtension(path).Equals(".xspf", StringComparison.OrdinalIgnoreCase) ? WriteXspf(sources) : M3uPlaylistDocument.Write(sources);

    /// <summary>XSPF: each track's location, a URI (local files as file URIs).</summary>
    public static string WriteXspf(IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        string[] values = sources.Where(x => !string.IsNullOrWhiteSpace(x)).Take(M3uPlaylistDocument.MaximumEntries + 1).ToArray();
        if (values.Length > M3uPlaylistDocument.MaximumEntries || values.Any(x => x.IndexOf('\0') >= 0)) throw new InvalidDataException("Playlist entries are invalid or too numerous.");
        System.Xml.Linq.XNamespace ns = "http://xspf.org/ns/0/";
        System.Xml.Linq.XDocument document = new(new System.Xml.Linq.XDeclaration("1.0", "UTF-8", null),
            new System.Xml.Linq.XElement(ns + "playlist", new System.Xml.Linq.XAttribute("version", "1"),
                new System.Xml.Linq.XElement(ns + "trackList", values.Select(value =>
                    new System.Xml.Linq.XElement(ns + "track", new System.Xml.Linq.XElement(ns + "location", Location(value)))))));
        return document.Declaration + "\n" + document.Root;
    }

    private static string Location(string source) =>
        NetworkSources.TryGetScheme(source, out _) || Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? source
        : Path.IsPathRooted(source) ? new Uri(source).AbsoluteUri : Uri.EscapeDataString(source).Replace("%2F", "/", StringComparison.Ordinal);

    public static IReadOnlyList<string> ParseXspf(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > 4_000_000) throw new InvalidDataException("Playlist is too large.");
        System.Xml.XmlReaderSettings settings = new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 };
        try
        {
            using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new StringReader(content), settings);
            System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Load(reader);
            List<string> entries = [];
            foreach (System.Xml.Linq.XElement location in document.Descendants().Where(element => element.Name.LocalName == "location"))
            {
                string value = location.Value.Trim();
                if (value.Length == 0) continue;
                if (entries.Count >= M3uPlaylistDocument.MaximumEntries) throw new InvalidDataException("Playlist has too many entries.");
                entries.Add(Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : Uri.UnescapeDataString(value));
            }
            return entries;
        }
        catch (System.Xml.XmlException exception) { throw new InvalidDataException("The XSPF playlist is not valid XML.", exception); }
    }

    /// <summary>PLS: the FileN= entries, in their numbered order.</summary>
    public static IReadOnlyList<string> ParsePls(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > 2_000_000) throw new InvalidDataException("Playlist is too large.");
        SortedDictionary<int, string> files = [];
        using StringReader reader = new(content);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            int equals = line.IndexOf('=');
            if (!line.StartsWith("File", StringComparison.OrdinalIgnoreCase) || equals <= 4) continue;
            if (!int.TryParse(line.AsSpan(4, equals - 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number)) continue;
            string value = line[(equals + 1)..].Trim();
            if (value.Length == 0 || value.IndexOf('\0') >= 0) continue;
            if (files.Count >= M3uPlaylistDocument.MaximumEntries) throw new InvalidDataException("Playlist has too many entries.");
            files[number] = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : value;
        }
        return files.Values.ToArray();
    }
}

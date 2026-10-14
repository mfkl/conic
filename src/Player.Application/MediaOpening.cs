using System.Text.Json;

namespace Player.Application;

public enum MediaSourceKind { File, Folder, NetworkUrl, Recent, DragDrop }
public sealed record MediaSource(string CanonicalSource, MediaSourceKind Kind, string DisplayName);
public sealed record MediaOpenResult(bool Succeeded, MediaSource? Source, string? UserMessage, string? Diagnostic)
{
    public static MediaOpenResult Fail(string message, string diagnostic) => new(false, null, message, diagnostic);
}

/// <summary>File-system seam keeps source validation deterministic and prevents UI code from guessing source rules.</summary>
public interface IMediaFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string GetFullPath(string path);
}

public sealed class SystemMediaFileSystem : IMediaFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string GetFullPath(string path) => Path.GetFullPath(path);
}

public sealed class MediaSourceValidator(IMediaFileSystem fileSystem)
{
    public MediaOpenResult Validate(string? value, MediaSourceKind requestedKind)
    {
        if (string.IsNullOrWhiteSpace(value)) return MediaOpenResult.Fail("Choose a media file or enter a URL.", "The source was empty.");
        value = value.Trim();
        bool network = NetworkSources.TryGetScheme(value, out string scheme);
        if (requestedKind == MediaSourceKind.NetworkUrl || network)
        {
            if (!network || !NetworkSources.IsSupported(scheme))
                return MediaOpenResult.Fail(NetworkSources.UnsupportedMessage, "Unsupported URI scheme or malformed URL.");
            bool parsed = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri);
            if (parsed && !string.IsNullOrEmpty(uri!.UserInfo))
                return MediaOpenResult.Fail("URLs with embedded credentials are not supported.", "The URL contains user-info credentials.");
            if (scheme is "http" or "https")
            {
                if (!parsed) return MediaOpenResult.Fail(NetworkSources.UnsupportedMessage, "Malformed URL.");
                UriBuilder builder = new(uri!) { Scheme = scheme, Host = uri!.Host.ToLowerInvariant(), Fragment = string.Empty };
                if ((builder.Scheme == "http" && builder.Port == 80) || (builder.Scheme == "https" && builder.Port == 443)) builder.Port = -1;
                Uri canonical = builder.Uri;
                string authority = canonical.Scheme + "://" + canonical.Host + (canonical.IsDefaultPort ? string.Empty : ":" + canonical.Port);
                string display = authority + canonical.PathAndQuery;
                return new(true, new(canonical.AbsoluteUri, MediaSourceKind.NetworkUrl, display), null, null);
            }
            // Other schemes reach VLC as typed (scheme in lower case): VLC
            // parses them itself, and .NET can't parse some, such as udp://@:1234.
            if (!parsed && !NetworkSources.IsListenAddress(value, scheme))
                return MediaOpenResult.Fail(NetworkSources.UnsupportedMessage, "Malformed URL.");
            string source = scheme + value[scheme.Length..];
            return new(true, new(source, MediaSourceKind.NetworkUrl, source), null, null);
        }
        try
        {
            string path = fileSystem.GetFullPath(value);
            if (requestedKind == MediaSourceKind.Folder || fileSystem.DirectoryExists(path))
            {
                if (!fileSystem.DirectoryExists(path)) return MediaOpenResult.Fail("That folder is unavailable.", $"Folder does not exist or cannot be accessed: {path}");
                return new(true, new(path, MediaSourceKind.Folder, Path.GetFileName(path)), null, null);
            }
            if (!fileSystem.FileExists(path)) return MediaOpenResult.Fail("That media file is unavailable.", $"File does not exist or cannot be accessed: {path}");
            return new(true, new(path, MediaSourceKind.File, Path.GetFileName(path)), null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or UnauthorizedAccessException or IOException)
        { return MediaOpenResult.Fail("That media file cannot be opened.", exception.Message); }
    }

    public static string Key(MediaSource source) => MediaSourceIdentity.Current.Key(source);
}

/// <summary>
/// The network URLs Conic hands to VLC: the schemes VLC 3's Open Network
/// Stream takes for playback. Credentials in the URL are refused.
/// </summary>
public static class NetworkSources
{
    public static IReadOnlyList<string> Schemes { get; } = ["http", "https", "ftp", "ftps", "sftp", "smb", "rtsp", "rtp", "udp", "mms", "mmsh", "rtmp"];

    public const string UnsupportedMessage = "Enter a network URL: http, https, ftp, sftp, smb, rtsp, rtp, udp, mms or rtmp.";

    public static bool IsSupported(string scheme) => Schemes.Contains(scheme);

    /// <summary>True when <paramref name="value"/> is written scheme://…, other than file://; the scheme comes back in lower case.</summary>
    public static bool TryGetScheme(string value, out string scheme)
    {
        scheme = string.Empty;
        int separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator < 2 || !char.IsAsciiLetter(value[0])) return false;
        for (int index = 1; index < separator; index++)
            if (!char.IsAsciiLetterOrDigit(value[index]) && value[index] is not ('+' or '-' or '.')) return false;
        scheme = value[..separator].ToLowerInvariant();
        if (scheme == "file") { scheme = string.Empty; return false; }
        return true;
    }

    /// <summary>VLC's listening form for UDP and RTP, such as udp://@:1234, which .NET can't parse.</summary>
    public static bool IsListenAddress(string value, string scheme) =>
        scheme is "udp" or "rtp" && value.AsSpan(scheme.Length + 3).StartsWith("@") && !value.AsSpan(scheme.Length + 3).ContainsAny(' ', '#');
}

public static class MediaEngineSourceNormalizer
{
    public static string Normalize(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (NetworkSources.TryGetScheme(source, out string scheme))
        {
            bool parsed = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri);
            if (!NetworkSources.IsSupported(scheme) || (!parsed && !NetworkSources.IsListenAddress(source, scheme)) ||
                (parsed && (!string.IsNullOrEmpty(uri!.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))))
                throw new ArgumentException("Only credential-free URLs with a supported scheme are supported.", nameof(source));
            return source;
        }
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? file) && file.IsFile)
            return Path.GetFullPath(file.LocalPath);
        return Path.GetFullPath(source);
    }
}

public sealed record RecentMediaEntry(string Source, string DisplayName, DateTimeOffset LastOpenedUtc);
public sealed record ResumeEntry(string Source, TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedUtc);
/// <summary>An explicit user-curated library entry. The player never scans folders to create these.</summary>
public sealed record FavoriteMediaEntry(string Source, string DisplayName, DateTimeOffset AddedUtc);
public sealed record MediaHistoryDocument(int Version, List<RecentMediaEntry> Recent, List<ResumeEntry> Resume, List<FavoriteMediaEntry>? Favorites = null)
{
    public const int CurrentVersion = 2;
    public static MediaHistoryDocument Empty { get; } = new(CurrentVersion, [], [], []);
}
public interface IMediaHistoryStorage { string? Read(); void WriteAtomically(string content); void PreserveInvalid(string content) { } }

public sealed class MediaHistoryService(IMediaHistoryStorage storage, Func<DateTimeOffset>? clock = null, MediaSourceIdentity? identity = null)
{
    private readonly MediaSourceIdentity _identity = identity ?? MediaSourceIdentity.Current;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private MediaHistoryDocument _document = MediaHistoryDocument.Empty;
    public IReadOnlyList<RecentMediaEntry> Recent => _document.Recent;
    public IReadOnlyList<ResumeEntry> Resume => _document.Resume;
    public IReadOnlyList<FavoriteMediaEntry> Favorites => _document.Favorites ?? [];
    public string? LoadDiagnostic { get; private set; }
    public void Load()
    {
        string? raw;
        try { raw = storage.Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _document = MediaHistoryDocument.Empty; LoadDiagnostic = "Media history could not be read."; return; }
        if (string.IsNullOrWhiteSpace(raw)) { _document = MediaHistoryDocument.Empty; LoadDiagnostic = null; return; }
        try
        {
            MediaHistoryDocument? parsed = JsonSerializer.Deserialize<MediaHistoryDocument>(raw, Options);
            if (parsed is null || parsed.Version is < 1 or > MediaHistoryDocument.CurrentVersion) throw new JsonException("Unsupported media history version.");
            _document = Normalize(parsed);
            if (RequiresWriteBack(parsed, _document))
            {
                try { Save(_document); LoadDiagnostic = null; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { LoadDiagnostic = "Media history was loaded but could not be normalized on disk."; }
            }
            else LoadDiagnostic = null;
        }
        catch (JsonException) { try { storage.PreserveInvalid(raw); storage.WriteAtomically(JsonSerializer.Serialize(MediaHistoryDocument.Empty, Options)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } _document = MediaHistoryDocument.Empty; LoadDiagnostic = "Media history was invalid and has been reset."; }
    }
    public void RecordOpened(MediaSource source, bool enabled)
    {
        if (!enabled) return;
        string key = _identity.Key(source);
        List<RecentMediaEntry> recent = _document.Recent.Where(x => !Same(x.Source, key)).ToList();
        recent.Insert(0, new(source.CanonicalSource, source.DisplayName, _clock()));
        Save(_document with { Recent = recent.Take(50).ToList() });
    }
    public void Checkpoint(MediaSource source, TimeSpan position, TimeSpan duration, bool enabled, int minimumElapsedSeconds, int maximumRemainingSeconds, bool seekable = true)
    {
        if (!enabled || duration > TimeSpan.Zero && duration - position <= TimeSpan.FromSeconds(maximumRemainingSeconds)) { RemoveResumeIfPresent(source); return; }
        if (!seekable || duration <= TimeSpan.Zero || position < TimeSpan.FromSeconds(minimumElapsedSeconds)) return;
        string key = _identity.Key(source);
        List<ResumeEntry> entries = _document.Resume.Where(x => !Same(x.Source, key)).ToList();
        entries.Insert(0, new(source.CanonicalSource, position, duration, _clock())); Save(_document with { Resume = entries.Take(50).ToList() });
    }
    public ResumeEntry? FindResume(MediaSource source) => _document.Resume.FirstOrDefault(x => Same(x.Source, _identity.Key(source)));
    public ResumeEntry? FindEligibleResume(MediaSource source, int minimumElapsedSeconds, int maximumRemainingSeconds)
    {
        ResumeEntry? entry = FindResume(source);
        return entry is not null && entry.Position >= TimeSpan.FromSeconds(minimumElapsedSeconds) && entry.Duration - entry.Position > TimeSpan.FromSeconds(maximumRemainingSeconds) ? entry : null;
    }
    public void RemoveResume(string source) => Save(_document with { Resume = _document.Resume.Where(x => !Same(x.Source, _identity.KeyFromCanonicalSource(source))).ToList() });
    public void RemoveRecent(string source) => Save(_document with { Recent = _document.Recent.Where(x => !Same(x.Source, _identity.KeyFromCanonicalSource(source))).ToList() });
    public void ClearRecent() => Save(_document with { Recent = [] });
    public void ClearResume() => Save(_document with { Resume = [] });
    public bool IsFavorite(string source) => Favorites.Any(x => Same(x.Source, _identity.KeyFromCanonicalSource(source)));
    public void AddFavorite(MediaSource source)
    {
        string key = _identity.Key(source);
        List<FavoriteMediaEntry> favorites = Favorites.Where(x => !Same(x.Source, key)).ToList();
        favorites.Insert(0, new FavoriteMediaEntry(source.CanonicalSource, source.DisplayName, _clock()));
        Save(_document with { Favorites = favorites.Take(50).ToList() });
    }
    public void RemoveFavorite(string source) => Save(_document with { Favorites = Favorites.Where(x => !Same(x.Source, _identity.KeyFromCanonicalSource(source))).ToList() });
    public void ClearFavorites() => Save(_document with { Favorites = [] });
    public void ClearAll() => Save(MediaHistoryDocument.Empty);
    private void RemoveResumeIfPresent(MediaSource source)
    {
        string key = _identity.Key(source);
        if (_document.Resume.Any(x => Same(x.Source, key))) RemoveResume(source.CanonicalSource);
    }
    private bool Same(string source, string key)
    {
        string sourceKey = _identity.KeyFromCanonicalSource(source);
        return string.Equals(sourceKey, key, StringComparison.Ordinal);
    }
    public static string KeyFromCanonicalSource(string source) => MediaSourceIdentity.Current.KeyFromCanonicalSource(source);
    private void Save(MediaHistoryDocument document) { storage.WriteAtomically(JsonSerializer.Serialize(document, Options)); _document = document; }
    private static bool RequiresWriteBack(MediaHistoryDocument parsed, MediaHistoryDocument normalized) =>
        parsed.Version != MediaHistoryDocument.CurrentVersion ||
        parsed.Favorites is null ||
        !(parsed.Recent ?? []).SequenceEqual(normalized.Recent) ||
        !(parsed.Resume ?? []).SequenceEqual(normalized.Resume) ||
        !parsed.Favorites.SequenceEqual(normalized.Favorites ?? []);
    private MediaHistoryDocument Normalize(MediaHistoryDocument value)
    {
        List<RecentMediaEntry> recent = (value.Recent ?? [])
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.DisplayName))
            .Select(x => x with { Source = x.Source.Trim(), DisplayName = x.DisplayName.Trim() })
            .GroupBy(x => _identity.KeyFromCanonicalSource(x.Source), StringComparer.Ordinal).Select(x => x.OrderByDescending(y => y.LastOpenedUtc).First()).OrderByDescending(x => x.LastOpenedUtc).Take(50).ToList();
        List<ResumeEntry> resume = (value.Resume ?? [])
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Source) && x.Position >= TimeSpan.Zero && x.Duration > TimeSpan.Zero && x.Position < x.Duration)
            .Select(x => x with { Source = x.Source.Trim() })
            .GroupBy(x => _identity.KeyFromCanonicalSource(x.Source), StringComparer.Ordinal).Select(x => x.OrderByDescending(y => y.UpdatedUtc).First()).OrderByDescending(x => x.UpdatedUtc).Take(50).ToList();
        List<FavoriteMediaEntry> favorites = (value.Favorites ?? [])
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.DisplayName))
            .Select(x => x with { Source = x.Source.Trim(), DisplayName = x.DisplayName.Trim() })
            .GroupBy(x => _identity.KeyFromCanonicalSource(x.Source), StringComparer.Ordinal).Select(x => x.OrderByDescending(y => y.AddedUtc).First()).OrderByDescending(x => x.AddedUtc).Take(50).ToList();
        return new(MediaHistoryDocument.CurrentVersion, recent, resume, favorites);
    }
}

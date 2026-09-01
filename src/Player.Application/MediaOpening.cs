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
        if (requestedKind == MediaSourceKind.NetworkUrl || Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme is not ("http" or "https"))
                return MediaOpenResult.Fail("Enter an http or https media URL.", "Unsupported URI scheme or malformed URL.");
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return MediaOpenResult.Fail("URLs with embedded credentials are not supported.", "The URL contains user-info credentials.");
            UriBuilder builder = new(uri) { Scheme = uri.Scheme.ToLowerInvariant(), Host = uri.Host.ToLowerInvariant(), Fragment = string.Empty };
            if ((builder.Scheme == "http" && builder.Port == 80) || (builder.Scheme == "https" && builder.Port == 443)) builder.Port = -1;
            Uri canonical = builder.Uri;
            string authority = canonical.Scheme + "://" + canonical.Host + (canonical.IsDefaultPort ? string.Empty : ":" + canonical.Port);
            string display = authority + canonical.PathAndQuery;
            return new(true, new(canonical.AbsoluteUri, MediaSourceKind.NetworkUrl, display), null, null);
        }
        try
        {
            string path = fileSystem.GetFullPath(value);
            if (requestedKind == MediaSourceKind.Folder || fileSystem.DirectoryExists(path))
                return MediaOpenResult.Fail("Opening folders requires playlist support, which is not available yet.", "Folder queue capability Q1 is unavailable.");
            if (!fileSystem.FileExists(path)) return MediaOpenResult.Fail("That media file is unavailable.", $"File does not exist or cannot be accessed: {path}");
            return new(true, new(path, MediaSourceKind.File, Path.GetFileName(path)), null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or UnauthorizedAccessException or IOException)
        { return MediaOpenResult.Fail("That media file cannot be opened.", exception.Message); }
    }

    public static string Key(MediaSource source) => source.Kind == MediaSourceKind.NetworkUrl
        ? "url:" + source.CanonicalSource : "file:" + source.CanonicalSource.ToUpperInvariant();
}

public static class MediaEngineSourceNormalizer
{
    public static string Normalize(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Only credential-free http and https URLs are supported.", nameof(source));
            return source;
        }
        return Path.GetFullPath(source);
    }
}

public sealed record RecentMediaEntry(string Source, string DisplayName, DateTimeOffset LastOpenedUtc);
public sealed record ResumeEntry(string Source, TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedUtc);
public sealed record MediaHistoryDocument(int Version, List<RecentMediaEntry> Recent, List<ResumeEntry> Resume)
{
    public const int CurrentVersion = 1;
    public static MediaHistoryDocument Empty { get; } = new(CurrentVersion, [], []);
}
public interface IMediaHistoryStorage { string? Read(); void WriteAtomically(string content); void PreserveInvalid(string content) { } }

public sealed class MediaHistoryService(IMediaHistoryStorage storage, Func<DateTimeOffset>? clock = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private MediaHistoryDocument _document = MediaHistoryDocument.Empty;
    public IReadOnlyList<RecentMediaEntry> Recent => _document.Recent;
    public IReadOnlyList<ResumeEntry> Resume => _document.Resume;
    public string? LoadDiagnostic { get; private set; }
    public void Load()
    {
        string? raw;
        try { raw = storage.Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _document = MediaHistoryDocument.Empty; LoadDiagnostic = "Media history could not be read."; return; }
        if (string.IsNullOrWhiteSpace(raw)) { _document = MediaHistoryDocument.Empty; LoadDiagnostic = null; return; }
        try { _document = Normalize(JsonSerializer.Deserialize<MediaHistoryDocument>(raw, Options) is { Version: MediaHistoryDocument.CurrentVersion } value ? value : MediaHistoryDocument.Empty); LoadDiagnostic = null; }
        catch (JsonException) { try { storage.PreserveInvalid(raw); storage.WriteAtomically(JsonSerializer.Serialize(MediaHistoryDocument.Empty, Options)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } _document = MediaHistoryDocument.Empty; LoadDiagnostic = "Media history was invalid and has been reset."; }
    }
    public void RecordOpened(MediaSource source, bool enabled)
    {
        if (!enabled) return;
        string key = MediaSourceValidator.Key(source);
        List<RecentMediaEntry> recent = _document.Recent.Where(x => !Same(x.Source, key)).ToList();
        recent.Insert(0, new(source.CanonicalSource, source.DisplayName, _clock()));
        Save(_document with { Recent = recent.Take(50).ToList() });
    }
    public void Checkpoint(MediaSource source, TimeSpan position, TimeSpan duration, bool enabled, int minimumElapsedSeconds, int maximumRemainingSeconds, bool seekable = true)
    {
        if (!enabled || duration > TimeSpan.Zero && duration - position <= TimeSpan.FromSeconds(maximumRemainingSeconds)) { RemoveResumeIfPresent(source); return; }
        if (!seekable || duration <= TimeSpan.Zero || position < TimeSpan.FromSeconds(minimumElapsedSeconds)) return;
        string key = MediaSourceValidator.Key(source);
        List<ResumeEntry> entries = _document.Resume.Where(x => !Same(x.Source, key)).ToList();
        entries.Insert(0, new(source.CanonicalSource, position, duration, _clock())); Save(_document with { Resume = entries.Take(50).ToList() });
    }
    public ResumeEntry? FindResume(MediaSource source) => _document.Resume.FirstOrDefault(x => Same(x.Source, MediaSourceValidator.Key(source)));
    public ResumeEntry? FindEligibleResume(MediaSource source, int minimumElapsedSeconds, int maximumRemainingSeconds)
    {
        ResumeEntry? entry = FindResume(source);
        return entry is not null && entry.Position >= TimeSpan.FromSeconds(minimumElapsedSeconds) && entry.Duration - entry.Position > TimeSpan.FromSeconds(maximumRemainingSeconds) ? entry : null;
    }
    public void RemoveResume(string source) => Save(_document with { Resume = _document.Resume.Where(x => !Same(x.Source, KeyFromCanonicalSource(source))).ToList() });
    public void RemoveRecent(string source) => Save(_document with { Recent = _document.Recent.Where(x => !Same(x.Source, KeyFromCanonicalSource(source))).ToList() });
    public void ClearRecent() => Save(_document with { Recent = [] });
    public void ClearResume() => Save(_document with { Resume = [] });
    private void RemoveResumeIfPresent(MediaSource source)
    {
        string key = MediaSourceValidator.Key(source);
        if (_document.Resume.Any(x => Same(x.Source, key))) RemoveResume(source.CanonicalSource);
    }
    private static bool Same(string source, string key)
    {
        string sourceKey = KeyFromCanonicalSource(source);
        return string.Equals(sourceKey, key, StringComparison.Ordinal);
    }
    public static string KeyFromCanonicalSource(string source)
    {
        bool url = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile;
        return url ? "url:" + source : "file:" + source.ToUpperInvariant();
    }
    private void Save(MediaHistoryDocument document) { storage.WriteAtomically(JsonSerializer.Serialize(document, Options)); _document = document; }
    private static MediaHistoryDocument Normalize(MediaHistoryDocument value)
    {
        List<RecentMediaEntry> recent = (value.Recent ?? []).GroupBy(x => KeyFromCanonicalSource(x.Source), StringComparer.Ordinal).Select(x => x.OrderByDescending(y => y.LastOpenedUtc).First()).OrderByDescending(x => x.LastOpenedUtc).Take(50).ToList();
        List<ResumeEntry> resume = (value.Resume ?? []).GroupBy(x => KeyFromCanonicalSource(x.Source), StringComparer.Ordinal).Select(x => x.OrderByDescending(y => y.UpdatedUtc).First()).OrderByDescending(x => x.UpdatedUtc).Take(50).ToList();
        return new(MediaHistoryDocument.CurrentVersion, recent, resume);
    }
}

namespace Player.Application;

/// <summary>Comparison keys only; never changes the path passed to the filesystem or VLC.</summary>
public sealed class MediaSourceIdentity(bool caseSensitiveFiles)
{
    // Preserve spelling on Unix, including case-sensitive macOS volumes. Aliases
    // on an insensitive volume may remain separate, but distinct files cannot merge.
    public static MediaSourceIdentity Current { get; } = new(!OperatingSystem.IsWindows());

    public string FileKey(string path) => caseSensitiveFiles ? path : path.ToUpperInvariant();

    public string Key(MediaSource source) => source.Kind == MediaSourceKind.NetworkUrl
        ? "url:" + source.CanonicalSource : "file:" + FileKey(source.CanonicalSource);

    public string KeyFromCanonicalSource(string source)
    {
        bool url = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile;
        return url ? "url:" + source : "file:" + FileKey(source);
    }
}

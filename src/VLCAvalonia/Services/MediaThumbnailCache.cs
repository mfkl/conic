using System.Security.Cryptography;
using System.Text;

namespace VLCAvalonia.Services;

/// <summary>Local-file artwork, shared across Home sections. Never opens a playback session.</summary>
internal sealed class MediaThumbnailCache(string directory, Func<string, Task<byte[]?>> extract)
{
    private const int MemoryLimit = 64, DiskLimit = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<byte[]?>> _pending = [];
    public static MediaThumbnailCache Shared { get; } = new(PlayerProfile.FilePath("thumbnails"), WindowsMediaThumbnail.ReadAsync);

    public async Task<byte[]?> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        // File metadata and disk access also stay off the dispatcher.
        return await Task.Run(async () =>
        {
            var file = LocalFile(source);
            if (file is null) return null;
            string identity = $"v1|{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            Task<byte[]?> task;
            lock (_gate)
            {
                if (!_pending.TryGetValue(key, out task!))
                {
                    foreach (string old in _pending.Where(p => p.Value.IsCompleted).Select(p => p.Key).Take(Math.Max(0, _pending.Count - MemoryLimit + 1)).ToArray())
                        _pending.Remove(old);
                    task = Task.Run(() => LoadAsync(file.FullName, key));
                    // The native worker has a bounded queue; avoid retaining unbounded requests here.
                    if (_pending.Count < MemoryLimit) _pending.Add(key, task);
                }
            }
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> LoadAsync(string path, string key)
    {
        string cacheFile = Path.Combine(directory, key + ".png");
        try
        {
            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length is > 0 and <= 4 * 1024 * 1024)
                return await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        byte[]? bytes;
        try { bytes = await extract(path).ConfigureAwait(false); }
        catch (Exception) { return null; } // An unavailable OS codec must not break browsing.
        if (bytes is not { Length: > 0 }) return null;
        string temporary = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
            File.Move(temporary, cacheFile, overwrite: true);
            foreach (FileInfo old in new DirectoryInfo(directory).EnumerateFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(DiskLimit))
                old.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return bytes;
    }

    private static FileInfo? LocalFile(string source)
    {
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
            {
                if (!uri.IsFile || uri.IsUnc) return null;
                source = uri.LocalPath;
            }
            if (!Path.IsPathFullyQualified(source) || source.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            string fullPath = Path.GetFullPath(source);
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType == DriveType.Network) return null;
            FileInfo file = new(fullPath);
            return file.Exists ? file : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}

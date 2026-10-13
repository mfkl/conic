using Avalonia.Platform.Storage;
using Player.Application;

namespace Conic.Services;

internal sealed class StorageMediaSourcePicker(IStorageProvider storageProvider) : IMediaSourcePicker, IPlaylistDocumentPicker, IExternalTrackPicker
{
#if SMOKE
    /// <summary>
    /// --interactions-smoke: while set, every picker answers with the next
    /// of <see cref="PicksForSmoke"/> (null once they run out) instead of
    /// opening a dialog, and <see cref="RequestsForSmoke"/> records which
    /// picker was asked for.
    /// </summary>
    internal bool ScriptedForSmoke { get; set; }
    internal Queue<string?> PicksForSmoke { get; } = new();
    internal List<string> RequestsForSmoke { get; } = [];

    private bool Scripted(string request, out string? path)
    {
        path = null;
        if (!ScriptedForSmoke) return false;
        RequestsForSmoke.Add(request);
        PicksForSmoke.TryDequeue(out path);
        return true;
    }
#endif

    public async Task<string?> PickMediaAsync(CancellationToken cancellationToken = default)
    {
#if SMOKE
        if (Scripted("media", out string? scripted)) return scripted;
#endif
        FilePickerOpenOptions options = new()
        {
            Title = "Open Media File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media Files")
                {
                    Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.mp3", "*.flac" },
                },
                FilePickerFileTypes.All,
            },
        };
        IReadOnlyList<IStorageFile> result = await storageProvider.OpenFilePickerAsync(options);
        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if SMOKE
        if (Scripted("folder", out string? scripted)) return scripted;
#endif
        IReadOnlyList<IStorageFolder> result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select media folder",
            AllowMultiple = false,
        });
        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }

    public async Task<string?> PickPlaylistToOpenAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IStorageFile> result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load playlist", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("M3U playlists") { Patterns = new[] { "*.m3u", "*.m3u8" } } } });
        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }

    public async Task<string?> PickPlaylistToSaveAsync(CancellationToken cancellationToken = default)
    {
        IStorageFile? result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save playlist", SuggestedFileName = "playlist.m3u8", FileTypeChoices = new[] { new FilePickerFileType("M3U8 playlist") { Patterns = new[] { "*.m3u8" } } } });
        return result?.Path.LocalPath;
    }

    public async Task<string?> PickExternalTrackAsync(MediaTrackKind kind, CancellationToken cancellationToken = default)
    {
#if SMOKE
        if (Scripted(kind == MediaTrackKind.Subtitle ? "subtitle" : "audio", out string? scripted)) return scripted;
#endif
        string[] patterns = kind == MediaTrackKind.Subtitle
            ? ["*.srt", "*.ass", "*.ssa", "*.vtt", "*.sub"]
            : ["*.mp3", "*.m4a", "*.aac", "*.flac", "*.wav", "*.ogg"];
        IReadOnlyList<IStorageFile> result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == MediaTrackKind.Subtitle ? "Add external subtitle file" : "Add external audio file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(kind == MediaTrackKind.Subtitle ? "Subtitle files" : "Audio files") { Patterns = patterns }],
        });
        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }
}

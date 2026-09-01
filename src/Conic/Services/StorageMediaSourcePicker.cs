using Avalonia.Platform.Storage;
using Player.Application;

namespace Conic.Services;

internal sealed class StorageMediaSourcePicker(IStorageProvider storageProvider) : IMediaSourcePicker, IPlaylistDocumentPicker
{
    public async Task<string?> PickMediaAsync(CancellationToken cancellationToken = default)
    {
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
}

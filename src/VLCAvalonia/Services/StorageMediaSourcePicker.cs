using Avalonia.Platform.Storage;
using Player.Application;

namespace VLCAvalonia.Services;

internal sealed class StorageMediaSourcePicker(IStorageProvider storageProvider) : IMediaSourcePicker
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
}

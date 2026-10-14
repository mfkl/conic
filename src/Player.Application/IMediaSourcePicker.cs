namespace Player.Application;

/// <summary>Application boundary for choosing a media source from the host UI.</summary>
public interface IMediaSourcePicker
{
    Task<string?> PickMediaAsync(CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>Open File: one or more files. A host without a multiple picker gives the one file.</summary>
    async Task<IReadOnlyList<string>> PickMediaFilesAsync(CancellationToken cancellationToken = default) =>
        await PickMediaAsync(cancellationToken) is { Length: > 0 } path ? [path] : [];
}

public interface IPlaylistDocumentPicker
{
    Task<string?> PickPlaylistToOpenAsync(CancellationToken cancellationToken = default);
    Task<string?> PickPlaylistToSaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Host picker for a local external audio or subtitle file.</summary>
public interface IExternalTrackPicker
{
    Task<string?> PickExternalTrackAsync(MediaTrackKind kind, CancellationToken cancellationToken = default);
}

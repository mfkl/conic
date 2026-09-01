namespace Player.Application;

/// <summary>Application boundary for choosing a media source from the host UI.</summary>
public interface IMediaSourcePicker
{
    Task<string?> PickMediaAsync(CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}

public interface IPlaylistDocumentPicker
{
    Task<string?> PickPlaylistToOpenAsync(CancellationToken cancellationToken = default);
    Task<string?> PickPlaylistToSaveAsync(CancellationToken cancellationToken = default);
}

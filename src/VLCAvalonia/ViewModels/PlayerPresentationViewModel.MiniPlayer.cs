namespace VLCAvalonia.ViewModels;

// Home's floating mini player (design 970:3091): what is playing, where it
// lives, and whether the queue can move.
internal sealed partial class PlayerPresentationViewModel
{
    private string? _nowPlayingSource;

    /// <summary>The playing item's source (the queue's current item first), for the mini player's artwork.</summary>
    public string? NowPlayingSource
    {
        get => _nowPlayingSource;
        private set
        {
            if (!Set(ref _nowPlayingSource, value)) return;
            OnPropertyChanged(nameof(NowPlayingFolder));
            OnPropertyChanged(nameof(HasNowPlayingFolder));
        }
    }

    /// <summary>The name of the folder that holds the playing file, or the host of a network stream.</summary>
    public string NowPlayingFolder => FolderNameOf(NowPlayingSource);
    public bool HasNowPlayingFolder => NowPlayingFolder.Length != 0;
    /// <summary>Previous and next only mean something with more than one queued item.</summary>
    public bool HasQueueNavigation => QueueItems.Count > 1;

    private void UpdateNowPlaying() =>
        NowPlayingSource = QueueItems.FirstOrDefault(item => item.IsCurrent)?.Source ?? _currentSource?.CanonicalSource ?? _lastState?.Source;

    internal static string FolderNameOf(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        string path = source;
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile) return uri.Host;
            path = uri.LocalPath;
        }
        string? folder = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folder)) return string.Empty;
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length != 0 ? name : folder;
    }
}

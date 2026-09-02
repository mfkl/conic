namespace Player.Application;

public enum MediaInformationLoadState { Unavailable, Loading, Ready, Empty, Stale, Failed }
public sealed record MediaInformationState(long QueueGeneration, long MediaGeneration, MediaInformationLoadState Status, string Name, IReadOnlyDictionary<string, string> Metadata, string? Error = null)
{
    public static MediaInformationState Unavailable { get; } = new(0, 0, MediaInformationLoadState.Unavailable, "", new Dictionary<string, string>());
}

public interface IClipboardService { bool TrySetText(string text, out string? error); }
public interface IMediaInformationTransport
{
    MediaInformationState MediaInformation { get; }
    event EventHandler<MediaInformationState>? MediaInformationChanged;
    void RefreshMediaInformation();
}

public static class MediaDiagnostics
{
    public static string Build(MediaInformationState state)
    {
        var fields = new[] { "title", "artist", "album", "genre", "date", "description" }
            .Where(state.Metadata.ContainsKey).Select(key => key + "=" + state.Metadata[key].Replace('\r', ' ').Replace('\n', ' '));
        return "Conic diagnostics\nname=" + state.Name + "\nstatus=" + state.Status + "\ncodecStreams=unavailable-through-engine-ABI\n" + string.Join('\n', fields);
    }
}

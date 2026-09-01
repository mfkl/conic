namespace Player.Application;

public sealed record MediaInformationState(long Generation, string Name, IReadOnlyDictionary<string, string> Metadata, string Status, string? Error = null)
{
    public static MediaInformationState Unavailable { get; } = new(0, "", new Dictionary<string, string>(), "Unavailable");
}

public interface IClipboardService { bool TrySetText(string text, out string? error); }

public static class MediaDiagnostics
{
    public static string Build(MediaInformationState state)
    {
        var fields = new[] { "title", "artist", "album", "genre", "date", "description" }
            .Where(state.Metadata.ContainsKey).Select(key => key + "=" + state.Metadata[key].Replace('\r', ' ').Replace('\n', ' '));
        return "VLC Avalonia diagnostics\nname=" + state.Name + "\nstatus=" + state.Status + "\ncodecStreams=unavailable-through-engine-ABI\n" + string.Join('\n', fields);
    }
}

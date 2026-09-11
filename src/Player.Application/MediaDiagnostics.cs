namespace Player.Application;

public enum MediaInformationLoadState { Unavailable, Loading, Ready, Empty, Stale, Failed }
public sealed record MediaInformationField(string Name, string Value);
public sealed record MediaCodecStream
{
    public MediaCodecStream(int index, IReadOnlyDictionary<string, string> fields)
    {
        Index = index;
        Fields = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(fields));
    }
    public int Index { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public string Heading => $"Stream {Index}" + (Fields.TryGetValue("Type", out string? type) ? $" · {type}" : "");
    public IReadOnlyList<MediaInformationField> Details => Fields.Where(x => x.Key != "Type").Select(x => new MediaInformationField(x.Key, x.Value)).ToArray();
}

public sealed record MediaInformationState(long QueueGeneration, long MediaGeneration, MediaInformationLoadState Status, string Name, IReadOnlyDictionary<string, string> Metadata, string? Error = null, IReadOnlyList<MediaCodecStream>? CodecStreams = null)
{
    public static MediaInformationState Unavailable { get; } = new(0, 0, MediaInformationLoadState.Unavailable, "", new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));
    public IReadOnlyList<MediaCodecStream> Streams => CodecStreams ?? Array.Empty<MediaCodecStream>();
    public IReadOnlyList<MediaInformationField> MetadataFields => Metadata.Select(x => new MediaInformationField(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(x.Key), x.Value)).ToArray();
    public bool HasStreams => Streams.Count > 0;
    public string Summary => Status switch
    {
        MediaInformationLoadState.Loading => "Reading media information…",
        MediaInformationLoadState.Stale => "The media changed. Refresh to read its information.",
        MediaInformationLoadState.Failed => "Media information could not be read. Try refreshing.",
        MediaInformationLoadState.Unavailable => "Open media to view its information.",
        _ => HasStreams ? $"{Streams.Count} stream(s) · Reported by VLC" : "No codec streams have been reported yet. Refresh once playback starts."
    };
}

public interface IClipboardService { Task<string?> SetTextAsync(string text); }
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
            .Where(state.Metadata.ContainsKey).Select(key => key + "=" + Clean(state.Metadata[key]));
        string streams = string.Join('\n', state.Streams.SelectMany(stream => stream.Fields.Select(field => $"stream.{stream.Index}.{Clean(field.Key)}={Clean(field.Value)}")));
        return "VLC Avalonia diagnostics\nname=" + Clean(state.Name) + "\nstatus=" + state.Status + "\ncodecStreams=" + state.Streams.Count + "\n" + string.Join('\n', fields) + (streams.Length > 0 ? "\n" + streams : "");
    }

    private static string Clean(string? value)
    {
        string clean = string.Concat((value ?? string.Empty)
        .Replace('\r', ' ').Replace('\n', ' ')
        .Where(character => !char.IsControl(character)));
        return System.Text.RegularExpressions.Regex.Replace(clean, @"(?i)\b[a-z][a-z0-9+.-]*://\S+", "[redacted-uri]");
    }
}

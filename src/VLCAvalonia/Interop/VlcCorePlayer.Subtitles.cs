using Player.Application;
using VLCLR;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCAvalonia.Interop;

// Settings › Subtitles on a playing core (phase 9d). VLC's text renderer
// (freetype) reads the text size, color, outline thickness and background
// from its object's parents each time it lays out a line; the video output
// is the playlist's child, so these variables on the playlist reach it. A
// line already on screen keeps its look; the next one takes the new style.
// Each new core starts with the same values as options (OpenArguments).
internal sealed partial class VlcCorePlayer
{
    private static readonly string[] SubtitleVariables =
        ["sub-text-scale", "freetype-color", "freetype-outline-thickness", "freetype-background-opacity", "freetype-background-color"];
    private LiveSubtitleSettings? _liveSubtitles;
    // What the running core's text renderer reads: its startup options, then the live changes.
    private LiveSubtitleSettings? _appliedSubtitles;
    private bool _subtitleVariablesCreated;

    /// <summary>The subtitle style for the playing core (new cores start with it through <see cref="OpenArguments"/>).</summary>
    public LiveSubtitleSettings? LiveSubtitles
    {
        get { lock (_gate) return _liveSubtitles; }
        set
        {
            lock (_gate) _liveSubtitles = value;
            QueueMediaRequest(ApplyLiveSubtitles);
        }
    }

    // Called when a core starts with the current options.
    private void ResetAppliedSubtitles(LiveSubtitleSettings? startup)
    {
        _appliedSubtitles = startup;
        _subtitleVariablesCreated = false;
    }

    // Runs on the core's thread.
    private void ApplyLiveSubtitles()
    {
        LiveSubtitleSettings? wanted, applied;
        nint playlist;
        lock (_gate)
        {
            if (_tearingDown || _playlist == 0) return;
            (wanted, applied, playlist) = (_liveSubtitles, _appliedSubtitles, _playlist);
        }
        if (wanted is null || wanted == applied) return;
        // The playlist already has sub-text-scale; creating it again only
        // holds it. The others start from the core's options.
        if (!_subtitleVariablesCreated)
        {
            foreach (string name in SubtitleVariables) VLCCore.VarCreate(playlist, name, VLCVarType.Integer | VLCVarType.DoInherit);
            _subtitleVariablesCreated = true;
        }
        VLCVariable variables = new(playlist);
        variables.SetInteger("sub-text-scale", wanted.TextScale);
        variables.SetInteger("freetype-color", wanted.Color);
        variables.SetInteger("freetype-outline-thickness", wanted.OutlineThickness);
        variables.SetInteger("freetype-background-opacity", wanted.BackgroundOpacity);
        variables.SetInteger("freetype-background-color", wanted.BackgroundColor);
        lock (_gate) _appliedSubtitles = wanted;
    }

    /// <summary>The playing core's subtitle style variables (as the text renderer inherits them), for the smoke.</summary>
    internal IReadOnlyDictionary<string, long> ReadSubtitleVariablesForSmoke() => ReadIntegerVariablesForSmoke(SubtitleVariables);

    /// <summary>Integer variables as the playing core's objects inherit them (its options, then the live changes), for the smoke.</summary>
    internal IReadOnlyDictionary<string, long> ReadIntegerVariablesForSmoke(params string[] names) => InvokeMediaCapabilityForSmoke(_ =>
    {
        nint playlist;
        lock (_gate) playlist = _playlist;
        Dictionary<string, long> values = new(StringComparer.Ordinal);
        if (playlist == 0) return (IReadOnlyDictionary<string, long>)values;
        foreach (string name in names)
            if (VLCCore.VarInherit(playlist, name, VLCVarType.Integer, out VLCValueNative value) == 0) values[name] = value.Integer;
        return values;
    });
}

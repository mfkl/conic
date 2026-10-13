using Player.Application;
using VLCLR;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCAvalonia.Interop;

// Settings › Audio on a playing core (phase 9c). Each core starts with the
// audio options (OpenArguments); a change while one plays goes to its audio
// output's variables, as VLC's own equalizer and effects dialogs do: the
// equalizer and replay gain follow their variables at once, and a changed
// filter chain (or a parameter its filters read when they start) restarts
// the chain at the next audio block. The device switches through
// aout_DeviceSet.
internal sealed partial class VlcCorePlayer
{
    private LiveAudioSettings? _liveAudio;
    // What the running core's output has: its startup options, then the live changes.
    private LiveAudioSettings? _appliedAudio;
    private readonly HashSet<string> _audioVariables = new(StringComparer.Ordinal);

    /// <summary>The audio settings for the playing core (and the state new cores start in, through <see cref="OpenArguments"/>).</summary>
    public LiveAudioSettings? LiveAudio
    {
        get { lock (_gate) return _liveAudio; }
        set
        {
            lock (_gate) _liveAudio = value;
            QueueMediaRequest(ApplyLiveAudio);
        }
    }

    // Called when a core starts with the current options: its output already has these values.
    private void ResetAppliedAudio(LiveAudioSettings? startup)
    {
        _appliedAudio = startup;
        _audioVariables.Clear();
    }

    // Runs on the core's thread.
    private void ApplyLiveAudio()
    {
        LiveAudioSettings? wanted, applied;
        nint playlist;
        lock (_gate)
        {
            if (_tearingDown || _playlist == 0) return;
            (wanted, applied, playlist) = (_liveAudio, _appliedAudio, _playlist);
        }
        if (wanted is null || wanted == applied) return;
        nint output = VlcAudioApi.HoldOutput(playlist);
        if (output == 0) return;
        try
        {
            VLCVariable variables = new(output);
            SetAudioVariable(output, "equalizer-bands", VLCVarType.String, () => variables.SetString("equalizer-bands", wanted.EqualizerBands));
            SetAudioVariable(output, "equalizer-preamp", VLCVarType.Float, () => variables.SetFloat("equalizer-preamp", (float)wanted.EqualizerPreamp));
            SetAudioVariable(output, "norm-max-level", VLCVarType.Float, () => variables.SetFloat("norm-max-level", (float)wanted.NormalizationLevel));
            SetAudioVariable(output, "pitch-shift", VLCVarType.Float, () => variables.SetFloat("pitch-shift", (float)wanted.PitchShift));
            SetAudioVariable(output, "spatialaudio-headphones", VLCVarType.Bool, () =>
                VLCCore.VarSetChecked(output, "spatialaudio-headphones", VLCVarType.Bool, new VLCValueNative { Bool = wanted.SpatialHeadphones ? (byte)1 : (byte)0 }) == 0);
            SetAudioVariable(output, "audio-replay-gain-mode", VLCVarType.String, () => variables.SetString("audio-replay-gain-mode", wanted.ReplayGainMode));
            SetAudioVariable(output, "audio-replay-gain-preamp", VLCVarType.Float, () => variables.SetFloat("audio-replay-gain-preamp", (float)wanted.ReplayGainPreamp));

            // The output restarts its filters when "audio-filter" changes. A
            // parameter the filters read when they start needs that restart
            // too: an empty chain in between makes it.
            bool parameters = applied is null || wanted.NormalizationLevel != applied.NormalizationLevel ||
                wanted.PitchShift != applied.PitchShift || wanted.SpatialHeadphones != applied.SpatialHeadphones;
            if (applied is null || wanted.Filters != applied.Filters || (parameters && wanted.Filters.Length > 0))
            {
                if (applied?.Filters == wanted.Filters) variables.SetString("audio-filter", string.Empty);
                variables.SetString("audio-filter", wanted.Filters);
            }
            // The device is an MMDevice endpoint: only an output that started
            // with MMDevice (a known device) takes another one.
            if (wanted.Device is { } device && applied?.Device is { } current && device != current)
                VlcAudioApi.SetDevice(output, device);
            lock (_gate) _appliedAudio = wanted;
        }
        finally
        {
            VLCCore.ObjectRelease(output);
        }
    }

    // Filters create most of these variables only when they start; the
    // output keeps the value it is given for when they do.
    private void SetAudioVariable(nint output, string name, int type, Func<bool> set)
    {
        if (_audioVariables.Add(name)) VLCCore.VarCreate(output, name, type | VLCVarType.DoInherit);
        set();
    }

    /// <summary>The playing core's audio output: its filter chain, device and passthrough option, for the smoke.</summary>
    internal (string? Filters, string Device, bool Passthrough) ReadAudioOutputForSmoke() => InvokeMediaCapabilityForSmoke(_ =>
    {
        nint playlist;
        lock (_gate) playlist = _playlist;
        nint output = playlist == 0 ? 0 : VlcAudioApi.HoldOutput(playlist);
        if (output == 0) return ((string?)null, string.Empty, false);
        try
        {
            bool passthrough = VLCCore.VarInherit(output, "spdif", VLCVarType.Bool, out VLCValueNative value) == 0 && value.Bool != 0;
            return (new VLCVariable(output).GetString("audio-filter"), VlcAudioApi.Device(output), passthrough);
        }
        finally { VLCCore.ObjectRelease(output); }
    });
}

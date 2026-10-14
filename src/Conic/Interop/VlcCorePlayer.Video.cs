using Player.Application;
using Player.Contracts;
using VLCLR;

namespace Conic.Interop;

internal sealed partial class VlcCorePlayer
{
    private readonly VLCVideoPresentation _videoControls;
    private VideoAdjustmentState _videoAdjustments = VideoAdjustmentState.Unavailable;
    public VideoAdjustmentState VideoAdjustments { get { lock (_gate) return _videoAdjustments; } }
    public event EventHandler<VideoAdjustmentState>? VideoAdjustmentsChanged;

    public void SetVideoAdjustment(VideoAdjustmentState expected, string setting, string value) => QueueMediaRequest(() =>
    {
        if (State.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused)) return;
        var snapshot = _videoControls.Read(out _);
        if (snapshot is null || State.Generation != expected.QueueGeneration || snapshot.Generation.MediaGeneration != expected.MediaGeneration || snapshot.OutputToken != expected.OutputToken) return;
        var result = _videoControls.Set(snapshot, setting, value);
        Console.WriteLine($"[video-adjustment] {setting}={value} result={result.Code}");
        PublishVideoAdjustments();
    });

    private void PublishVideoAdjustments()
    {
        var snapshot = State.Status is PlaybackStatus.Playing or PlaybackStatus.Paused ? _videoControls.Read(out _) : null;
        var next = snapshot is null ? VideoAdjustmentState.Unavailable with { QueueGeneration = State.Generation }
            : new VideoAdjustmentState(State.Generation, snapshot.Generation.MediaGeneration, snapshot.OutputToken,
                Array.AsReadOnly(snapshot.Aspects.Select(x => new VideoChoice(x.Value, x.Label)).ToArray()),
                Array.AsReadOnly(snapshot.Crops.Select(x => new VideoChoice(x.Value, x.Label)).ToArray()),
                Array.AsReadOnly(new[] { new VideoChoice("fit", "Fit to window") }.Concat(snapshot.Zooms.Select(x => new VideoChoice(x.Value, x.Label))).ToArray()),
                snapshot.Aspect, snapshot.Crop, snapshot.AutoScale ? "fit" : snapshot.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));
        lock (_gate)
        {
            var old = _videoAdjustments;
            if (old.QueueGeneration == next.QueueGeneration && old.MediaGeneration == next.MediaGeneration && old.OutputToken == next.OutputToken && old.Aspect == next.Aspect && old.Crop == next.Crop && old.Zoom == next.Zoom && old.Aspects.SequenceEqual(next.Aspects) && old.Crops.SequenceEqual(next.Crops) && old.Zooms.SequenceEqual(next.Zooms)) return;
            _videoAdjustments = next;
        }
        VideoAdjustmentsChanged?.Invoke(this, next);
    }

    // Settings › Video on a playing core: VLC's filter chain, overlays,
    // deinterlacing and pixel crop go to the output (and the playlist, for
    // the next item's output), as VLC's Effects and Filters window does.
    private LiveVideoSettings? _liveVideo;
    // What the running core has: its startup options, then the live changes.
    private LiveVideoSettings? _appliedVideo;

    public LiveVideoSettings? LiveVideo
    {
        get { lock (_gate) return _liveVideo; }
        set
        {
            lock (_gate) _liveVideo = value;
            QueueMediaRequest(ApplyLiveVideo);
        }
    }

    private void ResetAppliedVideo(LiveVideoSettings? startup) => _appliedVideo = startup;

    // Runs on the core's thread. Only what changed is set: each set restarts
    // the filter chain, and the crop would replace a crop chosen in the Video panel.
    private void ApplyLiveVideo()
    {
        LiveVideoSettings? wanted, applied;
        lock (_gate)
        {
            if (_tearingDown || _playlist == 0) return;
            (wanted, applied) = (_liveVideo, _appliedVideo);
        }
        if (wanted is null || wanted.Equals(applied)) return;
        List<string> results = [];
        if (!wanted.SameFilters(applied))
            results.Add("filters=" + _videoControls.SetFilters(new VLCVideoFilterSettings(wanted.VideoFilter, wanted.SubSource, wanted.Parameters)).Code);
        if (applied is null || wanted.Deinterlace != applied.Deinterlace || wanted.DeinterlaceMethod != applied.DeinterlaceMethod)
            results.Add("deinterlace=" + _videoControls.SetDeinterlace(wanted.Deinterlace, wanted.DeinterlaceMethod).Code);
        if (applied is null ? wanted.Crop != default : wanted.Crop != applied.Crop)
            results.Add("crop=" + _videoControls.SetCropBorders(wanted.Crop.Left, wanted.Crop.Top, wanted.Crop.Right, wanted.Crop.Bottom).Code);
        Console.WriteLine($"[live-video] {DateTime.Now:HH:mm:ss.fff} {string.Join(' ', results)}");
        lock (_gate) _appliedVideo = wanted;
    }

    /// <summary>Whether the playing core has the last live video settings, for the smoke.</summary>
    internal bool LiveVideoAppliedForSmoke { get { lock (_gate) return _liveVideo is not null && _liveVideo.Equals(_appliedVideo); } }

    /// <summary>The playing output's filter chain, subpicture sources, deinterlace mode and crop, for the smoke.</summary>
    internal (string? Filters, string? Sources, long? Deinterlace, string? Crop) ReadVideoOutputForSmoke() => InvokeMediaCapabilityForSmoke(_ =>
        (_videoControls.ReadOutputString("video-filter"), _videoControls.ReadOutputString("sub-source"),
         _videoControls.ReadOutputInteger("deinterlace"), _videoControls.ReadOutputString("crop")));

    /// <summary>The playing audio output's stereo mode, for the smoke.</summary>
    internal long? ReadStereoModeForSmoke() => InvokeMediaCapabilityForSmoke(_ =>
    {
        nint playlist;
        lock (_gate) playlist = _playlist;
        nint output = playlist == 0 ? 0 : VlcAudioApi.HoldOutput(playlist);
        if (output == 0) return (long?)null;
        try { return VLCLR.Native.VLCCore.VarGetChecked(output, "stereo-mode", VLCLR.Types.VLCVarType.Integer, out VLCLR.Native.VLCValueNative value) == 0 ? value.Integer : null; }
        finally { VLCLR.Native.VLCCore.ObjectRelease(output); }
    });

    internal VLCVideoAdjustments ReadVideoForSmoke() => InvokeMediaCapabilityForSmoke(adapter => _videoControls.Read(out _))!;
    internal VLCPlayerCapabilityResult SetVideoForSmoke(VLCVideoAdjustments state, string setting, string value) => InvokeMediaCapabilityForSmoke(_ => _videoControls.Set(state, setting, value));
}

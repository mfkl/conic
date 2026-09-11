using Player.Application;
using Player.Contracts;
using VLCLR;

namespace VLCAvalonia.Interop.Windows;

internal sealed partial class WindowsVlcCorePlayer
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

    internal VLCVideoAdjustments ReadVideoForSmoke() => InvokeMediaCapabilityForSmoke(adapter => _videoControls.Read(out _))!;
    internal VLCPlayerCapabilityResult SetVideoForSmoke(VLCVideoAdjustments state, string setting, string value) => InvokeMediaCapabilityForSmoke(_ => _videoControls.Set(state, setting, value));
}

namespace Player.Application;

public sealed record VideoChoice(string Value, string Label)
{
    public override string ToString() => Label;
}
public sealed record VideoAdjustmentState(long QueueGeneration, long MediaGeneration, ulong OutputToken,
    IReadOnlyList<VideoChoice> Aspects, IReadOnlyList<VideoChoice> Crops, IReadOnlyList<VideoChoice> Zooms,
    string Aspect, string Crop, string Zoom)
{
    public bool Available => OutputToken != 0;
    public static VideoAdjustmentState Unavailable { get; } = new(0, 0, 0, [], [], [], "", "", "fit");
}
public interface IVideoAdjustmentTransport
{
    VideoAdjustmentState VideoAdjustments { get; }
    event EventHandler<VideoAdjustmentState>? VideoAdjustmentsChanged;
    void SetVideoAdjustment(VideoAdjustmentState expected, string setting, string value);
}

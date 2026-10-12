namespace Conic.ViewModels;

/// <summary>The loudness band the volume icon shows (design icons
/// <c>volume, value=mute|low|medium|high</c>).</summary>
public enum VolumeLevel
{
    Mute,
    Low,
    Medium,
    High,
}

public static class VolumeLevels
{
    /// <summary>Thirds of the 0–100 % range; amplified levels above 100 %
    /// stay High.</summary>
    public static VolumeLevel For(bool muted, double volumePercent) => muted || volumePercent <= 0
        ? VolumeLevel.Mute
        : volumePercent < 100d / 3 ? VolumeLevel.Low
        : volumePercent < 200d / 3 ? VolumeLevel.Medium
        : VolumeLevel.High;
}

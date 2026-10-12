namespace Player.Application;

/// <summary>
/// Which secondary controls the single-row control bar keeps as the window
/// narrows. Transport, the timeline, mute and the queue always stay.
/// </summary>
public static class PlayerLayoutPolicy
{
    /// <summary>Below this width the elapsed and duration labels give their room to the timeline.</summary>
    public const double CompactWidth = 900;

    /// <summary>Below this width the volume slider and the track buttons (also in the More menu) go.</summary>
    public const double NarrowWidth = 640;

    public static PlayerLayoutResult ForWidth(double availableWidth) =>
        availableWidth >= CompactWidth ? new(false, true, true, true, 20, 20)
        : availableWidth >= NarrowWidth ? new(true, false, true, true, 16, 16)
        : new(true, false, false, false, 12, 12);
}

/// <param name="IsCompact">Narrower than <see cref="PlayerLayoutPolicy.CompactWidth"/>.</param>
/// <param name="ShowTimeLabels">Elapsed and duration text beside the timeline.</param>
/// <param name="ShowVolumeSlider">The volume slider beside the mute button.</param>
/// <param name="ShowTrackButtons">Audio, subtitle and playback settings buttons.</param>
/// <param name="Spacing">Gap between the bar's groups.</param>
/// <param name="Padding">The bar's horizontal padding.</param>
public sealed record PlayerLayoutResult(
    bool IsCompact,
    bool ShowTimeLabels,
    bool ShowVolumeSlider,
    bool ShowTrackButtons,
    double Spacing,
    double Padding);

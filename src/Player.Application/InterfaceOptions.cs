namespace Player.Application;

/// <summary>What one move of the mouse wheel over the video does.</summary>
public enum WheelTarget { None, Volume, Seek }

/// <summary>A wheel move: the volume in percent, or the position in seconds; positive is louder or later.</summary>
public readonly record struct WheelStep(WheelTarget Target, double Amount);

/// <summary>
/// Settings › Controls, Interface and Library (phase 9d): settings the app
/// applies itself, at once (the window, the mouse and the controls) or when
/// it next opens something (the folder and queue rows). The choices the rows
/// offer live here, as <see cref="PlayerEngineOptions"/> does for VLC's.
/// </summary>
public static class InterfaceOptions
{
    public const int MaximumSkipSeconds = 3600;
    public const double MinimumFeedbackSeconds = .5, MaximumFeedbackSeconds = 5;
    // A notch is 1; a precision touchpad sends fractions, a fast wheel more.
    private const double MaximumNotches = 3;

    public static IReadOnlyList<SettingOption> ClickActions { get; } = [new("None", "Nothing"), new("PlayPause", "Play / Pause"), new("Fullscreen", "Toggle fullscreen")];
    public static IReadOnlyList<SettingOption> DoubleClickActions { get; } = [new("Fullscreen", "Toggle fullscreen"), new("PlayPause", "Play / Pause"), new("None", "Nothing")];
    public static IReadOnlyList<SettingOption> WheelActions { get; } = [new("Volume", "Volume"), new("Seek", "Seek"), new("None", "Nothing")];
    public static IReadOnlyList<SettingOption> FolderOpenings { get; } = [new("Subfolders", "Include subfolders"), new("FolderOnly", "This folder only")];

    /// <summary>The settings the app applies at once.</summary>
    public static IReadOnlySet<string> LiveKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.ShortcutFeedback), nameof(PlayerSettings.ShortcutFeedbackSeconds),
        nameof(PlayerSettings.VeryShortSkipSeconds), nameof(PlayerSettings.MediumSkipSeconds), nameof(PlayerSettings.LongSkipSeconds),
        nameof(PlayerSettings.ClickAction), nameof(PlayerSettings.DoubleClickAction), nameof(PlayerSettings.WheelAction),
        nameof(PlayerSettings.HorizontalWheelAction), nameof(PlayerSettings.ReverseWheel),
        nameof(PlayerSettings.KeepOnTop), nameof(PlayerSettings.ShowFileNameInTitle), nameof(PlayerSettings.PauseWhenMinimized),
        nameof(PlayerSettings.ShowControlsInFullscreen), nameof(PlayerSettings.AutoHideControls),
    };

    /// <summary>The settings the app reads when it next opens media or a folder.</summary>
    public static IReadOnlySet<string> NextOpenKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerSettings.ResizeToVideo), nameof(PlayerSettings.EnqueueOnOpen), nameof(PlayerSettings.FolderOpening), nameof(PlayerSettings.ShowHiddenFiles),
    };

    /// <summary>
    /// What a wheel move over the video does: the larger axis decides, each
    /// with its action; a notch moves the volume by the volume step or the
    /// position by the very short skip. Positive deltas (wheel up, or right)
    /// raise the volume and seek forward, unless the direction is reversed.
    /// </summary>
    public static WheelStep Wheel(PlayerSettings settings, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) return default;
        bool vertical = Math.Abs(deltaY) >= Math.Abs(deltaX);
        double delta = Math.Clamp(vertical ? deltaY : deltaX, -MaximumNotches, MaximumNotches);
        if (delta == 0) return default;
        if (settings.ReverseWheel) delta = -delta;
        return (vertical ? settings.WheelAction : settings.HorizontalWheelAction) switch
        {
            "Volume" => new(WheelTarget.Volume, delta * settings.VolumeStep),
            "Seek" => new(WheelTarget.Seek, delta * settings.VeryShortSkipSeconds),
            _ => default,
        };
    }

    /// <summary>Puts the Controls, Interface and Library fields in their ranges and choices.</summary>
    public static PlayerSettings Normalize(PlayerSettings value) => value with
    {
        // Half seconds, as the slider moves.
        ShortcutFeedbackSeconds = double.IsFinite(value.ShortcutFeedbackSeconds)
            ? Math.Clamp(Math.Round(value.ShortcutFeedbackSeconds * 2, MidpointRounding.AwayFromZero) / 2, MinimumFeedbackSeconds, MaximumFeedbackSeconds)
            : PlayerSettings.Default.ShortcutFeedbackSeconds,
        VeryShortSkipSeconds = Math.Clamp(value.VeryShortSkipSeconds, 1, MaximumSkipSeconds),
        MediumSkipSeconds = Math.Clamp(value.MediumSkipSeconds, 1, MaximumSkipSeconds),
        LongSkipSeconds = Math.Clamp(value.LongSkipSeconds, 1, MaximumSkipSeconds),
        ClickAction = Choice(value.ClickAction, ClickActions, "None"),
        DoubleClickAction = Choice(value.DoubleClickAction, DoubleClickActions, "Fullscreen"),
        WheelAction = Choice(value.WheelAction, WheelActions, "Volume"),
        HorizontalWheelAction = Choice(value.HorizontalWheelAction, WheelActions, "Seek"),
        FolderOpening = Choice(value.FolderOpening, FolderOpenings, "Subfolders"),
    };

    private static string Choice(string? value, IReadOnlyList<SettingOption> options, string fallback) =>
        options.Any(option => option.Value == value) ? value! : fallback;
}

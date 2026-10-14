using Player.Application;

namespace Conic.ViewModels;

// Controls, Interface and Library rows (phase 9d): settings the app applies
// itself (InterfaceOptions). The skips, the mouse and the window rows take
// effect at once; Remember window size and position at the next start;
// resize to video and the Library rows when media or a folder next opens.
internal sealed partial class SettingsViewModel
{
    private static readonly string[] InterfaceRowProperties =
    [
        nameof(VeryShortSkipSeconds), nameof(MediumSkipSeconds), nameof(LongSkipSeconds), nameof(ClickAction), nameof(DoubleClickAction),
        nameof(WheelAction), nameof(HorizontalWheelAction), nameof(ReverseWheel),
        nameof(ResizeToVideo), nameof(RememberWindowGeometry), nameof(KeepOnTop), nameof(ShowFileNameInTitle), nameof(OneInstance), nameof(PauseWhenMinimized),
        nameof(ShowControlsInFullscreen), nameof(EnqueueOnOpen), nameof(FolderOpening), nameof(ShowHiddenFiles),
    ];

    // Controls › Skip Amounts: the short skip is Skip backward / Skip forward.
    public int MaximumSkipSeconds => InterfaceOptions.MaximumSkipSeconds;
    public decimal VeryShortSkipSeconds { get => Current.VeryShortSkipSeconds; set => Update(s => s with { VeryShortSkipSeconds = (int)value }); }
    public decimal MediumSkipSeconds { get => Current.MediumSkipSeconds; set => Update(s => s with { MediumSkipSeconds = (int)value }); }
    public decimal LongSkipSeconds { get => Current.LongSkipSeconds; set => Update(s => s with { LongSkipSeconds = (int)value }); }

    // Controls › Mouse, over the video.
    public IReadOnlyList<SettingOption> ClickActions => InterfaceOptions.ClickActions;
    public SettingOption ClickAction { get => Find(ClickActions, Current.ClickAction); set { if (value is not null) Update(s => s with { ClickAction = value.Value }); } }
    public IReadOnlyList<SettingOption> DoubleClickActions => InterfaceOptions.DoubleClickActions;
    public SettingOption DoubleClickAction { get => Find(DoubleClickActions, Current.DoubleClickAction); set { if (value is not null) Update(s => s with { DoubleClickAction = value.Value }); } }
    public IReadOnlyList<SettingOption> WheelActions => InterfaceOptions.WheelActions;
    public SettingOption WheelAction { get => Find(WheelActions, Current.WheelAction); set { if (value is not null) Update(s => s with { WheelAction = value.Value }); } }
    public SettingOption HorizontalWheelAction { get => Find(WheelActions, Current.HorizontalWheelAction); set { if (value is not null) Update(s => s with { HorizontalWheelAction = value.Value }); } }
    public bool ReverseWheel { get => Current.ReverseWheel; set => Update(s => s with { ReverseWheel = value }); }

    // Interface › Window. Keep window above other windows is also the More
    // menu's Always on Top; the player writes it back here.
    public bool ResizeToVideo { get => Current.ResizeToVideo; set => Update(s => s with { ResizeToVideo = value }); }
    public bool RememberWindowGeometry { get => Current.RememberWindowGeometry; set => Update(s => s with { RememberWindowGeometry = value }); }
    public bool KeepOnTop { get => Current.KeepOnTop; set => Update(s => s with { KeepOnTop = value }); }
    public bool ShowFileNameInTitle { get => Current.ShowFileNameInTitle; set => Update(s => s with { ShowFileNameInTitle = value }); }
    public bool OneInstance { get => Current.OneInstance; set => Update(s => s with { OneInstance = value }); }
    public bool PauseWhenMinimized { get => Current.PauseWhenMinimized; set => Update(s => s with { PauseWhenMinimized = value }); }

    // Interface › Playback Controls (with Auto-hide, Hide after and Reduce motion).
    public bool ShowControlsInFullscreen { get => Current.ShowControlsInFullscreen; set => Update(s => s with { ShowControlsInFullscreen = value }); }

    // Library › Opening Files.
    public bool EnqueueOnOpen { get => Current.EnqueueOnOpen; set => Update(s => s with { EnqueueOnOpen = value }); }
    public IReadOnlyList<SettingOption> FolderOpenings => InterfaceOptions.FolderOpenings;
    public SettingOption FolderOpening { get => Find(FolderOpenings, Current.FolderOpening); set { if (value is not null) Update(s => s with { FolderOpening = value.Value }); } }
    public bool ShowHiddenFiles { get => Current.ShowHiddenFiles; set => Update(s => s with { ShowHiddenFiles = value }); }

    private static PlayerSettings ResetControlsPage(PlayerSettings s, PlayerSettings d) => s with
    {
        SkipBackwardSeconds = d.SkipBackwardSeconds, SkipForwardSeconds = d.SkipForwardSeconds, VeryShortSkipSeconds = d.VeryShortSkipSeconds,
        MediumSkipSeconds = d.MediumSkipSeconds, LongSkipSeconds = d.LongSkipSeconds, ClickAction = d.ClickAction,
        DoubleClickAction = d.DoubleClickAction, WheelAction = d.WheelAction, HorizontalWheelAction = d.HorizontalWheelAction, ReverseWheel = d.ReverseWheel,
    };

    private static PlayerSettings ResetInterfacePage(PlayerSettings s, PlayerSettings d) => s with
    {
        Density = d.Density, ControlTimeoutSeconds = d.ControlTimeoutSeconds, ReduceMotion = d.ReduceMotion, AutoHideControls = d.AutoHideControls,
        ResizeToVideo = d.ResizeToVideo, RememberWindowGeometry = d.RememberWindowGeometry, KeepOnTop = d.KeepOnTop,
        ShowFileNameInTitle = d.ShowFileNameInTitle, OneInstance = d.OneInstance, PauseWhenMinimized = d.PauseWhenMinimized, ShowControlsInFullscreen = d.ShowControlsInFullscreen,
    };

    private static PlayerSettings ResetLibraryPage(PlayerSettings s, PlayerSettings d) => s with
    {
        EnqueueOnOpen = d.EnqueueOnOpen, FolderOpening = d.FolderOpening, ShowHiddenFiles = d.ShowHiddenFiles,
    };
}

using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

// The control bar's panels (design: bar panels above the control bar). One
// drawer shows one of three tab sets, chosen by the selected tab:
//   Queue panel     (queue button)             Queue, Chapters
//   Tracks panel    (audio/subtitle buttons)   Audio, Subtitles
//   Playback panel  (playback settings gear)   Playback, Video, Information
// Bookmarks and the Video track list are not supported and stay hidden (D2).
internal sealed partial class PlayerPresentationViewModel
{
    public const string QueuePanel = "Queue";
    public const string TracksPanel = "Tracks";
    public const string PlaybackPanel = "Playback";

    private static readonly string[] QueuePanelTabs = ["Queue", "Chapters"];
    private static readonly string[] TracksPanelTabs = ["Audio", "Subtitles"];
    private static readonly string[] PlaybackPanelTabs = ["Playback", "Video", "Information"];

    /// <summary>The panel a drawer tab belongs to.</summary>
    public static string PanelOf(string tab) => tab switch
    {
        "Audio" or "Subtitles" => TracksPanel,
        "Playback" or "Video" or "Information" => PlaybackPanel,
        _ => QueuePanel,
    };

    /// <summary>The tabs the drawer shows while <paramref name="tab"/> is selected.</summary>
    public static IReadOnlyList<string> TabsFor(string tab) => PanelOf(tab) switch
    {
        TracksPanel => TracksPanelTabs,
        PlaybackPanel => PlaybackPanelTabs,
        _ => QueuePanelTabs,
    };

    public string DrawerPanel => PanelOf(DrawerTab);
    public IReadOnlyList<string> VisibleDrawerTabs => TabsFor(DrawerTab);
    public bool IsQueuePanel => DrawerPanel == QueuePanel;
    public bool IsTracksPanel => DrawerPanel == TracksPanel;
    public bool IsPlaybackPanel => DrawerPanel == PlaybackPanel;
    public bool IsPlaybackTab => DrawerTab == "Playback";
    public string DrawerAutomationName => DrawerPanel switch
    {
        TracksPanel => "Tracks and synchronization",
        PlaybackPanel => "Playback settings",
        _ => "Playback queue and chapters",
    };

    // The bar button of the open panel shows its active (accent) state. The
    // audio and subtitle buttons share the tracks panel; each is active while
    // its own tab shows.
    public bool IsQueuePanelOpen => IsQueueOpen && IsQueuePanel;
    public bool IsAudioPanelOpen => IsQueueOpen && IsAudioTab;
    public bool IsSubtitlePanelOpen => IsQueueOpen && IsSubtitleTab;
    public bool IsPlaybackPanelOpen => IsQueueOpen && IsPlaybackPanel;

    /// <summary>
    /// A bar button: opens the drawer on <c>tab</c>, or closes it when that
    /// button is already the active one (another tab of a different button
    /// switches instead).
    /// </summary>
    public ICommand ToggleDrawerPanelCommand => _toggleDrawerPanelCommand ??= new RelayCommand(p => ToggleDrawerPanel(p as string ?? QueuePanel));
    private ICommand? _toggleDrawerPanelCommand;

    public void ToggleDrawerPanel(string tab)
    {
        bool active = IsQueueOpen && (tab is "Audio" or "Subtitles" ? DrawerTab == tab : PanelOf(tab) == DrawerPanel);
        if (active) { IsQueueOpen = false; return; }
        DrawerTab = tab;
        IsQueueOpen = true;
    }

    private void NotifyDrawerPanel()
    {
        foreach (string property in new[] { nameof(DrawerPanel), nameof(DrawerAutomationName), nameof(VisibleDrawerTabs), nameof(IsQueuePanel), nameof(IsTracksPanel), nameof(IsPlaybackPanel), nameof(IsPlaybackTab), nameof(IsQueuePanelOpen), nameof(IsAudioPanelOpen), nameof(IsSubtitlePanelOpen), nameof(IsPlaybackPanelOpen) })
            OnPropertyChanged(property);
    }

    // Queue panel header: "4 items". Queue items carry no durations, so the
    // design's total duration is left out.
    public string QueueSummaryText => QueueItems.Count == 1 ? "1 item" : $"{QueueItems.Count} items";
    public bool IsRepeatOn => QueueState.Repeat is QueueRepeatMode.CurrentItem or QueueRepeatMode.All;
    public bool IsRepeatItem => QueueState.Repeat == QueueRepeatMode.CurrentItem;
    public bool IsShuffleOn => QueueState.Order == QueueOrder.Random;

    // Playback panel speed slider (design: 0.25 to 2 with a tick every 0.25).
    // It shows the effective rate, clamped to its range, and writes through
    // the same command as the More menu's speed list.
    public const double MinimumSliderSpeed = .25, MaximumSliderSpeed = 2;
    public double PlaybackSpeed
    {
        get => Math.Clamp(PlaybackRate, MinimumSliderSpeed, MaximumSliderSpeed);
        set
        {
            value = Math.Clamp(Math.Round(value * 4) / 4, MinimumSliderSpeed, MaximumSliderSpeed);
            if (_isUpdatingFromEngine || Math.Abs(value - PlaybackSpeed) < .001) return;
            Registry.Execute(PlayerCommandId.SetPlaybackRate, value);
        }
    }
    public string PlaybackRateText => $"{PlaybackRate:0.##}×";
}

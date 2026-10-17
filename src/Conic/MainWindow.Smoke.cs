using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Conic.Controls;
using Conic.Interop;
using Player.Application;
using Player.Contracts;
using VLCLR;

namespace Conic;

// Every member here exists only to drive the interactive smoke harness.
// The file compiles solely in builds that define SMOKE (Debug, or Release
// packages published with -p:Smoke=true); the product binary excludes it.
#if SMOKE
public partial class MainWindow
{
    internal void EnableGPUCompositionSmoke(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        VideoSurface.ProbeSharedTextureForSmoke = true;
        ulong firstDeviceGeneration = 0;
        ulong firstResourceGeneration = 0;
        string? firstAdapter = null;
        IMediaEngine? playerBeforeDetach = null;
        int reattachments = 0;
        int phase = 0;
        DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            Console.Error.WriteLine(
                $"[composition-ui-smoke] Timed out waiting for a GPU frame (phase {phase}, reattach {reattachments}).");
            desktop.Shutdown(1);
        };
        VideoSurface.StatusChanged += status =>
        {
            Console.WriteLine($"[composition-ui-smoke] {status}");
            if (status.StartsWith(
                    "D3D11 presentation failed",
                    StringComparison.Ordinal) ||
                status.Contains(
                    "using CPU BGRA fallback",
                    StringComparison.Ordinal))
            {
                timeout.Stop();
                desktop.Shutdown(1);
            }
        };
        VideoSurface.GPUFramePresentedForSmoke +=
            (deviceGeneration, resourceGeneration, adapter) =>
            {
                if (phase == 3) return; // The detach is queued after this frame callback.
                if (phase == 4)
                {
                    if (VideoSurface.Player is null || ReferenceEquals(playerBeforeDetach, VideoSurface.Player) ||
                        !string.Equals(firstAdapter, adapter, StringComparison.Ordinal))
                    {
                        timeout.Stop();
                        Console.Error.WriteLine("[composition-ui-smoke] Reattachment did not create a new player on the same adapter.");
                        desktop.Shutdown(1);
                        return;
                    }
                    Console.WriteLine($"[composition-ui-smoke] PASS reattach={++reattachments} adapter-luid={adapter}");
                    RestartTimeout();
                    if (reattachments == 3)
                    {
                        timeout.Stop();
                        desktop.Shutdown(0);
                    }
                    else ReattachSurface();
                    return;
                }
                if (phase == 0)
                {
                    firstDeviceGeneration = deviceGeneration;
                    firstResourceGeneration = resourceGeneration;
                    firstAdapter = adapter;
                    phase = 1;
                    Console.WriteLine(
                        "[composition-ui-smoke] resize-fullscreen " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Width += 120;
                    Height += 80;
                    WindowState = WindowState.FullScreen;
                    return;
                }

                if (phase == 1)
                {
                    phase = 2;
                    RestartTimeout();
                    WindowState = WindowState.Normal;
                    Console.WriteLine(
                        "[composition-ui-smoke] PASS resize-fullscreen " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Console.WriteLine(
                        "[composition-ui-smoke] reopening " +
                        $"device-generation={deviceGeneration} " +
                        $"resource-generation={resourceGeneration} " +
                        $"adapter-luid={adapter}");
                    Dispatcher.UIThread.Post(VideoSurface.ReopenForSmoke);
                    return;
                }

                if (resourceGeneration == firstResourceGeneration)
                    return;

                if (deviceGeneration <= firstDeviceGeneration ||
                    !string.Equals(
                        firstAdapter,
                        adapter,
                        StringComparison.Ordinal))
                {
                    timeout.Stop();
                    Console.Error.WriteLine(
                        "[composition-ui-smoke] Reopen did not recreate " +
                        "resources on the same adapter.");
                    desktop.Shutdown(1);
                    return;
                }

                Console.WriteLine(
                    "[composition-ui-smoke] PASS reopen " +
                    $"device-generation={deviceGeneration} " +
                    $"resource-generation={resourceGeneration} " +
                    $"adapter-luid={adapter}");
                RestartTimeout();
                ReattachSurface();
            };

        void RestartTimeout()
        {
            timeout.Stop();
            timeout.Start();
        }
        void ReattachSurface()
        {
            playerBeforeDetach = VideoSurface.Player;
            phase = 3;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    int index = VideoArea.Children.IndexOf(VideoSurface);
                    VideoArea.Children.RemoveAt(index);
                    VideoArea.Children.Insert(index, VideoSurface);
                    phase = 4;
                }
                catch (Exception exception)
                {
                    timeout.Stop();
                    Console.Error.WriteLine($"[composition-ui-smoke] Reattachment failed: {exception}");
                    desktop.Shutdown(1);
                }
            });
        }
        Opened += (_, _) => { OpenTestMedia(); timeout.Start(); };
    }

    internal void EnableLayoutSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _presentation.IsHomeOpen = false;
        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 720;
        int phase = 0;
        int resizeWaits = 0;
        DispatcherTimer? timeout = null;
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            try
            {
                double width = ClientSize.Width;
                bool compact = width <= 480;
                Console.WriteLine($"[layout-smoke] phase={phase} width={width}");
                switch (phase)
                {
                    case 0: ValidateLayout(width, compact); ValidateTopChrome(width); OpenMenu.IsVisible = true; UrlPanel.IsVisible = true; SettingsPanel.IsVisible = true; _presentation.IsQueueOpen = true; _presentation.IsLibraryOpen = true; phase = 30; return;
                    case 30: ValidateLibraryDrawer(width); ClearLibraryButton.IsEnabled = true; OnClearLibrary(null, new Avalonia.Interactivity.RoutedEventArgs()); phase = 32; return;
                    case 32: ValidateSurface("library-clear-prompt", ClearLibraryPrompt, [ClearLibraryPrompt, CancelClearLibraryButton]); OnCancelClearLibrary(null, new Avalonia.Interactivity.RoutedEventArgs()); phase = 33; return;
                    case 33: if (!ClearLibraryButton.IsVisible) throw new InvalidOperationException("[layout-smoke] FAIL library clear focus restoration"); _presentation.IsLibraryOpen = false; ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase = 1; return;
                    case 1: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 20; return;
                    case 20: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction]); CloseOpenMenu(); ShowUrlPanel(); phase = 2; return;
                    case 2: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 3: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction, CancelResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(page => page.Name == "Privacy"); ApplyResponsiveSettings(); phase++; return;
                    case 4: ValidateSettingsLayout(width, compact); BringSettingsControlIntoView(SettingsPanel.SettingsClearRecentButton); phase++; return;
                    case 5: ValidateSettingsControl("recent-clear", SettingsPanel.SettingsClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearRecentButton); phase++; return;
                    case 6: ValidateSettingsControl("recent-confirm", SettingsPanel.SettingsConfirmClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsPanel.SettingsClearResumeButton); phase++; return;
                    case 7: ValidateSettingsControl("resume-clear", SettingsPanel.SettingsClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearResumeButton); phase++; return;
                    case 8: ValidateSettingsControl("resume-confirm", SettingsPanel.SettingsConfirmClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
                    case 9: ValidateToastForSmoke(); OpenErrorToast.IsVisible = false; Width = 480; phase++; return;
                    case 10 when Math.Abs(width - 480) > 1 && ++resizeWaits < 12: return;
                    case 10: ApplyResponsiveChrome(); ApplyResponsiveSettings(); ValidateLayout(width, true); ValidateTopChrome(width); OpenMenu.IsVisible = true; UrlPanel.IsVisible = true; SettingsPanel.IsVisible = true; _presentation.IsQueueOpen = true; _presentation.IsLibraryOpen = true; phase = 31; return;
                    case 31: ValidateLibraryDrawer(width); _presentation.IsLibraryOpen = false; ShowOpenMenuForSmoke(); _presentation.IsQueueOpen = true; phase = 11; return;
                    case 11: ValidateQueueDrawer(width); _presentation.DrawerTab = "Audio"; phase = 21; return;
                    case 21: ValidateMediaDrawer(width); _presentation.IsQueueOpen = false; ValidateSurface("open-menu", OpenMenu, [OpenMenu, OpenFolderAction]); CloseOpenMenu(); ShowUrlPanel(); phase = 12; return;
                    case 12: ValidateSurface("url-panel", UrlPanel, [UrlPanel, UrlTextBox, UrlCancelButton, UrlSubmitButton]); CloseUrlPanel(); ResumePanel.IsVisible = true; Dispatcher.UIThread.Post(() => ResumeAction.Focus()); phase++; return;
                    case 13: ValidateSurface("resume-panel", ResumePanel, [ResumePanel, ResumeAction, DeclineResumeAction, CancelResumeAction]); ResumePanel.IsVisible = false; OpenSettings(); _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(page => page.Name == "Privacy"); ApplyResponsiveSettings(); phase++; return;
                    case 14: ValidateSettingsLayout(width, true); BringSettingsControlIntoView(SettingsPanel.SettingsClearRecentButton); phase++; return;
                    case 15: ValidateSettingsControl("recent-clear", SettingsPanel.SettingsClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearRecentButton); phase++; return;
                    case 16: ValidateSettingsControl("recent-confirm", SettingsPanel.SettingsConfirmClearRecentButton); SettingsPanel.SettingsClearRecentPrompt.IsVisible = false; BringSettingsControlIntoView(SettingsPanel.SettingsClearResumeButton); phase++; return;
                    case 17: ValidateSettingsControl("resume-clear", SettingsPanel.SettingsClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = true; BringSettingsControlIntoView(SettingsPanel.SettingsConfirmClearResumeButton); phase++; return;
                    case 18: ValidateSettingsControl("resume-confirm", SettingsPanel.SettingsConfirmClearResumeButton); SettingsPanel.SettingsClearResumePrompt.IsVisible = false; CloseSettings(); OpenErrorToast.IsVisible = true; phase++; return;
                    case 19: ValidateToastForSmoke(); OpenErrorToast.IsVisible = false; timer.Stop(); timeout?.Stop(); Console.WriteLine("[layout-smoke] PASS opening surfaces wide-and-compact"); desktop.Shutdown(0); return;
                    default: throw new InvalidOperationException("[layout-smoke] unexpected phase");
                }
            }
            catch (Exception exception)
            {
                timer.Stop(); timeout?.Stop(); Console.Error.WriteLine($"[layout-smoke] FAIL phase={phase} {exception}"); desktop.Shutdown(1);
            }
        };
        timeout = new() { Interval = TimeSpan.FromSeconds(14) };
        timeout.Tick += (_, _) => { timeout.Stop(); timer.Stop(); Console.Error.WriteLine("[layout-smoke] FAIL timeout"); desktop.Shutdown(1); };
        timer.Start(); timeout.Start();
    }

    // The control bar is one 54 px row (phase 4a): docked at the bottom in a
    // window with the video between it and the title bar, groups left to
    // right, the timeline filling the room between transport and volume, and
    // secondary controls dropped by PlayerLayoutPolicy as the window narrows.
    private void ValidateLayout(double width, bool compact)
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(ClientSize.Width);
        Rect dock = GetClientRect(PlayerDock);
        Rect top = GetClientRect(TopChrome);
        Rect video = GetClientRect(VideoSurface);
        Rect transport = GetClientRect(TransportGroup), timeline = GetClientRect(TimelineGroup);
        Rect volume = GetClientRect(VolumeGroup), tools = GetClientRect(ToolsGroup);
        Control[] required = layout.ShowVolumeSlider
            ? [TransportGroup, StopButton, PlayerTimeline, MuteButton, PlayerVolume, QueueButton]
            : [TransportGroup, StopButton, PlayerTimeline, MuteButton, QueueButton];
        bool contained = required.All(control =>
        {
            Rect rect = GetClientRect(control);
            return control.IsEffectivelyVisible && rect.Width > 0 && rect.Height > 0 &&
                rect.Left >= dock.Left && rect.Top >= dock.Top && rect.Right <= dock.Right + .5 && rect.Bottom <= dock.Bottom + .5;
        });
        bool docked = Math.Abs(dock.Height - PlayerBarHeight) < .5 && Math.Abs(dock.Bottom - ClientSize.Height) < .5 &&
            Math.Abs(dock.Width - ClientSize.Width) < .5 && Math.Abs(top.Top) < .5 && Math.Abs(top.Height - PlatformChrome.TitleBarHeight) < .5;
        bool videoBetweenBars = Math.Abs(video.Top - top.Bottom) < .5 && Math.Abs(video.Bottom - dock.Top) < .5 &&
            Math.Abs(video.Width - ClientSize.Width) < .5 && ReferenceEquals(VideoSurface.Parent, VideoArea) &&
            Grid.GetRow(SubtitleLayer) == Grid.GetRow(VideoSurface) && Grid.GetRowSpan(SubtitleLayer) == Grid.GetRowSpan(VideoSurface);
        double spacing = DockGrid.ColumnSpacing;
        bool ordered = transport.Right <= timeline.Left && timeline.Right <= volume.Left && volume.Right <= tools.Left;
        bool fills = Math.Abs(timeline.Left - transport.Right - spacing) < 1 && Math.Abs(volume.Left - timeline.Right - spacing) < 1 &&
            GetClientRect(PlayerTimeline).Width > 40;
        bool policy = layout.IsCompact == compact && ElapsedLabel.IsVisible == layout.ShowTimeLabels &&
            PlayerVolume.IsVisible == layout.ShowVolumeSlider && AudioTracksButton.IsVisible == layout.ShowTrackButtons &&
            (!layout.ShowVolumeSlider || Math.Abs(PlayerVolume.Bounds.Width - 88) < 0.1);
        Console.WriteLine($"[layout-smoke] width={width} dock={dock} top={top} video={video} transport={transport} timeline={timeline} volume={volume} tools={tools} contained={contained} docked={docked} video-between={videoBetweenBars} ordered={ordered} fills={fills} policy={policy}");
        if (!contained || !docked || !videoBetweenBars || !ordered || !fills || !policy)
            throw new InvalidOperationException("[layout-smoke] FAIL bar bounds or layout policy");
    }

    private void ShowOpenMenuForSmoke()
    {
        PrepareOpenMenu();
        OpenMenu.IsVisible = true;
        UrlPanel.IsVisible = false;
        ClearRecentPrompt.IsVisible = false;
        UpdateOpenErrorSurface();
        Dispatcher.UIThread.Post(() => OpenMenu.Focus());
    }

    private void ValidateTopChrome(double width)
    {
        Control[] controls = [OpenOptionsButton, HomeButton, FullscreenButton];
        bool contained = controls.All(control => IsContained(control) && control.IsEffectivelyVisible);
        Rect title = GetClientRect(TopChromeTitle);
        Rect more = GetClientRect(OpenOptionsButton);
        Rect home = GetClientRect(HomeButton);
        Rect fullscreen = GetClientRect(FullscreenButton);
        bool separate = !more.Intersects(home) && !home.Intersects(fullscreen) &&
            (title.Width == 0 || (!title.Intersects(home) && !title.Intersects(fullscreen)));
        Console.WriteLine($"[layout-smoke] top width={width} more={more} home={home} fullscreen={fullscreen} title={title} contained={contained} separate={separate}");
        if (!contained || !separate)
            throw new InvalidOperationException("[layout-smoke] FAIL top chrome bounds");
    }

    private void ValidateQueueDrawer(double width)
    {
        bool contained = IsContained(QueueDrawer) && QueueDrawer.IsVisible && QueueDrawer.Bounds.Width > 0 && QueueDrawer.Bounds.Width <= Math.Max(400, width - 24);
        Console.WriteLine($"[layout-smoke] queue-drawer width={width} bounds={QueueDrawer.Bounds} contained={contained}");
        if (!contained) throw new InvalidOperationException("[layout-smoke] FAIL queue drawer bounds");
    }

    private void ValidateLibraryDrawer(double width)
    {
        bool contained = IsContained(LibraryPanel) && LibraryPanel.IsVisible && LibraryPanel.Bounds.Width > 0 && LibraryPanel.Bounds.Width <= Math.Max(430, width - 32);
        bool exclusive = !QueueDrawer.IsVisible && !OpenMenu.IsVisible && !UrlPanel.IsVisible && !SettingsPanel.IsVisible && !ResumePanel.IsVisible;
        Console.WriteLine($"[layout-smoke] library-drawer width={width} bounds={LibraryPanel.Bounds} contained={contained} exclusive={exclusive}");
        if (!contained || !exclusive) throw new InvalidOperationException("[layout-smoke] FAIL library drawer bounds or overlay exclusivity");
    }

    private void ValidateMediaDrawer(double width)
    {
        Control[] controls = [QueueDrawer, AudioDelayInput];
        bool contained = controls.All(IsContained) && AudioDelayInput.Bounds.Width > 0 && AudioDelayInput.Bounds.Width <= 112.1;
        Console.WriteLine($"[layout-smoke] media-drawer width={width} drawer={QueueDrawer.Bounds} audio-delay={AudioDelayInput.Bounds} contained={contained}");
        if (!contained) throw new InvalidOperationException("[layout-smoke] FAIL media drawer bounds");
    }

    private void ValidateSurface(string name, Control surface, Control[] required)
    {
        bool contained = required.All(IsContained);
        Console.WriteLine($"[layout-smoke] {name} bounds={surface.Bounds} contained={contained}");
        if (!contained || (name == "open-menu" && GetClientRect(OpenMenu).Top < GetClientRect(OpenOptionsButton).Bottom))
            throw new InvalidOperationException($"[layout-smoke] FAIL {name} bounds or capability gate");
    }


    private bool IsContained(Control control)
    {
        Rect rect = GetClientRect(control);
        return rect.Width > 0 && rect.Height > 0 && !double.IsNaN(rect.X) && !double.IsNaN(rect.Y) &&
            rect.Left >= 0 && rect.Top >= 0 && rect.Right <= ClientSize.Width && rect.Bottom <= ClientSize.Height;
    }

    private bool IsContainedInSettings(Control control)
    {
        Rect panel = GetClientRect(SettingsPanel);
        Rect rect = GetClientRect(control);
        return IsContained(control) && rect.Left >= panel.Left && rect.Top >= panel.Top && rect.Right <= panel.Right && rect.Bottom <= panel.Bottom;
    }

    private void ValidateSettingsLayout(double width, bool compact)
    {
        Control navigation = compact ? SettingsPanel.CompactSettingsPages : SettingsPanel.SettingsPages;
        Control[] required = [SettingsPanel, SettingsPanel.SettingsSearch, navigation, SettingsPanel.SettingsContent];
        bool contained = required.All(IsContained);
        Console.WriteLine($"[settings-layout-smoke] width={width} compact={compact} panel={SettingsPanel.Bounds} search={SettingsPanel.SettingsSearch.Bounds} navigation={navigation.Bounds} content={SettingsPanel.SettingsContent.Bounds} recent-clear={SettingsPanel.SettingsClearRecentButton.Bounds} resume-clear={SettingsPanel.SettingsClearResumeButton.Bounds} open={OpenSettingsButton.Bounds} reset={SettingsPanel.SettingsResetRow.Bounds} contained={contained}");
        if (!contained || !navigation.IsVisible)
            throw new InvalidOperationException("[settings-layout-smoke] FAIL bounds or responsive navigation");
    }

    private void BringSettingsControlIntoView(Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.Post(() => control.Focus());
    }

    private void ValidateSettingsControl(string name, Control control)
    {
        Rect rect = GetClientRect(control);
        bool contained = control.IsVisible && IsContainedInSettings(control);
        Console.WriteLine($"[settings-layout-smoke] {name} rect={rect} panel={GetClientRect(SettingsPanel)} contained={contained}");
        if (!contained)
            throw new InvalidOperationException($"[settings-layout-smoke] FAIL {name} is not visible in settings viewport");
    }

    private void ValidateToastForSmoke()
    {
        Rect toast = GetClientRect(OpenErrorToast);
        Rect dock = GetClientRect(PlayerDock);
        bool valid = IsContained(OpenErrorToast) && !toast.Intersects(dock) && !OpenMenu.IsVisible && !UrlPanel.IsVisible && !ResumePanel.IsVisible && !SettingsPanel.IsVisible;
        Console.WriteLine($"[layout-smoke] toast={toast} dock={dock} valid={valid}");
        if (!valid)
            throw new InvalidOperationException("[layout-smoke] FAIL toast geometry");
    }

    internal void EnableAdapterMismatchSmoke(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        VideoSurface.ForceUnavailableAdapterForSmoke = true;
        bool fallbackObserved = false;
        DispatcherTimer timeout = new()
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            Console.Error.WriteLine(
                "[adapter-mismatch-smoke] Timed out waiting for safe CPU " +
                "fallback.");
            desktop.Shutdown(1);
        };
        VideoSurface.StatusChanged += status =>
        {
            Console.WriteLine($"[adapter-mismatch-smoke] {status}");
            if (status.Contains(
                    "fallback=requested-adapter-unavailable",
                    StringComparison.Ordinal) ||
                status.Contains(
                    "fallback=adapter-mismatch",
                    StringComparison.Ordinal))
            {
                fallbackObserved = true;
            }
            else if (status == "D3D11 frame presented by Avalonia compositor")
            {
                timeout.Stop();
                Console.Error.WriteLine(
                    "[adapter-mismatch-smoke] A mismatched producer was " +
                    "incorrectly presented.");
                desktop.Shutdown(1);
            }
            else if (status == "CPU BGRA fallback frame presented" &&
                fallbackObserved)
            {
                timeout.Stop();
                Console.WriteLine(
                    "[adapter-mismatch-smoke] PASS safe CPU fallback");
                desktop.Shutdown(0);
            }
        };
        Opened += (_, _) => { OpenTestMedia(); timeout.Start(); };
    }
}
#endif // SMOKE

#if SMOKE
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Contracts;
using Player.Application;
using VLCAvalonia.Interop;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableAccessibilitySmoke(IClassicDesktopStyleApplicationLifetime desktop, bool native)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                async Task WaitFor(Func<bool> condition, string message)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                if (native)
                {
                    _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                    await WaitFor(() => _presentation.IsPlaying && _presentation.CanSeek, "fixture supports seeking");
                    _presentation.TogglePauseCommand.Execute(null);
                    await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "fixture pauses for exact seek checks");
                    var seek = ControlAutomationPeer.CreatePeerForElement(PlayerTimeline) as IRangeValueProvider
                        ?? throw new InvalidOperationException("Timeline has no range-value automation provider");
                    int started = 0, committed = 0;
                    PlayerTimeline.SeekStarted += (_, _) => started++;
                    PlayerTimeline.SeekCommitted += (_, _) => committed++;
                    seek.SetValue(.6);
                    await WaitFor(() => Math.Abs(VideoSurface.Player!.State.Position - .6) < .03, "automation seek reaches VLC");
                    Require(started == 1 && committed == 1, "automation commits exactly one seek");
                    try { seek.SetValue(double.NaN); throw new InvalidOperationException("Invalid seek accepted"); }
                    catch (ArgumentOutOfRangeException) { }
                    Require(started == 1 && committed == 1, "invalid automation value does not start a seek");
                    var disabled = new Controls.PlayerTimeline { Minimum = 0, Maximum = 1, IsEnabled = false };
                    var disabledPeer = (IRangeValueProvider)ControlAutomationPeer.CreatePeerForElement(disabled)!;
                    bool rejected = false;
                    try { disabledPeer.SetValue(.5); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected && disabled.Value == 0, "disabled timeline rejects automation writes");
                    var volume = ControlAutomationPeer.CreatePeerForElement(PlayerVolume) as IRangeValueProvider
                        ?? throw new InvalidOperationException("Volume has no range-value automation provider");
                    volume.SetValue(125);
                    await WaitFor(() => Math.Abs(VideoSurface.Player!.State.Volume - 1.25) < .03, "automation volume reaches VLC");
                    _presentation.TogglePictureInPictureCommand.Execute(null);
                    await WaitFor(() => _presentation.IsPictureInPicture && PictureInPictureTimeline.IsEffectivelyVisible, "PiP timeline is available");
                    var pipSeek = (IRangeValueProvider)ControlAutomationPeer.CreatePeerForElement(PictureInPictureTimeline)!;
                    pipSeek.SetValue(.25);
                    await WaitFor(() => Math.Abs(VideoSurface.Player!.State.Position - .25) < .03, "PiP automation seek reaches VLC");
                    _presentation.TogglePictureInPictureCommand.Execute(null);
                    _presentation.DrawerTab = "Queue"; _presentation.IsQueueOpen = true;
                    await Task.Delay(150);
                    var queueActions = QueueDrawer.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is QueueItem && b.Content is string).ToArray();
                    Require(queueActions.Length >= 3 && queueActions.All(b => ControlAutomationPeer.CreatePeerForElement(b)!.GetName().Contains(((QueueItem)b.DataContext!).Name, StringComparison.Ordinal)), "queue action names identify their item");
                    _presentation.ShowHomeCommand.Execute(null);
                    await Task.Delay(150);
                    var actions = HomeScreen.GetVisualDescendants().OfType<Button>().Where(b =>
                        ReferenceEquals(b.Command, _presentation.EnqueueHomeItemCommand) || ReferenceEquals(b.Command, _presentation.AddFavoriteCommand) || ReferenceEquals(b.Command, _presentation.RemoveHomeItemCommand)).ToArray();
                    Require(actions.Length >= 3 && actions.All(b => ControlAutomationPeer.CreatePeerForElement(b)!.GetName().Contains(((ViewModels.HomeMediaItem)b.DataContext!).Title, StringComparison.Ordinal)), "Home action names identify their media item");
                    Console.WriteLine("[accessibility-smoke] PASS Playback/PiP automation seek and volume reach VLC, single seek commit, invalid/disabled writes rejected, contextual Home/queue action names");
                    desktop.Shutdown(0); return;
                }

                await Task.Delay(350);
                string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "accessibility-review");
                Directory.CreateDirectory(directory);
                int audited = 0;
                void Audit(Control root)
                {
                    foreach (Control control in root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.TemplatedParent is null && c is Button or TextBox or Slider or ComboBox or NumericUpDown))
                    {
                        string? name = ControlAutomationPeer.CreatePeerForElement(control)?.GetName();
                        Require(!string.IsNullOrWhiteSpace(name), $"accessible name: {control.GetType().Name} {control.Name}");
                        Rect bounds = GetClientRect(control), viewport = GetClientRect(root);
                        Require(bounds.Left >= viewport.Left - 1 && bounds.Right <= viewport.Right + 1, $"interactive control fits horizontally: {control.GetType().Name} {name}");
                        audited++;
                    }
                }
                void Capture(string screen, double scale, double font)
                {
                    // Simulate a fixed 1280×720 pixel display at different scale factors.
                    // This checks DIP layout and scaled rasterization, not WM_DPICHANGED.
                    using RenderTargetBitmap bitmap = new(new PixelSize((int)Math.Ceiling(ClientSize.Width * scale), (int)Math.Ceiling(ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
                    bitmap.Render(VideoArea);
                    bitmap.Save(Path.Combine(directory, $"{screen}-{scale * 100:0}-font-{font:0}.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (double font in new[] { 14d, 20d })
                foreach (double scale in new[] { 1d, 1.25, 1.5, 2d })
                {
                    FontSize = font;
                    Width = 1280 / scale; Height = 720 / scale;
                    _presentation.IsHomeOpen = true;
                    await Task.Delay(150);
                    Require(IsContained(HomeScreen), "Home fits the scaled logical viewport");
                    Audit(HomeScreen); Capture("home", scale, font);
                    _presentation.IsHomeOpen = false;
                    _presentation.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Paused, CanSeek = true });
                    await Task.Delay(100);
                    Require(new Control[] { TransportGroup, PlayerTimeline, VolumeGroup, ToolsGroup }.All(IsContained), "Playback controls fit the scaled logical viewport");
                    Audit(PlayerDock); Audit(TopChrome); Capture("playback", scale, font);
                    _presentation.DrawerTab = "Audio"; _presentation.IsQueueOpen = true;
                    await Task.Delay(100);
                    Require(DrawerTabs.Children.OfType<Button>().All(IsContained), "drawer tabs fit with larger text");
                    Audit(QueueDrawer); Capture("drawer", scale, font);
                    _presentation.IsQueueOpen = false;
                    OpenSettings();
                    await Task.Delay(100);
                    foreach (var page in _settingsViewModel.Pages)
                    {
                        _settingsViewModel.SelectedPage = page;
                        await Task.Delay(60);
                        Require(IsContained(SettingsPanel.SettingsSearch) && IsContained(SettingsPanel.SettingsContent), "Settings viewport fits");
                        foreach (Control editor in SettingsPanel.SettingsContent.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.TemplatedParent is null && c is Button or TextBox or ComboBox or NumericUpDown))
                        {
                            Rect rect = GetClientRect(editor), viewport = GetClientRect(SettingsPanel.SettingsContent);
                            Require(rect.Left >= viewport.Left - 1 && rect.Right <= viewport.Right + 1, $"{page.Name} editor fits at scale={scale}, font={font}: {editor.GetType().Name}");
                        }
                        Audit(SettingsPanel);
                        if (page.Name == "Hotkeys") Capture("hotkeys", scale, font);
                    }
                    CloseSettings();
                }
                Console.WriteLine($"[accessibility-smoke] PASS {audited} accessible control names; 100/125/150/200% equivalent viewports and rasterization, 14/20 DIP text; Home, Playback, drawer and all Settings pages");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[accessibility-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

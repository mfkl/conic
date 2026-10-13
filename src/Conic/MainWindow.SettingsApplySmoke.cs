#if SMOKE
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Services;

namespace Conic;

// --settings-apply-smoke: the settings phase 9a wired up take effect in the
// running app. Default speed on open and when the queue moves on, Remember
// volume writes volume.json (and nothing when off), Reduce motion turns off
// every transition and the pressed scale (and no longer keeps the controls
// up: Auto-hide the controls does), Compact narrows the control bar,
// and the cone stays orange in another accent. Restoring the volume at the
// next start needs a second run; the view model tests cover it.
public partial class MainWindow
{
    internal void EnableSettingsApplySmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Require(condition(), message);
                }
                double EngineRate() => VideoSurface.Player!.State.PlaybackRate;

                // Start from known values: other smokes share the profile.
                _settingsViewModel.RememberVolume = true;
                _settingsViewModel.ReduceMotion = false;
                _settings.Update(s => s with { AutoHideControls = true });
                _settingsViewModel.Density = "Comfortable";

                // Default speed: each new item starts at it; the speed menu changes only the current one.
                _settingsViewModel.DefaultSpeed = "1.5";
                Require(_settings.Current.DefaultSpeed == 1.5, "default speed saved");
                _presentation.OpenSource(RepositoryPaths.TestMediaPath, MediaSourceKind.File);
                await WaitFor(() => _presentation.IsPlaying && Math.Abs(EngineRate() - 1.5) < .01, "the first item starts at the default speed");
                Require(Math.Abs(_presentation.PlaybackRate - 1.5) < .01, "the player shows the default speed");
                _presentation.SetPlaybackRateCommand.Execute(.75);
                await WaitFor(() => Math.Abs(EngineRate() - .75) < .01, "the speed menu changes the current item");
                _presentation.OpenDroppedSources([RepositoryPaths.TestMediaPath, RepositoryPaths.TestMediaPath]);
                await WaitFor(() => _presentation.QueueItems.Count == 2 && _presentation.IsPlaying && Math.Abs(EngineRate() - 1.5) < .01, "a new queue starts at the default speed");
                _presentation.SetPlaybackRateCommand.Execute(.75);
                await WaitFor(() => Math.Abs(EngineRate() - .75) < .01, "the speed menu changes the first queue item");
                await WaitFor(() => _presentation.NextCommand.CanExecute(null), $"Next is available ({_presentation.QueueState.Status}, index {_presentation.QueueState.CurrentIndex}, repeat {_presentation.QueueState.Repeat}, order {_presentation.QueueState.Order})");
                _presentation.NextCommand.Execute(null);
                await WaitFor(() => _presentation.QueueState.CurrentIndex == 1 && Math.Abs(EngineRate() - 1.5) < .01, $"the next queue item starts at the default speed (index {_presentation.QueueState.CurrentIndex}, rate {EngineRate():0.###})");

                // Remember volume: written about 2 s after the volume settles, only while on.
                string volumeFile = PlayerProfile.FilePath("volume.json");
                _presentation.Volume = 35;
                if (!_presentation.IsMuted) _presentation.ToggleMuteCommand.Execute(null);
                await WaitFor(() => Math.Abs(VideoSurface.Player!.State.Volume - .35) < .01 && VideoSurface.Player.State.IsMuted, "VLC takes the volume and mute");
                await WaitFor(() => new FileVolumeMemory(volumeFile).Load() == new RememberedVolume(35, true), "volume.json holds the volume and mute", 6);
                _settingsViewModel.RememberVolume = false;
                _presentation.Volume = 55;
                _presentation.ToggleMuteCommand.Execute(null);
                await Task.Delay(2600);
                Require(new FileVolumeMemory(volumeFile).Load() == new RememberedVolume(35, true), "nothing is written while Remember volume is off");
                _presentation.SetPlaybackRateCommand.Execute(1d);
                _presentation.TogglePauseCommand.Execute(null);
                await WaitFor(() => _presentation.PlaybackStatus == PlaybackStatus.Paused, "pauses for the motion checks");

                // Reduce motion: no transitions anywhere, including Settings' scroll bars and newly loaded controls.
                OpenSettings();
                await Task.Delay(300);
                Animatable[] animated = Animated();
                Require(animated.Length > 0, "Fluent sets transitions while Reduce motion is off");
                Button press = StopButton;
                SetPressed(press, true);
                await Task.Delay(250);
                Require(press.RenderTransform is not null && press.RenderTransform.Value != Matrix.Identity, $"a pressed button scales while Reduce motion is off ({press.RenderTransform?.Value})");
                SetPressed(press, false);
                await Task.Delay(250);
                _settingsViewModel.ReduceMotion = true;
                await Task.Delay(200);
                Require(Classes.Contains("reduced-motion"), "the window carries the reduced-motion class");
                Require(Animated().Length == 0, $"no transitions with Reduce motion: {string.Join(", ", Animated().Select(a => a.GetType().Name).Distinct())}");
                _settingsViewModel.SelectedPage = _settingsViewModel.Pages.Single(page => page.Name == "Controls");
                await Task.Delay(300);
                Require(Animated().Length == 0, "controls loaded later have no transitions either");
                SetPressed(press, true);
                await Task.Delay(250);
                Require(press.RenderTransform is null || press.RenderTransform.Value == Matrix.Identity, "a pressed button does not scale with Reduce motion");
                SetPressed(press, false);
                Require(!OpeningIndicator.IsVisible, "the sliding Opening bar is hidden with Reduce motion");
                // Since phase 9d, Auto-hide the controls decides that on its own.
                Require(_chromeVisibility.AutoHide && _settings.Current.AutoHideControls, "Reduce motion leaves Auto-hide the controls on");
                CaptureSettingsApply("reduce-motion-on");
                _settingsViewModel.ReduceMotion = false;
                await Task.Delay(200);
                Require(Animated().Length > 0 && OpeningIndicator.IsVisible, "turning Reduce motion off restores the transitions");

                // Density: Compact narrows the control bar.
                CloseSettings();
                await Task.Delay(200);
                double comfortable = DockGrid.ColumnSpacing;
                _settingsViewModel.Density = "Compact";
                await Task.Delay(200);
                Require(comfortable == 20 && DockGrid.ColumnSpacing == 12 && PlayerDock.Padding.Left == 12, $"Compact narrows the control bar ({comfortable} -> {DockGrid.ColumnSpacing})");
                _settingsViewModel.Density = "Comfortable";

                // The cone stays orange in another accent.
                _settingsViewModel.Accent = AccentPalette.Colors.First(color => color.Hex != AccentPalette.DefaultHex).Hex;
                await Task.Delay(200);
                Avalonia.Controls.Shapes.Path cone = ProductMark.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
                Require(cone.Fill is ISolidColorBrush { Color: var logo } && logo == Color.Parse("#D37918"), $"the cone stays orange ({(cone.Fill as ISolidColorBrush)?.Color})");
                CaptureSettingsApply("player-other-accent");
                _settingsViewModel.Accent = AccentPalette.DefaultHex;
                _settingsViewModel.DefaultSpeed = "1";
                _settingsViewModel.RememberVolume = true;
                _presentation.Volume = 100;
                if (_presentation.IsMuted) _presentation.ToggleMuteCommand.Execute(null);

                Console.WriteLine("[settings-apply-smoke] PASS default speed on open, new queue and next item; volume.json written only while Remember volume is on; no transitions or pressed scale with Reduce motion, restored when off; Compact control bar; orange cone in another accent");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[settings-apply-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };

        Animatable[] Animated() => this.GetVisualDescendants().Prepend(this).OfType<Animatable>()
            .Where(target => target.Transitions is { Count: > 0 }).ToArray();
        static void SetPressed(Button button, bool pressed) => ((IPseudoClasses)button.Classes).Set(":pressed", pressed);
    }

    private void CaptureSettingsApply(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-apply");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(System.IO.Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
#endif // SMOKE

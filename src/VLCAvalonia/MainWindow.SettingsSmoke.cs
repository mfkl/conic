using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using VLCAvalonia.Interop;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia;

public partial class MainWindow
{
    internal void EnableSettingsReview()
    {
        WindowState = WindowState.Normal;
        Width = 1280; Height = 720;
        Opened += (_, _) => OpenSettings();
    }

    internal void EnableSettingsSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal;
        Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                OpenSettings();
                foreach (bool compact in new[] { false, true })
                {
                    Width = compact ? 480 : 1280;
                    Height = compact ? 360 : 720;
                    await Task.Delay(200);
                    foreach (SettingsPage page in _settingsViewModel.Pages)
                    {
                        _settingsViewModel.SelectedPage = page;
                        await Task.Delay(100);
                        Require(IsContained(SettingsPanel) && IsContained(SettingsPanel.SettingsSearch) && IsContained(SettingsPanel.SettingsContent), "Settings page fits window");
                        Require(compact == SettingsPanel.CompactSettingsPages.IsVisible, "responsive category navigation");
                        foreach (Control editor in SettingsPanel.SettingsContent.GetVisualDescendants().OfType<Control>()
                            .Where(c => c is Button or TextBox or ComboBox or NumericUpDown).Where(c => c.IsEffectivelyVisible))
                        {
                            Rect rect = GetClientRect(editor);
                            Rect viewport = GetClientRect(SettingsPanel.SettingsContent);
                            Require(rect.Left >= viewport.Left && rect.Right <= viewport.Right + 1, $"{page.Name} editor fits horizontally: {editor.GetType().Name} {rect}");
                        }
                        CaptureSettings($"settings-{page.Name.ToLowerInvariant()}-{(compact ? "compact" : "wide")}");
                    }
                }
                _settingsViewModel.Search = "no-such-setting-abcdef";
                await Task.Delay(100);
                Require(!_settingsViewModel.HasSettingsResults && !SettingsPanel.SettingsContent.IsVisible, "no results hides stale content");
                CaptureSettings("settings-no-results");
                _settingsViewModel.Search = "keyboard";
                await Task.Delay(100);
                Require(_settingsViewModel.IsHotkeys && SettingsPanel.SettingsContent.IsVisible, "search selects matching category");
                _settingsViewModel.ShortcutSearch = "no-such-shortcut-abcdef";
                Require(!_settingsViewModel.HasShortcutResults, "shortcut empty result");
                _settingsViewModel.ShortcutSearch = "";
                _settingsViewModel.Search = "";
                _settingsViewModel.SelectedPage = _settingsViewModel.Pages[2];
                _settingsViewModel.ResetAllCommand.Execute(null);
                SettingsPanel.SettingsResetRow.BringIntoView();
                await Task.Delay(150);
                Require(_settingsViewModel.ConfirmReset && IsContained(SettingsPanel.SettingsResetRow), "reset confirmation fits compact viewport");
                CaptureSettings("settings-reset-confirmation");
                CloseSettings();
                Require(!_settingsViewModel.ConfirmReset && _presentation.IsHomeOpen && HomeScreen.IsKeyboardFocusWithin, "close cancels confirmation and restores Home focus");
                OpenSettings();
                _settingsViewModel.OpenMediaInformationCommand.Execute(null);
                await Task.Delay(100);
                Require(!SettingsPanel.IsVisible && _presentation.IsQueueOpen && _presentation.IsInformationTab, "Media information opens the existing drawer");
                _presentation.IsQueueOpen = false;
                OpenSettings();
                KeyEventArgs escape = new() { Key = Key.Escape };
                OnPlayerKeyDown(this, escape);
                Require(escape.Handled && !SettingsPanel.IsVisible, "Escape closes Settings");
                Console.WriteLine("[settings-smoke] PASS all eight categories, 1280x720 and 480x360, editors, search/no results, reset confirmation, Home focus, Media information and Escape");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[settings-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }

    private void CaptureSettings(string name)
    {
        string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "settings-review");
        Directory.CreateDirectory(directory);
        using RenderTargetBitmap bitmap = new(new PixelSize((int)ClientSize.Width, (int)ClientSize.Height));
        bitmap.Render(VideoArea);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

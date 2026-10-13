#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using VLCAvalonia.Controls;
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
                    ValidateSettingsDialog(compact);
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
                Require(_settingsViewModel.IsControls && SettingsPanel.SettingsContent.IsVisible, "search selects matching category");
                _settingsViewModel.ShortcutSearch = "no-such-shortcut-abcdef";
                Require(!_settingsViewModel.HasShortcutResults, "shortcut empty result");
                _settingsViewModel.ShortcutSearch = "";
                _settingsViewModel.Search = "";
                await ValidateAppearanceChoicesAsync();
                // Privacy scrolls in the compact dialog, and the confirmation
                // wraps the reset row: let both lay out before scrolling it
                // into view.
                _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(page => page.Name == "Privacy");
                await Task.Delay(100);
                _settingsViewModel.ResetAllCommand.Execute(null);
                await Task.Delay(100);
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
                Console.WriteLine("[settings-smoke] PASS all twelve categories, dialog bounds, theme and accent radio groups applied live, 1280x720 and 480x360, editors, search/no results, reset confirmation, Home focus, Media information and Escape");
                desktop.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine($"[settings-smoke] FAIL {error}"); desktop.Shutdown(1); }
        };
    }

    // The dialog: at most 1048 wide, centered, below the title bar; compact,
    // it fills the window and the ComboBox replaces the category pane.
    private void ValidateSettingsDialog(bool compact)
    {
        Rect dialog = GetClientRect(SettingsPanel.SettingsDialog);
        Size client = ClientSize;
        if (compact)
            Require(dialog.Left <= 0.5 && dialog.Top <= 0.5 && dialog.Right >= client.Width - 0.5 && dialog.Bottom >= client.Height - 0.5, $"compact Settings fills the window: {dialog}");
        else
            Require(dialog.Width <= 1048.5 && dialog.Height <= 865.5 && dialog.Top >= PlatformChrome.TitleBarHeight && Math.Abs(dialog.Left - (client.Width - dialog.Right)) <= 1 && IsContained(SettingsPanel.SettingsDialog), $"Settings dialog is centered below the title bar: {dialog}");
        Require(SettingsPanel.SettingsCategoryPane.IsVisible != compact, "category pane only in the wide dialog");
    }

    // Theme and accent are radio groups: named options, one checked, only the
    // checked one is a tab stop, and arrow keys move and apply the choice.
    private async Task ValidateAppearanceChoicesAsync()
    {
        _settingsViewModel.SelectedPage = _settingsViewModel.Pages.First(page => page.Name == "General");
        await Task.Delay(100);
        RadioButton[] themes = SettingsPanel.ThemeSegments.GetVisualDescendants().OfType<RadioButton>().ToArray();
        RadioButton[] accents = SettingsPanel.AccentSwatches.GetVisualDescendants().OfType<RadioButton>().ToArray();
        Require(themes.Select(AutomationName).SequenceEqual(new[] { "Theme Dark", "Theme Light", "Theme System" }), "theme segments are named");
        Require(accents.Length == 9 && accents.All(option => AutomationName(option).StartsWith("Accent color ", StringComparison.Ordinal)), "nine named accent swatches");
        foreach (RadioButton[] group in new[] { themes, accents })
            Require(group.Count(option => option.IsChecked == true) == 1 && group.All(option => option.IsTabStop == (option.IsChecked == true)), "one checked option and tab stop per group");
        string accent = _settingsViewModel.Accent;
        RadioButton selected = accents.Single(option => option.IsChecked == true);
        selected.Focus(NavigationMethod.Tab);
        selected.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, Source = selected });
        await Task.Delay(100);
        Require(_settingsViewModel.Accent != accent && accents.Single(option => option.IsChecked == true).IsFocused, "Right arrow checks and focuses the next accent");
        RadioButton next = accents.Single(option => option.IsChecked == true);
        next.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left, Source = next });
        await Task.Delay(100);
        Require(_settingsViewModel.Accent == accent, "Left arrow returns to the previous accent");
        CaptureSettings("settings-general-focus");

        // The Theme applies live: Light swaps the palette, while the video
        // canvas and the picture-in-picture chrome over it stay dark.
        string theme = _settingsViewModel.Theme;
        themes.Single(option => AutomationName(option) == "Theme Light").IsChecked = true;
        await Task.Delay(150);
        Require(_settingsViewModel.Theme == "Light" && ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light, "Light theme applies live");
        Require(BrushColor(SettingsPanel, "Vlc.Bg.B1") == Avalonia.Media.Color.Parse("#E7E7E7") &&
            BrushColor(PictureInPictureChrome, "Vlc.Fg.1") == Avalonia.Media.Color.Parse("#EAEAEA") &&
            BrushColor(VideoArea, "PlayerCanvas") == Avalonia.Media.Colors.Black, "Light palette, dark video surfaces");
        CaptureSettings("settings-general-light");
        themes.Single(option => AutomationName(option) == $"Theme {theme}").IsChecked = true;
        await Task.Delay(150);
        Require(_settingsViewModel.Theme == theme, "theme restored");

        // The accent applies live to every accent brush, in both themes.
        Avalonia.Media.Color? orange = BrushColor(SettingsPanel, "PlayerAccent");
        accents.Single(option => AutomationName(option) == "Accent color Blue").IsChecked = true;
        await Task.Delay(150);
        Theming.AccentFamily blue = Theming.AccentTheme.Derive(Avalonia.Media.Color.Parse("#1B5DD3"), light: ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light);
        Require(_settingsViewModel.Accent == "#1B5DD3" && BrushColor(SettingsPanel, "PlayerAccent") == blue.Main &&
            BrushColor(SettingsPanel, "Vlc.Bg.AccentBright") == blue.BackgroundBright && BrushColor(PlayButton, "Vlc.Fg.OnAccent") == blue.OnAccent, "accent applies live");
        CaptureSettings("settings-general-blue");
        accents.Single(option => AutomationName(option) == "Accent color Orange").IsChecked = true;
        await Task.Delay(150);
        Require(BrushColor(SettingsPanel, "PlayerAccent") == orange, "accent restored");

        static string AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control) ?? "";
        static Avalonia.Media.Color? BrushColor(Control control, string key) =>
            control.TryFindResource(key, control.ActualThemeVariant, out object? value) && value is Avalonia.Media.ISolidColorBrush brush ? brush.Color : null;
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
#endif // SMOKE

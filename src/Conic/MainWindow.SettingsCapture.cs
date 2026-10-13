#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Conic.ViewModels;

namespace Conic;

// --settings-capture=<folder>: review captures of every Settings page in
// Dark and Light, at 1600x900 px (1067x600 DIP rendered at 150 %, as the
// earlier phases captured) and in the 480x360 compact dialog, scrolled from
// top to bottom (one image per screenful). Not a smoke: it checks nothing
// and always exits 0 once the images are written.
public partial class MainWindow
{
    internal void EnableSettingsCapture(IClassicDesktopStyleApplicationLifetime desktop, string folder)
    {
        WindowState = WindowState.Normal;
        Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                OpenSettings();
                // The Audio page shows its equalizer open, to review the bands.
                _settingsViewModel.IsEqualizerOpen = true;
                foreach (string theme in new[] { "Dark", "Light" })
                {
                    _settingsViewModel.Theme = theme;
                    foreach ((string size, double width, double height, double scale) in new[] { ("1600x900", 1600 / 1.5, 900 / 1.5, 1.5), ("compact", 480d, 360d, 1d) })
                    {
                        Width = width; Height = height;
                        await Task.Delay(300);
                        foreach (SettingsPage page in _settingsViewModel.Pages)
                        {
                            _settingsViewModel.SelectedPage = page;
                            await Task.Delay(200);
                            ScrollViewer content = SettingsPanel.SettingsContent;
                            content.Offset = default;
                            await Task.Delay(100);
                            int part = 1;
                            while (true)
                            {
                                Capture(Path.Combine(folder, $"{theme.ToLowerInvariant()}-settings-{page.Name.ToLowerInvariant()}-{size}-{part}.png"), scale);
                                double next = content.Offset.Y + content.Viewport.Height * .9;
                                if (content.Offset.Y + content.Viewport.Height >= content.Extent.Height - 1 || part >= 8) break;
                                content.Offset = new Vector(0, Math.Min(next, content.Extent.Height - content.Viewport.Height));
                                await Task.Delay(150);
                                part++;
                            }
                        }
                    }
                }
                _settingsViewModel.Theme = "Dark";
            }
            catch (Exception error) { Console.Error.WriteLine($"[settings-capture] {error}"); }
            desktop.Shutdown(0);
        };

        void Capture(string path, double scale)
        {
            using RenderTargetBitmap bitmap = new(new PixelSize((int)Math.Round(ClientSize.Width * scale), (int)Math.Round(ClientSize.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(VideoArea);
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
        }
    }
}
#endif // SMOKE

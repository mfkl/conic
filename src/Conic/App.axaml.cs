using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Conic;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            desktop.MainWindow = window;
            if (desktop.Args?.Contains("--drawer-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableDrawerSmoke(desktop, false);
            else if (desktop.Args?.Contains("--drawer-native-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableDrawerSmoke(desktop, true);
            else if (desktop.Args?.Contains("--stop-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableStopSmoke(desktop);
            else if (desktop.Args?.Contains("--thumbnail-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableThumbnailSmoke(desktop, false);
            else if (desktop.Args?.Contains("--thumbnail-review", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableThumbnailSmoke(desktop, true);
            else if (desktop.Args?.Contains("--media-information-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableMediaInformationSmoke(desktop);
            else if (desktop.Args?.Contains("--settings-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableSettingsSmoke(desktop);
            else if (desktop.Args?.Contains("--settings-review", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableSettingsReview();
            else if (desktop.Args?.Contains("--pip-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnablePictureInPictureSmoke(desktop);
            else if (desktop.Args?.Contains("--pip-review", StringComparer.OrdinalIgnoreCase) == true)
                window.EnablePictureInPictureReview();
            else if (desktop.Args?.Contains("--home-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableHomeSmoke(desktop, false);
            else if (desktop.Args?.Contains("--home-session-smoke", StringComparer.OrdinalIgnoreCase) == true)
                window.EnableHomeSmoke(desktop, true);
            else if (desktop.Args?.Contains(
                    "--adapter-mismatch-smoke",
                    StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnableAdapterMismatchSmoke(desktop);
            }
            else if (desktop.Args?.Contains(
                    "--ui-smoke",
                    StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnableGPUCompositionSmoke(desktop);
            }
            else if (desktop.Args?.Contains("--playback-controls-smoke", StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnablePlaybackControlsSmoke(desktop);
            }
            else if (desktop.Args?.Contains("--layout-smoke", StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnableLayoutSmoke(desktop);
            }
            else if (desktop.Args?.Contains("--playback-review", StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnablePlaybackReview();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

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
            if (desktop.Args?.Contains(
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

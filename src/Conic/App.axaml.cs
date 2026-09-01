using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
#if DEBUG && AVALONIA_DIAGNOSTICS
using Avalonia.Diagnostics;
#endif

namespace Conic;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG && AVALONIA_DIAGNOSTICS
        this.AttachDeveloperTools();
#endif
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
            else if (desktop.Args?.Contains("--layout-smoke", StringComparer.OrdinalIgnoreCase) == true)
            {
                window.EnableLayoutSmoke(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

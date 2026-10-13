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
            if (desktop.Args?.Contains("--graphics-probe", StringComparer.OrdinalIgnoreCase) == true)
            {
                desktop.MainWindow = Video.GraphicsInteropProbe.CreateWindow(desktop, desktop.Args);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (Program.StartupFailure is { } failure)
            {
                desktop.MainWindow = new Views.StartupFailureWindow(failure);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            MainWindow window = new();
            desktop.MainWindow = window;
#if SMOKE
            EnableSmokeMode(window, desktop.Args);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

#if SMOKE
    // Every interactive smoke mode drives the real styled window; the first
    // matching flag wins, and no flag means the window runs as the product.
    private static void EnableSmokeMode(
        MainWindow window, string[]? args)
    {
        if (args is null) return;
        if (args.Contains("--video-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableVideoSmoke(desktopArg(), false);
        else if (args.Contains("--video-cpu-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableVideoSmoke(desktopArg(), true);
        else if (args.Contains("--accessibility-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableAccessibilitySmoke(desktopArg(), true);
        else if (args.Contains("--scale-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableAccessibilitySmoke(desktopArg(), false);
        else if (args.Contains("--drawer-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableDrawerSmoke(desktopArg(), false);
        else if (args.Contains("--drawer-native-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableDrawerSmoke(desktopArg(), true);
        else if (args.Contains("--stop-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableStopSmoke(desktopArg());
        else if (args.Contains("--caption-click-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableCaptionClickSmoke(desktopArg());
        else if (args.Contains("--thumbnail-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableThumbnailSmoke(desktopArg(), false);
        else if (args.Contains("--thumbnail-review", StringComparer.OrdinalIgnoreCase))
            window.EnableThumbnailSmoke(desktopArg(), true);
        else if (args.Contains("--media-information-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableMediaInformationSmoke(desktopArg());
        else if (args.Contains("--settings-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableSettingsSmoke(desktopArg());
        else if (args.FirstOrDefault(arg => arg.StartsWith("--settings-capture=", StringComparison.OrdinalIgnoreCase)) is { } capture)
            window.EnableSettingsCapture(desktopArg(), capture["--settings-capture=".Length..]);
        else if (args.Contains("--settings-review", StringComparer.OrdinalIgnoreCase))
            window.EnableSettingsReview();
        else if (args.Contains("--pip-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnablePictureInPictureSmoke(desktopArg());
        else if (args.Contains("--pip-review", StringComparer.OrdinalIgnoreCase))
            window.EnablePictureInPictureReview();
        else if (args.Contains("--home-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableHomeSmoke(desktopArg(), false);
        else if (args.Contains("--home-session-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableHomeSmoke(desktopArg(), true);
        else if (args.Contains("--adapter-mismatch-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableAdapterMismatchSmoke(desktopArg());
        else if (args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableGPUCompositionSmoke(desktopArg());
        else if (args.Contains("--playback-controls-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnablePlaybackControlsSmoke(desktopArg());
        else if (args.Contains("--layout-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableLayoutSmoke(desktopArg());
        else if (args.Contains("--playback-review", StringComparer.OrdinalIgnoreCase))
            window.EnablePlaybackReview();
        else if (args.Contains("--settings-apply-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableSettingsApplySmoke(desktopArg());
        else if (args.Contains("--engine-settings-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableEngineSettingsSmoke(desktopArg());
        else if (args.Contains("--menu-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableMenuSmoke(desktopArg());
        else if (args.FirstOrDefault(arg => arg.StartsWith("--menu-review=", StringComparison.OrdinalIgnoreCase)) is { } review)
        {
            // --menu-review=<step>[,WIDTHxHEIGHT]: the size is in DIPs.
            string[] parts = review["--menu-review=".Length..].Split(',');
            string[] size = parts.Length > 1 ? parts[1].Split('x') : [];
            window.EnableMenuReview(parts[0], size.Length == 2 && double.TryParse(size[0], System.Globalization.CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(size[1], System.Globalization.CultureInfo.InvariantCulture, out double height) ? new Avalonia.Size(width, height) : null);
        }

        IClassicDesktopStyleApplicationLifetime desktopArg() =>
            (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
    }
#endif
}

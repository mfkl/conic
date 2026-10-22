using Avalonia;
using Avalonia.Controls;
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
            if (OperatingSystem.IsLinux() && desktop.Args?.Contains("--dmabuf-probe", StringComparer.OrdinalIgnoreCase) == true)
            {
                desktop.MainWindow = Video.Linux.DmaBufProbe.CreateWindow(desktop, desktop.Args);
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
            if (!IsSmokeOrReview(desktop.Args))
#endif
                OpenRequestedMedia(window, desktop.Args);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // "conic movie.mkv", and the file manager's "Open with": on Windows and
    // Linux the files come as arguments, on macOS as an open-files event.
    private void OpenRequestedMedia(MainWindow window, string[]? args)
    {
        IReadOnlyList<string> startup = Player.Application.CommandLineSources.From(args);
        window.OpenOnStart(startup);
        if (Program.ListensForOtherStarts)
        {
            Services.SingleInstance listener = Services.SingleInstance.Listen(sources => Avalonia.Threading.Dispatcher.UIThread.Post(() => window.OpenFromOtherStart(sources)));
            window.Closed += (_, _) => listener.Dispose();
        }
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            // "Conic a.mkv b.mkv" from a terminal: AppKit also sends a.mkv and
            // b.mkv as open-files events, which must not open them a second time.
            string workingDirectory = Environment.CurrentDirectory;
            List<string> echoed = OperatingSystem.IsMacOS() ? Player.Application.CommandLineSources.EchoedByMacOS(startup, workingDirectory) : [];
            activatable.Activated += (_, e) =>
            {
                if (e is not FileActivatedEventArgs { Files.Count: > 0 } opened) return;
                string[] files = opened.Files.Select(file => file.Path.IsFile ? file.Path.LocalPath : file.Path.AbsoluteUri).ToArray();
                window.OpenOnStart(Player.Application.CommandLineSources.NotOnCommandLine(files, echoed, workingDirectory));
            };
        }
    }

#if SMOKE
    private static bool IsSmokeOrReview(string[]? args) => args?.Any(arg => arg.StartsWith("--", StringComparison.Ordinal) &&
        (arg.Contains("smoke", StringComparison.OrdinalIgnoreCase) || arg.Contains("capture", StringComparison.OrdinalIgnoreCase) || arg.Contains("review", StringComparison.OrdinalIgnoreCase))) == true;

    // Every interactive smoke mode drives the real styled window; the first
    // matching flag wins, and no flag means the window runs as the product.
    private static void EnableSmokeMode(
        MainWindow window, string[]? args)
    {
        if (args is null) return;
        // Smoke and capture windows open without taking focus, so test runs
        // do not pull the user out of other apps. The screen checks keep
        // their windows Topmost instead; only --menu-smoke and --interactions-smoke
        // activate their window, because their focus checks need an active one.
        if (args.Any(arg => arg.StartsWith("--", StringComparison.Ordinal) &&
            (arg.Contains("smoke", StringComparison.OrdinalIgnoreCase) || arg.Contains("capture", StringComparison.OrdinalIgnoreCase) || arg.Contains("review", StringComparison.OrdinalIgnoreCase))))
            window.ShowActivated = false;
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
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) window.EnableEngineSettingsSmoke(desktopArg());
            else throw new PlatformNotSupportedException("The settings smokes run on Windows, macOS and Linux.");
        }
        else if (args.Contains("--audio-settings-smoke", StringComparer.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) window.EnableAudioSettingsSmoke(desktopArg());
            else throw new PlatformNotSupportedException("The settings smokes run on Windows, macOS and Linux.");
        }
        else if (args.Contains("--interface-settings-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableInterfaceSettingsSmoke(desktopArg());
        else if (args.Contains("--interactions-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableInteractionsSmoke(desktopArg());
        else if (args.Contains("--protocols-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableProtocolsSmoke(desktopArg());
        else if (args.Contains("--parity-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableParitySmoke(desktopArg());
        else if (args.Contains("--messages-smoke", StringComparer.OrdinalIgnoreCase))
            window.EnableMessagesSmoke(desktopArg());
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

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VLCAvalonia.Controls;

// In-app window caption buttons. With ExtendClientAreaToDecorationsHint the
// platform draws a light caption over the dark chrome; these replace it.
// macOS keeps its own traffic lights instead (PlatformChrome).
public partial class WindowCaptionButtons : UserControl
{
    private Window? _window;
    private bool _subscribed;

    public WindowCaptionButtons()
    {
        InitializeComponent();
        IsVisible = !PlatformChrome.UsesSystemCaptionButtons;
        AttachedToVisualTree += (_, _) => Connect();
    }

    private void Connect()
    {
        if (_subscribed) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        _window = window;
        _subscribed = true;
        UpdateMaximizeIcon(window.WindowState);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != nameof(Window.WindowState)) return;
            bool fullscreen = window.WindowState == WindowState.FullScreen;
            IsVisible = !fullscreen && !PlatformChrome.UsesSystemCaptionButtons;
            UpdateMaximizeIcon(window.WindowState);
        };
    }

    private void UpdateMaximizeIcon(WindowState state)
    {
        if (this.TryFindResource(state == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize", out object? icon))
            MaximizeIcon.Icon = icon as IconData;
        ToolTip.SetTip(MaximizeButton, state == WindowState.Maximized ? "Restore" : "Maximize");
        AutomationProperties.SetName(MaximizeButton, state == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (_window is not null) _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => _window?.Close();
}

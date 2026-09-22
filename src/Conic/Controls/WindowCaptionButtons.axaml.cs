using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Conic.Controls;

// In-app window caption buttons. With ExtendClientAreaToDecorationsHint the
// platform draws a light caption over the dark chrome; these replace it.
public partial class WindowCaptionButtons : UserControl
{
    private Window? _window;
    private bool _subscribed;

    public WindowCaptionButtons()
    {
        InitializeComponent();
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
            IsVisible = !fullscreen;
            UpdateMaximizeIcon(window.WindowState);
        };
    }

    private void UpdateMaximizeIcon(WindowState state)
    {
        MaximizeIcon.Data = state == WindowState.Maximized
            ? Geometry.Parse("M1,1 H6 V3 H3 V6 H1 Z M3,3 H10 V10 H3 Z M4,4 V9 H9 V4 Z")
            : Geometry.Parse("M0,0 H10 V10 H0 Z M1,1 V9 H9 V1 Z");
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

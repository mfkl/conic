using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Conic.Controls;

// In-app window caption buttons. With ExtendClientAreaToDecorationsHint the
// platform draws a light caption over the dark chrome; these replace it.
// macOS keeps its own traffic lights instead (PlatformChrome), and fullscreen
// has no caption, but the Accessory (the player's fullscreen toggle) stays
// in both so fullscreen can always be left from the title bar.
public partial class WindowCaptionButtons : UserControl
{
    public static readonly StyledProperty<Control?> AccessoryProperty =
        AvaloniaProperty.Register<WindowCaptionButtons, Control?>(nameof(Accessory));

    private Window? _window;
    private bool _subscribed;

    public WindowCaptionButtons()
    {
        InitializeComponent();
        ShowSystemButtons(!PlatformChrome.UsesSystemCaptionButtons);
        AttachedToVisualTree += (_, _) => Connect();
    }

    /// <summary>Shown between maximize and close, on every platform and in fullscreen.</summary>
    public Control? Accessory
    {
        get => GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccessoryProperty) AccessoryPresenter.Content = change.NewValue;
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
            ShowSystemButtons(!fullscreen && !PlatformChrome.UsesSystemCaptionButtons);
            UpdateMaximizeIcon(window.WindowState);
        };
    }

    private void ShowSystemButtons(bool visible)
    {
        MinimizeButton.IsVisible = visible;
        MaximizeButton.IsVisible = visible;
        CloseButton.IsVisible = visible;
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

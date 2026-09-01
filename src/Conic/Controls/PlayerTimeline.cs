using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Conic.Controls;

public sealed class PlayerTimeline : Slider
{
    public event EventHandler? SeekStarted;
    public event EventHandler? SeekCommitted;

    public PlayerTimeline()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        PointerCaptureLost += (_, _) => SeekCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            SeekStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        SeekCommitted?.Invoke(this, EventArgs.Empty);
}

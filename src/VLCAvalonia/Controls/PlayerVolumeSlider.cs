using Avalonia.Controls;
using Avalonia;

namespace VLCAvalonia.Controls;

/// <summary>Named player control so volume chrome can evolve independently.</summary>
public sealed class PlayerVolumeSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);

    private bool _dragging;
    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;

    public PlayerVolumeSlider()
    {
        AddHandler(PointerPressedEvent, OnDragPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, (_, _) => CompleteDrag(), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
    }

    private void OnDragPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (IsEnabled && !_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerCaptureLost(Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CompleteDrag();
    }

    private void CompleteDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty) PseudoClasses.Set(":amplified", Value > 100);
    }
}

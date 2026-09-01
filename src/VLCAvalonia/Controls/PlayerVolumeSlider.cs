using Avalonia.Controls;

namespace VLCAvalonia.Controls;

/// <summary>Named player control so volume chrome can evolve independently.</summary>
public sealed class PlayerVolumeSlider : Slider
{
    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        DragStarted?.Invoke(this, EventArgs.Empty);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        DragCompleted?.Invoke(this, EventArgs.Empty);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        DragCompleted?.Invoke(this, EventArgs.Empty);
        base.OnPointerCaptureLost(e);
    }
}

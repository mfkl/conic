using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VLCAvalonia.Controls;

public sealed class PlayerTimeline : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);

    private bool _seeking;
    public event EventHandler? SeekStarted;
    public event EventHandler? SeekCommitted;

    public PlayerTimeline()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        PointerCaptureLost += (_, _) => CommitOnce();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsEnabled && !_seeking && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _seeking = true;
            SeekStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => CommitOnce();

    private void CommitOnce()
    {
        if (!_seeking) return;
        _seeking = false;
        SeekCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEnabled && e.Key is Key.Home or Key.End or Key.Left or Key.Right)
        {
            _seeking = true;
            SeekStarted?.Invoke(this, EventArgs.Empty);
            Value = e.Key switch
            {
                Key.Home => Minimum,
                Key.End => Maximum,
                Key.Left => Math.Max(Minimum, Value - SmallChange),
                _ => Math.Min(Maximum, Value + SmallChange),
            };
            CommitOnce();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Automation.Peers;

namespace VLCAvalonia.Controls;

public sealed class PlayerTimeline : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);
    protected override AutomationPeer OnCreateAutomationPeer() => new TimelineAutomationPeer(this);

    private sealed class TimelineAutomationPeer(PlayerTimeline owner) : SliderAutomationPeer(owner), IRangeValueProvider
    {
        void IRangeValueProvider.SetValue(double value)
        {
            if (!owner.IsEffectivelyEnabled) throw new InvalidOperationException("Playback position is unavailable.");
            if (!double.IsFinite(value) || value < owner.Minimum || value > owner.Maximum)
                throw new ArgumentOutOfRangeException(nameof(value));
            owner._seeking = true;
            owner.SeekStarted?.Invoke(owner, EventArgs.Empty);
            try { base.SetValue(value); }
            finally { owner.CommitOnce(); }
        }
    }

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

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace Conic.Theming;

/// <summary>
/// Settings › Interface › Reduce motion, for the whole app: while it is on,
/// no control runs a transition. The app's own styles have none; Fluent's
/// are the button press scale, the scroll bars' fade and growth and the
/// tooltip fade. Fluent sets some of them inside its templates, and template
/// values outrank styles, so they are overridden here at animation priority:
/// every control that is loaded, or gets transitions, while the setting is on.
/// Turning it off removes the overrides. The rest (the pressed scale itself
/// and the sliding "Opening" bar) is styled on the window's reduced-motion
/// class in Styles/PlayerChrome.axaml.
/// </summary>
internal static class ReducedMotion
{
    private static readonly ConditionalWeakTable<Animatable, IDisposable> Overrides = new();
    private static bool _hooked;

    public static bool IsEnabled { get; private set; }

    /// <summary>Turns reduced motion on or off for everything under <paramref name="root"/> now and every control loaded later.</summary>
    public static void Apply(bool enabled, Visual root)
    {
        if (!_hooked)
        {
            _hooked = true;
            Animatable.TransitionsProperty.Changed.AddClassHandler<Animatable>((target, _) => Suppress(target));
            Control.LoadedEvent.AddClassHandler<Control>((control, _) => Suppress(control));
        }
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (enabled)
        {
            Suppress(root);
            foreach (Visual visual in root.GetVisualDescendants()) Suppress(visual);
            return;
        }
        foreach (KeyValuePair<Animatable, IDisposable> entry in Overrides.ToArray()) entry.Value.Dispose();
        Overrides.Clear();
    }

    private static void Suppress(Animatable target)
    {
        if (!IsEnabled || target.Transitions is not { Count: > 0 } || Overrides.TryGetValue(target, out _)) return;
        if (target.SetValue(Animatable.TransitionsProperty, null, BindingPriority.Animation) is { } handle)
            Overrides.Add(target, handle);
    }
}

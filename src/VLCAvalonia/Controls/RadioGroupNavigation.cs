using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace VLCAvalonia.Controls;

/// <summary>
/// Radio-group keyboard behavior for the radio buttons inside a container
/// (Theme segments, accent swatches): arrow keys move to the previous or next
/// option and check it, Home and End go to the first and last. Bind each
/// radio button's <c>IsTabStop</c> to its checked state so Tab enters the
/// group once, on the checked option.
/// </summary>
public static class RadioGroupNavigation
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(RadioGroupNavigation));

    static RadioGroupNavigation()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true) control.AddHandler(InputElement.KeyDownEvent, OnKeyDown);
            else control.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        });
    }

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control group || e.KeyModifiers != KeyModifiers.None) return;
        RadioButton[] options = group.GetVisualDescendants().OfType<RadioButton>().Where(option => option.IsEffectivelyVisible && option.IsEnabled).ToArray();
        if (options.Length == 0) return;
        int current = Array.FindIndex(options, option => option.IsFocused);
        if (current < 0) current = Math.Max(0, Array.FindIndex(options, option => option.IsChecked == true));
        int target = e.Key switch
        {
            Key.Left or Key.Up => (current - 1 + options.Length) % options.Length,
            Key.Right or Key.Down => (current + 1) % options.Length,
            Key.Home => 0,
            Key.End => options.Length - 1,
            _ => -1,
        };
        if (target < 0) return;
        options[target].IsChecked = true;
        options[target].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Conic;

/// <summary>Keeps application shortcuts off controls that already own text, space, arrows, or menus.</summary>
internal static class ShortcutFocusPolicy
{
    public static bool Suppresses(object? source)
    {
        if (source is not Visual visual) return false;
        foreach (Visual candidate in visual.GetSelfAndVisualAncestors())
            if (candidate is TextBox or Slider or NumericUpDown or ComboBox or MenuItem or Menu or Button)
                return true;
        return false;
    }
}

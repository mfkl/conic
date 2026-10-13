using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Player.Application;

namespace VLCAvalonia.Controls;

/// <summary>
/// Shows each menu option's effective keyboard shortcut (the user's override
/// or the default), as the design's menus do. Mark an option with
/// <c>controls:MenuShortcuts.Command="TogglePause"</c>; <see cref="Apply"/>
/// fills the hint of every marked <see cref="MenuRow"/> and
/// <see cref="MenuItem"/> (submenus included) when the menu opens, so a
/// remapped shortcut shows the next time it opens.
/// </summary>
public static class MenuShortcuts
{
    /// <summary>The <see cref="PlayerCommandId"/> name whose shortcut the option shows.</summary>
    public static readonly AttachedProperty<string?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Command", typeof(MenuShortcuts));

    public static string? GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, string? value) => control.SetValue(CommandProperty, value);

    private static PlayerCommandId? CommandOf(Control control) =>
        Enum.TryParse(GetCommand(control), out PlayerCommandId command) ? command : null;

    public static void Apply(ILogical root, IReadOnlyDictionary<PlayerCommandId, string> shortcuts)
    {
        if (root is MenuRow rootRow) Apply(rootRow, shortcuts);
        foreach (ILogical child in root.GetLogicalDescendants())
        {
            if (child is MenuRow row) Apply(row, shortcuts);
        }
        if (root is ItemsControl menu) ApplyItems(menu, shortcuts);
    }

    private static void Apply(MenuRow row, IReadOnlyDictionary<PlayerCommandId, string> shortcuts)
    {
        if (CommandOf(row) is { } command)
            row.Shortcut = shortcuts.TryGetValue(command, out string? gesture) ? PlayerShortcutPolicy.Display(gesture) : null;
    }

    private static void ApplyItems(ItemsControl menu, IReadOnlyDictionary<PlayerCommandId, string> shortcuts)
    {
        foreach (object? item in menu.Items)
        {
            if (item is not MenuItem menuItem) continue;
            if (CommandOf(menuItem) is { } command)
                menuItem.InputGesture = shortcuts.TryGetValue(command, out string? gesture) ? ToKeyGesture(gesture) : null;
            ApplyItems(menuItem, shortcuts);
        }
    }

    /// <summary>A display gesture for a shortcut such as "Ctrl+O", "Alt+Left" or "Ctrl+,"; null if Avalonia cannot show it.</summary>
    public static KeyGesture? ToKeyGesture(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return null;
        string[] parts = gesture.Split('+');
        if (parts[^1] == string.Empty && parts.Length > 1) return null;
        parts[^1] = parts[^1] switch { "," => "OemComma", "[" => "OemOpenBrackets", "]" => "OemCloseBrackets", "=" => "OemPlus", string key => key };
        try { return KeyGesture.Parse(string.Join('+', parts)); }
        catch (ArgumentException) { return null; }
        catch (FormatException) { return null; }
    }
}

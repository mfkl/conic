using Avalonia;
using Avalonia.Controls;

namespace Conic.Controls;

/// <summary>
/// An option of the More menu (design 6062:13354 "option"): a 24 accent
/// <see cref="Icon"/>, the label (the button's content, also its accessible
/// name), and either a right-aligned <see cref="Shortcut"/> hint or, with
/// <see cref="HasSubmenu"/>, a chevron. 48 tall; the template and states are
/// in Styles/PlayerChrome.axaml ("Menus").
/// </summary>
public class MenuRow : Button
{
    public static readonly StyledProperty<IconData?> IconProperty =
        AvaloniaProperty.Register<MenuRow, IconData?>(nameof(Icon));

    public static readonly StyledProperty<string?> ShortcutProperty =
        AvaloniaProperty.Register<MenuRow, string?>(nameof(Shortcut));

    public static readonly StyledProperty<bool> HasSubmenuProperty =
        AvaloniaProperty.Register<MenuRow, bool>(nameof(HasSubmenu));

    public IconData? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The effective keyboard shortcut, as the user sees it (set from <see cref="MenuShortcuts"/>).</summary>
    public string? Shortcut
    {
        get => GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public bool HasSubmenu
    {
        get => GetValue(HasSubmenuProperty);
        set => SetValue(HasSubmenuProperty, value);
    }

    // Styled by its own template, not Fluent's Button theme.
    protected override Type StyleKeyOverride => typeof(MenuRow);
}

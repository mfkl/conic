using Avalonia.Controls;
using Avalonia.Input;
using Player.Application;
using Conic.Controls;
using Xunit;

namespace Conic.UITests;

/// <summary>The design's menus: shortcut hints follow the effective shortcuts,
/// the current speed is checked, and hints keep their contrast.</summary>
public sealed class MenuTests
{
    [Fact]
    public void MenuRowsAndNestedMenuItemsShowTheEffectiveShortcut()
    {
        MenuRow open = new() { Content = "Open File…" };
        MenuShortcuts.SetCommand(open, nameof(PlayerCommandId.OpenMedia));
        MenuRow recent = new() { Content = "Open Recent", Shortcut = "stale" };
        StackPanel more = new() { Children = { open, recent } };
        MenuItem skip = new() { Header = "Skip back" };
        MenuShortcuts.SetCommand(skip, nameof(PlayerCommandId.SkipBackward));
        MenuItem unassigned = new() { Header = "Next item" };
        MenuShortcuts.SetCommand(unassigned, nameof(PlayerCommandId.QueueNext));
        ContextMenu menu = new() { Items = { new MenuItem { Header = "Playback", Items = { skip, unassigned } } } };
        Dictionary<PlayerCommandId, string> shortcuts = new()
        {
            [PlayerCommandId.OpenMedia] = "Ctrl+M",
            [PlayerCommandId.SkipBackward] = "Alt+Left",
        };

        MenuShortcuts.Apply(more, shortcuts);
        MenuShortcuts.Apply(menu, shortcuts);

        Assert.Equal(PlayerShortcutPolicy.Display("Ctrl+M"), open.Shortcut);
        Assert.Equal("stale", recent.Shortcut);
        Assert.Equal(new KeyGesture(Key.Left, KeyModifiers.Alt), skip.InputGesture);
        Assert.Null(unassigned.InputGesture);

        shortcuts.Remove(PlayerCommandId.OpenMedia);
        MenuShortcuts.Apply(more, shortcuts);
        Assert.Null(open.Shortcut);
    }

    [Theory]
    [InlineData("Ctrl+O", Key.O, KeyModifiers.Control)]
    [InlineData("Ctrl+Shift+O", Key.O, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Ctrl+,", Key.OemComma, KeyModifiers.Control)]
    [InlineData("Space", Key.Space, KeyModifiers.None)]
    [InlineData("F11", Key.F11, KeyModifiers.None)]
    [InlineData("Ctrl+1", Key.D1, KeyModifiers.Control)]
    [InlineData("Alt+0", Key.D0, KeyModifiers.Alt)]
    public void ShortcutsBecomeMenuGestures(string gesture, Key key, KeyModifiers modifiers) =>
        Assert.Equal(new KeyGesture(key, modifiers), MenuShortcuts.ToKeyGesture(gesture));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NoSuchKey")]
    public void UnusableShortcutsShowNoGesture(string? gesture) =>
        Assert.Null(MenuShortcuts.ToKeyGesture(gesture));

    [Theory]
    [InlineData(1.5, "1.5", true)]
    [InlineData(1.0, "1", true)]
    [InlineData(1.0, "2", false)]
    [InlineData(0.5, "abc", false)]
    public void TheCurrentSpeedIsChecked(double rate, string parameter, bool expected) =>
        Assert.Equal(expected, RateEqualsConverter.Instance.Convert(rate, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void MenuTextKeepsItsContrastOnTheMenu(string theme)
    {
        var resources = PaletteTests.Resources(theme);
        uint menu = PaletteTests.Color(resources, "MenuFlyoutPresenterBackground");
        foreach (string text in new[] { "MenuFlyoutItemForeground", "MenuFlyoutItemKeyboardAcceleratorTextForeground" })
            Assert.True(PaletteTests.Contrast(PaletteTests.Color(resources, text), menu) >= 4.5, $"{theme}: {text}");
    }
}

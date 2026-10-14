using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Player.Application;
using Conic.Commands;

namespace Conic;

// The macOS menu bar. Items run the same registry commands as the in-app
// chrome; their key equivalents are the effective shortcuts, so a remapped
// shortcut shows (and works) in the menu too.
public partial class MainWindow
{
    private readonly List<(NativeMenuItem Item, PlayerCommandId Command)> _macMenuCommands = [];

    private void InitializeMacMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;
        BuildMacMenu();
        UpdateMacMenuGestures();
        _settings.Changed += (_, _) => UpdateMacMenuGestures();
    }

    // Built once: Avalonia's macOS exporter binds the window to its first
    // NativeMenu and throws when given another one, so later changes edit
    // these items in place.
    private void BuildMacMenu()
    {
        NativeMenuItem Item(string header, PlayerCommandId command)
        {
            NativeMenuItem item = new(header) { Command = new RegistryCommand(_presentation.Registry, command) };
            _macMenuCommands.Add((item, command));
            return item;
        }
        NativeMenuItem Action(string header, Action action, KeyGesture? gesture = null) => new(header)
        {
            Command = new RelayCommand(_ => action()),
            Gesture = gesture,
        };
        NativeMenuItem Submenu(string header, params NativeMenuItemBase[] items)
        {
            NativeMenu menu = [];
            foreach (NativeMenuItemBase item in items) menu.Items.Add(item);
            return new NativeMenuItem(header) { Menu = menu };
        }

        NativeMenu bar =
        [
            Submenu("File",
                Item("Open File…", PlayerCommandId.OpenMedia),
                Item("Open Folder…", PlayerCommandId.OpenFolder),
                Item("Open Network Stream…", PlayerCommandId.OpenUrl),
                new NativeMenuItemSeparator(),
                Item("Load Playlist…", PlayerCommandId.LoadPlaylist),
                Item("Save Playlist…", PlayerCommandId.SavePlaylist),
                new NativeMenuItemSeparator(),
                Item("Media Information", PlayerCommandId.MediaInformation),
                new NativeMenuItemSeparator(),
                Action("Close Window", Close, new KeyGesture(Key.W, KeyModifiers.Meta))),
            Submenu("Playback",
                Item("Play/Pause", PlayerCommandId.TogglePause),
                Item("Stop", PlayerCommandId.Stop),
                new NativeMenuItemSeparator(),
                Item("Previous", PlayerCommandId.QueuePrevious),
                Item("Next", PlayerCommandId.QueueNext),
                Item("Skip Backward", PlayerCommandId.SkipBackward),
                Item("Skip Forward", PlayerCommandId.SkipForward),
                new NativeMenuItemSeparator(),
                Item("Mute", PlayerCommandId.ToggleMute),
                Item("Tracks and Synchronization", PlayerCommandId.ToggleMediaControls),
                Item("Playback Queue", PlayerCommandId.ToggleQueue),
                new NativeMenuItemSeparator(),
                Item("Take Snapshot", PlayerCommandId.Snapshot)),
            Submenu("View",
                Item("Home", PlayerCommandId.ShowHome),
                Item("Library", PlayerCommandId.OpenLibrary),
                new NativeMenuItemSeparator(),
                Item("Picture in Picture", PlayerCommandId.TogglePictureInPicture),
                Action("Enter Full Screen", () => _presentation.Registry.Execute(PlayerCommandId.ToggleFullscreen),
                    new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Meta))),
            Submenu("Window",
                Action("Minimize", () => WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)),
                Action("Zoom", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
                new NativeMenuItemSeparator(),
                Item("Messages", PlayerCommandId.ShowMessages)),
        ];
        NativeMenu.SetMenu(this, bar);

        // Items added to the application menu, after "About".
        NativeMenu application = [Item("Settings…", PlayerCommandId.OpenSettings)];
        if (Application.Current is { } app) NativeMenu.SetMenu(app, application);
    }

    private void UpdateMacMenuGestures()
    {
        IReadOnlyDictionary<PlayerCommandId, string> shortcuts =
            PlayerShortcutPolicy.Effective(_presentation.Registry, _settings.Current.ShortcutOverrides);
        foreach ((NativeMenuItem item, PlayerCommandId command) in _macMenuCommands)
            item.Gesture = shortcuts.TryGetValue(command, out string? gesture) ? MenuGesture(gesture) : null;
    }

    /// <summary>
    /// A menu key equivalent, only for shortcuts that use Command. AppKit runs
    /// key equivalents before the window sees the key, so a plain key (Space,
    /// M, arrows) in the menu would bypass the editor focus rules in
    /// <see cref="OnPlayerKeyDown"/>; those stay window shortcuts only.
    /// </summary>
    internal static KeyGesture? MenuGesture(string gesture)
    {
        if (!gesture.Split('+').SkipLast(1).Contains("Meta")) return null;
        string[] parts = gesture.Split('+');
        KeyModifiers modifiers = KeyModifiers.None;
        foreach (string modifier in parts.SkipLast(1))
            modifiers |= modifier switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                "Meta" => KeyModifiers.Meta,
                _ => KeyModifiers.None,
            };
        string keyName = parts[^1];
        Key? key = keyName switch
        {
            "," => Key.OemComma,
            "[" => Key.OemOpenBrackets,
            "]" => Key.OemCloseBrackets,
            "=" => Key.OemPlus,
            _ when keyName.Length == 1 && char.IsDigit(keyName[0]) => Key.D0 + (keyName[0] - '0'),
            _ => Enum.TryParse(keyName, out Key parsed) ? parsed : null,
        };
        return key is { } value ? new KeyGesture(value, modifiers) : null;
    }
}

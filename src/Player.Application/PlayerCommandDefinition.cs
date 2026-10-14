namespace Player.Application;

public sealed record PlayerCommandDefinition(
    PlayerCommandId Id,
    string DisplayName,
    string Category,
    string? DefaultShortcut = null,
    // A command that needs a value (a position, a track, an item) does
    // nothing from a key, so the shortcut editor leaves it out.
    bool TakesValue = false);

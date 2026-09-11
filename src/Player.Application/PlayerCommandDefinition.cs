namespace Player.Application;

public sealed record PlayerCommandDefinition(
    PlayerCommandId Id,
    string DisplayName,
    string Category,
    string? DefaultShortcut = null);

namespace Player.Application;

/// <summary>Persistable, deterministic gestures. This model stays independent of any UI toolkit.</summary>
public sealed record PlayerShortcut(PlayerCommandId Command, string Gesture);

public static class PlayerShortcutPolicy
{
    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;
        string[] parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (string part in parts)
        {
            if (part.Equals("Control", StringComparison.OrdinalIgnoreCase) || part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers.Add("Ctrl");
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers.Add("Shift");
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers.Add("Alt");
            else if (key is null) key = part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..];
            else return string.Empty;
        }
        return key is null ? string.Empty : string.Join('+', new[] { "Ctrl", "Alt", "Shift" }.Where(modifiers.Contains).Append(key));
    }

    public static bool TryValidate(IEnumerable<PlayerShortcut> shortcuts, out string? error)
    {
        var used = new Dictionary<string, PlayerCommandId>(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerShortcut shortcut in shortcuts)
        {
            string normalized = Normalize(shortcut.Gesture);
            if (normalized.Length == 0) { error = $"'{shortcut.Gesture}' is not a valid shortcut."; return false; }
            if (used.TryGetValue(normalized, out PlayerCommandId other) && other != shortcut.Command) { error = $"{normalized} is already assigned to {other}."; return false; }
            used[normalized] = shortcut.Command;
        }
        error = null; return true;
    }

    public static IReadOnlyList<PlayerShortcut> Defaults(IPlayerCommandRegistry registry) => registry.Commands
        .Where(x => !string.IsNullOrWhiteSpace(x.DefaultShortcut))
        .Select(x => new PlayerShortcut(x.Id, Normalize(x.DefaultShortcut))).ToArray();
}

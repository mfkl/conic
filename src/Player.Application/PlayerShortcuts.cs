namespace Player.Application;

/// <summary>Persistable, deterministic gestures. This model stays independent of any UI toolkit.</summary>
public sealed record PlayerShortcut(PlayerCommandId Command, string Gesture);
public sealed record EditablePlayerShortcut(PlayerCommandId Command, string Name, string Category, string DefaultGesture, string Gesture);

public sealed class PlayerShortcutEditor
{
    private readonly PlayerSettingsService _settings; private readonly IPlayerCommandRegistry _registry;
    public PlayerShortcutEditor(PlayerSettingsService settings, IPlayerCommandRegistry registry) => (_settings, _registry) = (settings, registry);
    public IReadOnlyList<EditablePlayerShortcut> Items(string? search = null) => _registry.Commands.Where(x => string.IsNullOrWhiteSpace(search) || (x.DisplayName + " " + x.Category).Contains(search, StringComparison.OrdinalIgnoreCase)).Select(x => new EditablePlayerShortcut(x.Id, x.DisplayName, x.Category, PlayerShortcutPolicy.Normalize(x.DefaultShortcut), PlayerShortcutPolicy.Effective(_registry, _settings.Current.ShortcutOverrides).GetValueOrDefault(x.Id, ""))).ToArray();
    public bool TrySave(IEnumerable<PlayerShortcut> changes, out string? error)
    {
        var effective = PlayerShortcutPolicy.Effective(_registry, _settings.Current.ShortcutOverrides).ToDictionary(x => x.Key, x => x.Value);
        foreach (var change in changes) { if (!_registry.Commands.Any(x => x.Id == change.Command)) { error = "Unknown command."; return false; } effective[change.Command] = PlayerShortcutPolicy.Normalize(change.Gesture); }
        if (!PlayerShortcutPolicy.TryValidate(effective.Select(x => new PlayerShortcut(x.Key, x.Value)), out error)) return false;
        var overrides = effective.Where(x => PlayerShortcutPolicy.Normalize(_registry.Commands.First(c => c.Id == x.Key).DefaultShortcut) != x.Value).ToDictionary(x => x.Key.ToString(), x => x.Value);
        _settings.Update(s => s with { ShortcutOverrides = overrides }); return true;
    }
    public void Reset(PlayerCommandId id) => _settings.Update(s => s with { ShortcutOverrides = s.ShortcutOverrides?.Where(x => x.Key != id.ToString()).ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string,string>() });
    public void ResetAll() => _settings.Update(s => s with { ShortcutOverrides = new Dictionary<string,string>() });
}

public static class PlayerShortcutPolicy
{
    public static IReadOnlyDictionary<PlayerCommandId, string> Effective(IPlayerCommandRegistry registry, IReadOnlyDictionary<string, string>? overrides)
    {
        var result = Defaults(registry).ToDictionary(x => x.Command, x => x.Gesture);
        if (overrides is null) return result;
        foreach (var pair in overrides)
            if (Enum.TryParse(pair.Key, out PlayerCommandId id) && result.ContainsKey(id) && Normalize(pair.Value) is { Length: > 0 } gesture) result[id] = gesture;
        return result;
    }

    public static bool TryDispatch(IPlayerCommandRegistry registry, IReadOnlyDictionary<PlayerCommandId, string> active, string gesture)
    {
        string normalized = Normalize(gesture);
        foreach (var pair in active)
            if (string.Equals(pair.Value, normalized, StringComparison.OrdinalIgnoreCase) && registry.CanExecute(pair.Key))
            { registry.Execute(pair.Key); return true; }
        return false;
    }
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

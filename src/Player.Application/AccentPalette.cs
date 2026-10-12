namespace Player.Application;

/// <summary>One of the design's accent colors: a display name and its
/// <c>Accent/Main</c> value as <c>#RRGGBB</c>.</summary>
public sealed record AccentColor(string Name, string Hex);

/// <summary>
/// The nine accent colors of the Figma design's Settings and onboarding
/// frames, in their order there. <see cref="PlayerSettings.Accent"/> stores
/// one of the <see cref="AccentColor.Hex"/> values.
/// </summary>
public static class AccentPalette
{
    public const string DefaultHex = "#D37918";

    public static IReadOnlyList<AccentColor> Colors { get; } = Array.AsReadOnly(new AccentColor[]
    {
        new("Orange", DefaultHex),
        new("Red", "#E15032"),
        new("Yellow", "#C39E24"),
        new("Green", "#1BA236"),
        new("Teal", "#1BAAD3"),
        new("Blue", "#1B5DD3"),
        new("Purple", "#8E43FD"),
        new("Pink", "#EF53B3"),
        new("Gray", "#AFAFAF"),
    });

    /// <summary>The palette's spelling of <paramref name="hex"/> (any case),
    /// or null when it is not one of the palette's colors.</summary>
    public static string? Find(string? hex) =>
        Colors.FirstOrDefault(color => string.Equals(color.Hex, hex?.Trim(), StringComparison.OrdinalIgnoreCase))?.Hex;
}

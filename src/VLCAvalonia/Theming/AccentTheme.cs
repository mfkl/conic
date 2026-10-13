using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Player.Application;

namespace VLCAvalonia.Theming;

/// <summary>The accent tokens of one theme (Styles/PlayerPalette.axaml).</summary>
internal sealed record AccentFamily(
    Color Main, Color Bright, Color Dim, Color Logo, Color BackgroundBright, Color BackgroundDark,
    Color Text, Color TextHover, Color Icon, Color OnAccent);

/// <summary>
/// Derives and applies the accent family for the Accent setting. The design
/// defines it for orange only (tokens.md: Accent/Main, Bright, Dim, Logo,
/// BG/Accent Bright, BG/Accent Dark, per theme); another swatch takes the
/// same steps in HSL: its own hue, the token's saturation scaled by the
/// swatch's saturation relative to orange, and the token's lightness moved by
/// the swatch's lightness difference (the BG tokens keep the token's
/// lightness). Text, icon and label colors are then moved until they reach
/// 4.5:1 (text, labels) or 3:1 (icons) on the theme's surfaces. The VLC
/// cone (Accent/Logo) is not part of the family: it stays the design's
/// orange in every accent.
/// </summary>
internal static class AccentTheme
{
    private static readonly Color Orange = Color.Parse(AccentPalette.DefaultHex);

    private static readonly Color[] DarkDesign = Parse("#D37918", "#FC9729", "#AB6314", "#D37918", "#371F06", "#1A1611");
    private static readonly Color[] LightDesign = Parse("#E8851A", "#FFA13B", "#965916", "#C2711A", "#D8BA9A", "#C6AB89");

    // Surfaces that show accent text and icons: BG/B1, BG/A3, BG/A4, BG/A1.
    private static readonly Color[] DarkSurfaces = Parse("#0E0E0E", "#1B1B1B", "#191919", "#222222");
    private static readonly Color[] LightSurfaces = Parse("#E7E7E7", "#D5D5D5", "#B3B3B3", "#EDEDED");
    private static readonly Color DarkLabel = Color.Parse("#161616");

    public static AccentFamily Derive(Color swatch, bool light)
    {
        Color[] design = light ? LightDesign : DarkDesign;
        Color[] tokens = swatch.ToUInt32() == Orange.ToUInt32()
            ? design
            : design.Select((token, index) => Shift(token, swatch, keepLightness: index >= 4)).ToArray();
        Color main = tokens[0], bright = tokens[1], dim = tokens[2], backgroundBright = tokens[4];
        Color[] surfaces = light ? LightSurfaces : DarkSurfaces;
        Color[] textSurfaces = [.. surfaces.Where((_, index) => index != 2), backgroundBright];
        // Dark: the accent itself, lighter if needed; Light: Accent/Dim, darker.
        Color text = Reach(light ? dim : main, textSurfaces, 4.5, lighter: !light);
        Color textHover = light ? Lightness(text, -0.06) : Reach(bright, textSurfaces, 4.5, lighter: true);
        Color icon = Reach(main, surfaces, 3, lighter: !light);
        Color onAccent = Contrast(DarkLabel, main) >= 4.5 || Contrast(DarkLabel, main) >= Contrast(Colors.White, main) ? DarkLabel : Colors.White;
        // The cone is VLC's mark, not an accent: always the design's orange for the theme.
        Color logo = design[3];
        return new AccentFamily(main, bright, dim, logo, backgroundBright, tokens[5], text, textHover, icon, onAccent);
    }

    /// <summary>Recolors the accent brushes of both theme dictionaries (so
    /// every alias and binding follows) and Fluent's accent.</summary>
    public static void Apply(Application application, string hex)
    {
        Color swatch = Color.TryParse(AccentPalette.Find(hex) ?? AccentPalette.DefaultHex, out Color parsed) ? parsed : Orange;
        foreach ((ThemeVariant variant, bool light) in new[] { (ThemeVariant.Dark, false), (ThemeVariant.Light, true) })
        {
            AccentFamily family = Derive(swatch, light);
            Set(application, variant, "Vlc.Accent.Main", family.Main);
            Set(application, variant, "Vlc.Accent.Bright", family.Bright);
            Set(application, variant, "Vlc.Accent.Dim", family.Dim);
            Set(application, variant, "Vlc.Accent.Logo", family.Logo);
            Set(application, variant, "Vlc.Accent.Text", family.Text);
            Set(application, variant, "Vlc.Accent.TextHover", family.TextHover);
            Set(application, variant, "Vlc.Accent.Icon", family.Icon);
            Set(application, variant, "Vlc.Fg.OnAccent", family.OnAccent);
            Set(application, variant, "Vlc.Bg.AccentBright", family.BackgroundBright);
            Set(application, variant, "Vlc.Bg.AccentDark", family.BackgroundDark);
            if (application.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent &&
                fluent.Palettes.TryGetValue(variant, out ColorPaletteResources? palette) && palette.Accent != family.Main)
                palette.Accent = family.Main;
        }
    }

    private static void Set(Application application, ThemeVariant variant, string key, Color color)
    {
        if (application.TryGetResource(key, variant, out object? value) && value is SolidColorBrush brush && brush.Color != color)
            brush.Color = color;
    }

    internal static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    // Moves the lightness in 1 % steps (from the original color, so rounding
    // does not drift the hue) until the color reaches the contrast on every
    // surface.
    private static Color Reach(Color color, Color[] surfaces, double contrast, bool lighter)
    {
        Color result = color;
        for (int step = 1; step <= 100 && surfaces.Any(surface => Contrast(result, surface) < contrast); step++)
            result = Lightness(color, (lighter ? 0.01 : -0.01) * step);
        return result;
    }

    private static Color Lightness(Color color, double delta)
    {
        HslColor hsl = color.ToHsl();
        return HslColor.ToRgb(hsl.H, hsl.S, Math.Clamp(hsl.L + delta, 0, 1));
    }

    private static Color Shift(Color token, Color swatch, bool keepLightness)
    {
        HslColor t = token.ToHsl(), s = swatch.ToHsl(), o = Orange.ToHsl();
        double saturation = o.S > 0 ? Math.Clamp(t.S * s.S / o.S, 0, 1) : 0;
        double lightness = keepLightness ? t.L : Math.Clamp(t.L + s.L - o.L, 0, 1);
        return HslColor.ToRgb(s.H, saturation, lightness);
    }

    private static Color[] Parse(params string[] colors) => colors.Select(Color.Parse).ToArray();
}

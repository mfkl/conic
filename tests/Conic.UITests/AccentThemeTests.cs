using System.Xml.Linq;
using Player.Application;
using Conic.Theming;
using Xunit;
using static Conic.UITests.PaletteTests;

namespace Conic.UITests;

/// <summary>The accent family derived for each of the design's accent colors.</summary>
public sealed class AccentThemeTests
{
    [Theory]
    [InlineData("Default", false)]
    [InlineData("Light", true)]
    public void DefaultAccentDerivesThePaletteValues(string theme, bool light)
    {
        Dictionary<string, XElement> resources = Resources(theme);
        AccentFamily family = AccentTheme.Derive(Avalonia.Media.Color.Parse(AccentPalette.DefaultHex), light);

        Assert.Equal(Color(resources, "Vlc.Accent.Main"), family.Main.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.Bright"), family.Bright.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.Dim"), family.Dim.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.Logo"), family.Logo.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Bg.AccentBright"), family.BackgroundBright.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Bg.AccentDark"), family.BackgroundDark.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.Text"), family.Text.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.TextHover"), family.TextHover.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Accent.Icon"), family.Icon.ToUInt32());
        Assert.Equal(Color(resources, "Vlc.Fg.OnAccent"), family.OnAccent.ToUInt32());
    }

    [Theory]
    [InlineData("Default", false)]
    [InlineData("Light", true)]
    public void EveryAccentKeepsItsHueAndContrast(string theme, bool light)
    {
        Dictionary<string, XElement> resources = Resources(theme);
        uint[] surfaces = new[] { "Vlc.Bg.A1", "Vlc.Bg.A3", "Vlc.Bg.B1" }.Select(key => Color(resources, key)).ToArray();
        foreach (AccentColor accent in AccentPalette.Colors)
        {
            Avalonia.Media.Color swatch = Avalonia.Media.Color.Parse(accent.Hex);
            AccentFamily family = AccentTheme.Derive(swatch, light);
            uint[] textSurfaces = [.. surfaces, family.BackgroundBright.ToUInt32()];
            Assert.All(textSurfaces, surface => Assert.True(Contrast(family.Text.ToUInt32(), surface) >= 4.5, $"{theme} {accent.Name} text"));
            Assert.All(textSurfaces, surface => Assert.True(Contrast(family.TextHover.ToUInt32(), surface) >= 4.5, $"{theme} {accent.Name} text hover"));
            Assert.All(surfaces, surface => Assert.True(Contrast(family.Icon.ToUInt32(), surface) >= 3, $"{theme} {accent.Name} icon"));
            Assert.True(Contrast(family.OnAccent.ToUInt32(), family.Main.ToUInt32()) >= 4.5, $"{theme} {accent.Name} label on the accent");
            if (swatch.ToHsl().S > 0.1)
                foreach (Avalonia.Media.Color token in new[] { family.Main, family.Bright, family.Dim, family.BackgroundBright, family.Text })
                    Assert.True(Math.Abs(token.ToHsl().H - swatch.ToHsl().H) < 4, $"{theme} {accent.Name} hue");
            // Ordered like the design's family: Bright above Main above Dim.
            Assert.True(family.Bright.ToHsl().L > family.Main.ToHsl().L && family.Main.ToHsl().L > family.Dim.ToHsl().L, $"{theme} {accent.Name} order");
        }
    }
}

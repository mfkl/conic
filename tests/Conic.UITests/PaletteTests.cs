using System.Globalization;
using System.Xml.Linq;
using Avalonia.Styling;
using Conic.Theming;
using Xunit;

namespace Conic.UITests;

/// <summary>Checks Styles/PlayerPalette.axaml: both theme dictionaries define
/// the same keys, aliases resolve in their own dictionary, and text keeps its
/// contrast in both themes.</summary>
public sealed class PaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightRepeatsEveryKeyOfTheDarkDictionary()
    {
        Dictionary<string, XElement> dark = Resources("Default"), light = Resources("Light");

        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void AliasesResolveInsideTheirOwnDictionary(string theme)
    {
        Dictionary<string, XElement> resources = Resources(theme);

        foreach (XElement alias in resources.Values.Where(element => element.Name.LocalName == "StaticResource"))
            Assert.True(resources.ContainsKey((string)alias.Attribute("ResourceKey")!), $"{theme}: {alias.Attribute(Xaml + "Key")}");
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    public void BodyAndAccentTextKeepTheirContrast(string theme)
    {
        Dictionary<string, XElement> resources = Resources(theme);
        string[] surfaces = ["Vlc.Bg.A1", "Vlc.Bg.A2", "Vlc.Bg.A3", "Vlc.Bg.A4", "Vlc.Bg.A5", "Vlc.Bg.B1", "Vlc.Bg.B2"];
        foreach (string text in new[] { "Vlc.Fg.1", "Vlc.Fg.2", "Vlc.Fg.A1", "Vlc.Fg.A2" })
            foreach (string surface in surfaces)
                Assert.True(Contrast(Color(resources, text), Color(resources, surface)) >= 4.5, $"{theme}: {text} on {surface}");
        foreach (string surface in new[] { "Vlc.Bg.A1", "Vlc.Bg.A3", "Vlc.Bg.B1", "Vlc.Bg.AccentBright" })
            Assert.True(Contrast(Color(resources, "Vlc.Accent.Text"), Color(resources, surface)) >= 4.5, $"{theme}: accent text on {surface}");
        Assert.True(Contrast(Color(resources, "Vlc.Fg.OnAccent"), Color(resources, "Vlc.Accent.Main")) >= 4.5, $"{theme}: label on the accent");
        // Video and the controls over it stay dark in both themes.
        Assert.Equal("Black", (string)resources["PlayerCanvas"].Attribute("Color")!);
        Assert.Equal("#CC000000", (string)resources["Vlc.OnMedia.Scrim"].Attribute("Color")!);
    }

    [Theory]
    [InlineData("Dark", "Dark")]
    [InlineData("Light", "Light")]
    [InlineData("System", "Default")]
    [InlineData("unknown", "Dark")]
    public void ThemeSettingPicksTheVariant(string theme, string variant) =>
        Assert.Equal(variant, AppTheme.VariantFor(theme).Key);

    internal static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint argb)
    {
        static double Channel(uint value)
        {
            double c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((argb >> 16) & 0xFF) + 0.7152 * Channel((argb >> 8) & 0xFF) + 0.0722 * Channel(argb & 0xFF);
    }

    internal static uint Color(Dictionary<string, XElement> resources, string key)
    {
        XElement element = resources[key];
        while (element.Name.LocalName == "StaticResource")
            element = resources[(string)element.Attribute("ResourceKey")!];
        string value = ((string)element.Attribute("Color")!).TrimStart('#');
        uint color = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return value.Length == 6 ? color | 0xFF000000 : color;
    }

    internal static Dictionary<string, XElement> Resources(string theme)
    {
        XDocument palette = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Conic", "Styles", "PlayerPalette.axaml"));
        XElement dictionary = palette.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(Xaml + "Key") == theme);
        return dictionary.Elements().Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "conic.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}

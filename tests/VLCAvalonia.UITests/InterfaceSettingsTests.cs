using System.Xml.Linq;
using Player.Application;
using Xunit;

namespace VLCAvalonia.UITests;

/// <summary>Settings › Interface: what Density, Reduce motion and Auto-hide
/// the controls change, and that their help text says so. Reduce motion's runtime switch
/// (Theming/ReducedMotion) is checked by --settings-apply-smoke.</summary>
public sealed class InterfaceSettingsTests
{
    [Theory]
    [InlineData(1280, false, 20, 20)]
    [InlineData(1280, true, 12, 12)]
    [InlineData(800, true, 12, 12)]
    [InlineData(480, false, 12, 12)]
    [InlineData(480, true, 12, 12)]
    public void CompactNarrowsTheControlBar(double width, bool compact, double spacing, double padding) =>
        Assert.Equal((spacing, padding), MainWindow.BarSpacing(PlayerLayoutPolicy.ForWidth(width), compact));

    [Fact]
    public void DensityHelpNamesWhatCompactChanges()
    {
        XDocument view = Load("Views", "SettingsView.axaml");

        Assert.Equal("Compact narrows the gaps in the control bar and tightens the rows in Settings", Row(view, "Density"));
        XElement dense = Style(view, ":is(UserControl).dense controls|SettingRow");
        Assert.Equal("40", Setter(dense, "MinHeight"));
        Assert.Equal("12", Setter(Style(view, ":is(UserControl).dense StackPanel#SettingsPageContent"), "Spacing"));
    }

    [Fact]
    public void ReduceMotionIsStyledAndItsHelpSaysWhatItDoes()
    {
        XDocument chrome = Load("Styles", "PlayerChrome.axaml");
        XDocument window = Load("MainWindow.axaml");

        Assert.Equal("none", Setter(Style(chrome, ":is(Window).reduced-motion :is(Button):pressed"), "RenderTransform"));
        Assert.Equal("False", Setter(Style(chrome, ":is(Window).reduced-motion ProgressBar.openingIndicator"), "IsVisible"));
        XElement opening = window.Descendants().Single(element => element.Name.LocalName == "ProgressBar" && (string?)element.Attribute("Classes") == "openingIndicator");
        Assert.Equal("True", (string?)opening.Attribute("IsIndeterminate"));
        Assert.Equal("Turns off animation (fades, button press scaling, the sliding loading bar)",
            Row(Load("Views", "SettingsView.axaml"), "Reduce motion"));
    }

    [Fact]
    public void AutoHideTheControlsDecidesWhetherFullscreenControlsHide()
    {
        // Phase 9d split this from Reduce motion (the settings migration
        // turns it off for users who had Reduce motion on).
        Assert.StartsWith("Hides the bars over fullscreen video", Row(Load("Views", "SettingsView.axaml"), "Auto-hide the controls"));
        ChromeVisibilityController controller = new();
        controller.SetFullscreen(true);
        controller.SetPlaybackState(isPlaying: true, autoHide: false);
        Assert.False(controller.HidesAutomatically);
        controller.SetPlaybackState(isPlaying: true, autoHide: true);
        Assert.True(controller.HidesAutomatically);
    }

    private static XDocument Load(params string[] path) =>
        XDocument.Load(Path.Combine([PaletteTests.RepositoryRoot(), "src", "VLCAvalonia", .. path]));

    private static string? Row(XDocument view, string label) => (string?)view.Descendants()
        .Single(element => element.Name.LocalName == "SettingRow" && (string?)element.Attribute("Label") == label)
        .Attribute("Help");

    private static XElement Style(XDocument document, string selector) => document.Descendants()
        .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute("Selector") == selector);

    private static string? Setter(XElement style, string property) => (string?)style.Elements()
        .Single(element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property)
        .Attribute("Value");
}

using Avalonia;
using Avalonia.Styling;

namespace VLCAvalonia.Theming;

/// <summary>
/// Applies the Settings appearance to the application: the Theme setting
/// picks the theme variant ("System" follows the OS; anything unknown is
/// Dark, the default), which selects the "Default" (dark) or "Light"
/// dictionary of Styles/PlayerPalette.axaml.
/// </summary>
internal static class AppTheme
{
    public static ThemeVariant VariantFor(string theme) => theme switch
    {
        "Light" => ThemeVariant.Light,
        "System" => ThemeVariant.Default,
        _ => ThemeVariant.Dark,
    };

    public static void Apply(Application application, string theme)
    {
        ThemeVariant variant = VariantFor(theme);
        if (application.RequestedThemeVariant != variant)
            application.RequestedThemeVariant = variant;
    }
}

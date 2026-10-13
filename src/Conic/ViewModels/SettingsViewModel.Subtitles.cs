using Avalonia.Media;
using Avalonia.Media.Immutable;
using Player.Application;

namespace Conic.ViewModels;

// Subtitles rows (phase 9d): tracks, loading, appearance, timing and the
// on-screen messages. VLC's text renderer draws the subtitles, so the
// appearance rows are its options for each new core
// (SubtitleEngineOptions); the size, color, outline and background also
// reach a playing core. The shortcut feedback is the app's own overlay.
internal sealed partial class SettingsViewModel
{
    private static readonly string[] SubtitleRowProperties =
    [
        nameof(SubtitlesEnabled), nameof(SubtitleEncoding), nameof(SubtitleAutoload), nameof(SubtitleMatching), nameof(SubtitleFont),
        nameof(SubtitleTextScale), nameof(SubtitleBold), nameof(SubtitleColor), nameof(SubtitleColorSwatch), nameof(SubtitleOpacity),
        nameof(SubtitleOutline), nameof(SubtitleOutlineColor), nameof(SubtitleOutlineColorSwatch), nameof(SubtitleShadow),
        nameof(SubtitleShadowColor), nameof(SubtitleShadowColorSwatch), nameof(SubtitleShadowDistance), nameof(SubtitleBackground),
        nameof(SubtitleBackgroundColor), nameof(SubtitleBackgroundColorSwatch), nameof(SubtitleBackgroundOpacity), nameof(SubtitleMargin),
        nameof(SubtitleDelay), nameof(ShortcutFeedback), nameof(ShortcutFeedbackSeconds), nameof(ShowFileNameOnStart), nameof(FileNameSeconds),
    ];

    // Tracks & Languages: the preferred language is Playback › Tracks' setting, shown on both pages.
    public bool SubtitlesEnabled { get => Current.SubtitlesEnabled; set => Update(s => s with { SubtitlesEnabled = value }); }
    public IReadOnlyList<SettingOption> SubtitleEncodings => SubtitleEngineOptions.Encodings;
    public SettingOption SubtitleEncoding { get => Find(SubtitleEncodings, Current.SubtitleEncoding); set { if (value is not null) Update(s => s with { SubtitleEncoding = value.Value }); } }

    // Loading Subtitles.
    public bool SubtitleAutoload { get => Current.SubtitleAutoload; set => Update(s => s with { SubtitleAutoload = value }); }
    public IReadOnlyList<SettingOption> SubtitleMatchings => SubtitleEngineOptions.Matchings;
    public SettingOption SubtitleMatching
    {
        get => Find(SubtitleMatchings, Current.SubtitleMatching.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set { if (value is not null) Update(s => s with { SubtitleMatching = int.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture) }); }
    }

    // Appearance. Colors are typed as #RRGGBB; anything else goes back to the value in effect.
    public string SubtitleFont { get => Current.SubtitleFont; set => Update(s => s with { SubtitleFont = value ?? string.Empty }); }
    public decimal SubtitleTextScale { get => Current.SubtitleTextScale; set => Update(s => s with { SubtitleTextScale = (int)value }); }
    public bool SubtitleBold { get => Current.SubtitleBold; set => Update(s => s with { SubtitleBold = value }); }
    public string SubtitleColor { get => Current.SubtitleColor; set => Update(s => s with { SubtitleColor = SubtitleEngineOptions.NormalizeColor(value, s.SubtitleColor) }); }
    public IBrush SubtitleColorSwatch => Swatch(Current.SubtitleColor);
    public double SubtitleOpacity { get => Current.SubtitleOpacity; set => UpdatePercent(Current.SubtitleOpacity, value, (s, v) => s with { SubtitleOpacity = v }); }
    public IReadOnlyList<SettingOption> SubtitleOutlines => SubtitleEngineOptions.Outlines;
    public SettingOption SubtitleOutline { get => Find(SubtitleOutlines, Current.SubtitleOutline); set { if (value is not null) Update(s => s with { SubtitleOutline = value.Value }); } }
    public string SubtitleOutlineColor { get => Current.SubtitleOutlineColor; set => Update(s => s with { SubtitleOutlineColor = SubtitleEngineOptions.NormalizeColor(value, s.SubtitleOutlineColor) }); }
    public IBrush SubtitleOutlineColorSwatch => Swatch(Current.SubtitleOutlineColor);
    public bool SubtitleShadow { get => Current.SubtitleShadow; set => Update(s => s with { SubtitleShadow = value }); }
    public string SubtitleShadowColor { get => Current.SubtitleShadowColor; set => Update(s => s with { SubtitleShadowColor = SubtitleEngineOptions.NormalizeColor(value, s.SubtitleShadowColor) }); }
    public IBrush SubtitleShadowColorSwatch => Swatch(Current.SubtitleShadowColor);
    public IReadOnlyList<SettingOption> SubtitleShadowDistances => SubtitleEngineOptions.ShadowDistances;
    public SettingOption SubtitleShadowDistance { get => Find(SubtitleShadowDistances, Current.SubtitleShadowDistance); set { if (value is not null) Update(s => s with { SubtitleShadowDistance = value.Value }); } }
    public bool SubtitleBackground { get => Current.SubtitleBackground; set => Update(s => s with { SubtitleBackground = value }); }
    public string SubtitleBackgroundColor { get => Current.SubtitleBackgroundColor; set => Update(s => s with { SubtitleBackgroundColor = SubtitleEngineOptions.NormalizeColor(value, s.SubtitleBackgroundColor) }); }
    public IBrush SubtitleBackgroundColorSwatch => Swatch(Current.SubtitleBackgroundColor);
    public double SubtitleBackgroundOpacity { get => Current.SubtitleBackgroundOpacity; set => UpdatePercent(Current.SubtitleBackgroundOpacity, value, (s, v) => s with { SubtitleBackgroundOpacity = v }); }
    public decimal SubtitleMargin { get => Current.SubtitleMargin; set => Update(s => s with { SubtitleMargin = (int)value }); }

    // Timing, in VLC's 100 ms steps.
    public decimal SubtitleDelay { get => Current.SubtitleDelay; set => Update(s => s with { SubtitleDelay = (int)value }); }

    // On-Screen Messages.
    public bool ShortcutFeedback { get => Current.ShortcutFeedback; set => Update(s => s with { ShortcutFeedback = value }); }
    public double ShortcutFeedbackSeconds
    {
        get => Current.ShortcutFeedbackSeconds;
        set { double rounded = Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2; if (double.IsFinite(rounded) && rounded != Current.ShortcutFeedbackSeconds) Update(s => s with { ShortcutFeedbackSeconds = rounded }); }
    }
    public bool ShowFileNameOnStart { get => Current.ShowFileNameOnStart; set => Update(s => s with { ShowFileNameOnStart = value }); }
    public double FileNameSeconds { get => Current.FileNameSeconds; set { int rounded = (int)Math.Round(value); if (rounded != Current.FileNameSeconds) Update(s => s with { FileNameSeconds = rounded }); } }

    private static IBrush Swatch(string color) => new ImmutableSolidColorBrush(Color.Parse(SubtitleEngineOptions.NormalizeColor(color, SubtitleEngineOptions.DefaultColor)));

    // Sliders write on every step; whole percents, equal values skipped.
    private void UpdatePercent(int current, double value, Func<PlayerSettings, int, PlayerSettings> update)
    {
        if (!double.IsFinite(value)) return;
        int rounded = (int)Math.Round(value);
        if (rounded != current) Update(s => update(s, rounded));
    }

    private static PlayerSettings ResetSubtitlesPage(PlayerSettings s, PlayerSettings d) => s with
    {
        SubtitlesEnabled = d.SubtitlesEnabled, SubtitleLanguage = d.SubtitleLanguage, SubtitleEncoding = d.SubtitleEncoding,
        SubtitleAutoload = d.SubtitleAutoload, SubtitleMatching = d.SubtitleMatching, SubtitleFont = d.SubtitleFont,
        SubtitleTextScale = d.SubtitleTextScale, SubtitleBold = d.SubtitleBold, SubtitleColor = d.SubtitleColor, SubtitleOpacity = d.SubtitleOpacity,
        SubtitleOutline = d.SubtitleOutline, SubtitleOutlineColor = d.SubtitleOutlineColor, SubtitleShadow = d.SubtitleShadow,
        SubtitleShadowColor = d.SubtitleShadowColor, SubtitleShadowDistance = d.SubtitleShadowDistance, SubtitleBackground = d.SubtitleBackground,
        SubtitleBackgroundColor = d.SubtitleBackgroundColor, SubtitleBackgroundOpacity = d.SubtitleBackgroundOpacity, SubtitleMargin = d.SubtitleMargin,
        SubtitleDelay = d.SubtitleDelay, ShortcutFeedback = d.ShortcutFeedback, ShortcutFeedbackSeconds = d.ShortcutFeedbackSeconds,
        ShowFileNameOnStart = d.ShowFileNameOnStart, FileNameSeconds = d.FileNameSeconds,
    };
}

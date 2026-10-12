using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VLCAvalonia.Controls;

/// <summary>
/// A Settings row (design: label 14 FG/A2 with an optional info icon, the
/// control right-aligned in a 320 wide column). <see cref="Help"/> becomes
/// the info icon's tooltip and <see cref="Badge"/> a small apply-mode tag
/// ("Next open", "Restart"). The template is in Views/SettingsView.axaml.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> HelpProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Help));

    public static readonly StyledProperty<string?> BadgeProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Badge));

    /// <summary>The control column's width: 320, or the switch's 68 for toggles.</summary>
    public static readonly StyledProperty<double> ControlWidthProperty =
        SettingRowPanel.ControlWidthProperty.AddOwner<SettingRow>();

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Help { get => GetValue(HelpProperty); set => SetValue(HelpProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public double ControlWidth { get => GetValue(ControlWidthProperty); set => SetValue(ControlWidthProperty, value); }
}

/// <summary>
/// Lays out a label and a control side by side, the control in a fixed
/// <see cref="ControlWidth"/> column on the right, or stacked (label above,
/// control below at most that wide) when the label would get less than
/// <see cref="MinLabelWidth"/>.
/// </summary>
public sealed class SettingRowPanel : Panel
{
    public static readonly StyledProperty<double> ControlWidthProperty =
        AvaloniaProperty.Register<SettingRowPanel, double>(nameof(ControlWidth), 320);

    public static readonly StyledProperty<double> MinLabelWidthProperty =
        AvaloniaProperty.Register<SettingRowPanel, double>(nameof(MinLabelWidth), 160);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SettingRowPanel, double>(nameof(Spacing), 16);

    static SettingRowPanel()
    {
        AffectsMeasure<SettingRowPanel>(ControlWidthProperty, MinLabelWidthProperty, SpacingProperty);
    }

    public double ControlWidth { get => GetValue(ControlWidthProperty); set => SetValue(ControlWidthProperty, value); }
    public double MinLabelWidth { get => GetValue(MinLabelWidthProperty); set => SetValue(MinLabelWidthProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    /// <summary>True when the last layout stacked the control under the label.</summary>
    public bool IsStacked { get; private set; }

    private bool SideBySide(double width) =>
        double.IsInfinity(width) || width >= ControlWidth + Spacing + MinLabelWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        Control? label = Children.Count > 0 ? Children[0] : null;
        Control? control = Children.Count > 1 ? Children[1] : null;
        double width = availableSize.Width;
        IsStacked = !SideBySide(width);
        if (!IsStacked)
        {
            double labelWidth = double.IsInfinity(width) ? double.PositiveInfinity : width - ControlWidth - Spacing;
            control?.Measure(new Size(ControlWidth, availableSize.Height));
            label?.Measure(new Size(labelWidth, availableSize.Height));
            double height = Math.Max(label?.DesiredSize.Height ?? 0, control?.DesiredSize.Height ?? 0);
            double desiredWidth = double.IsInfinity(width) ? (label?.DesiredSize.Width ?? 0) + Spacing + ControlWidth : width;
            return new Size(desiredWidth, height);
        }
        label?.Measure(new Size(width, availableSize.Height));
        control?.Measure(new Size(Math.Min(width, ControlWidth), availableSize.Height));
        double stackedHeight = (label?.DesiredSize.Height ?? 0) + Spacing / 2 + (control?.DesiredSize.Height ?? 0);
        return new Size(width, stackedHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Control? label = Children.Count > 0 ? Children[0] : null;
        Control? control = Children.Count > 1 ? Children[1] : null;
        if (!IsStacked)
        {
            double labelWidth = Math.Max(0, finalSize.Width - ControlWidth - Spacing);
            if (label is not null)
            {
                double h = label.DesiredSize.Height;
                label.Arrange(new Rect(0, (finalSize.Height - h) / 2, labelWidth, h));
            }
            if (control is not null)
            {
                double h = control.DesiredSize.Height;
                control.Arrange(new Rect(Math.Max(0, finalSize.Width - ControlWidth), (finalSize.Height - h) / 2, Math.Min(ControlWidth, finalSize.Width), h));
            }
            return finalSize;
        }
        double top = 0;
        if (label is not null)
        {
            label.Arrange(new Rect(0, 0, finalSize.Width, label.DesiredSize.Height));
            top = label.DesiredSize.Height + Spacing / 2;
        }
        control?.Arrange(new Rect(0, top, Math.Min(finalSize.Width, ControlWidth), control.DesiredSize.Height));
        return finalSize;
    }
}

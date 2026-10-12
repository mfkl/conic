using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace VLCAvalonia.ViewModels;

/// <summary>
/// A Settings category: its name, the design group it belongs to ("App",
/// "Media" or "System"), the design icon (<c>Icon.{Icon}</c>, and
/// <c>Icon.{Icon}Active</c> while selected when the set has one) and the
/// extra words search matches.
/// </summary>
internal sealed class SettingsPage(string name, string group, string icon, string keywords) : INotifyPropertyChanged
{
    private bool _showsGroupHeader;

    public string Name { get; } = name;
    public string Group { get; } = group;
    public string Icon { get; } = icon;
    public string Keywords { get; } = keywords;
    public string Title => $"{Name} Settings";

    /// <summary>True for the first visible page of its group.</summary>
    public bool ShowsGroupHeader
    {
        get => _showsGroupHeader;
        set
        {
            if (_showsGroupHeader == value) return;
            _showsGroupHeader = value;
            PropertyChanged?.Invoke(this, new(nameof(ShowsGroupHeader)));
        }
    }

    public bool Matches(string search) =>
        string.IsNullOrWhiteSpace(search) ||
        (Name + " " + Keywords).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One option of a single-choice setting shown as radio buttons (the Theme
/// segments, the accent swatches). Checking it applies the setting;
/// <see cref="Sync"/> mirrors the value in effect.
/// </summary>
internal sealed class SettingChoice(string value, string label, string automationName, Action<string> select, string? color = null) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Value { get; } = value;
    public string Label { get; } = label;
    public string AutomationName { get; } = automationName;
    /// <summary>The swatch color (<c>#RRGGBB</c>) of an accent choice.</summary>
    public string? Color { get; } = color;
    public IBrush? Swatch { get; } = color is null ? null : new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse(color));

    public bool IsSelected
    {
        get => _isSelected;
        // Radio buttons also write false to the one they uncheck; only a
        // newly checked choice changes the setting.
        set { if (value && !_isSelected) select(Value); }
    }

    internal void Sync(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

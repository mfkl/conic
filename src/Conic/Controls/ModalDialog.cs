using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Conic.Controls;

/// <summary>
/// The design's modal dialog shell (Settings 999:5047, Open Network Stream
/// 6216:13614): a dimmed backdrop over the window and a centered surface
/// (radius 14, shadow, at most 1048x865, clear of the 40 px title bar) with a
/// 50 px BG/B2 <see cref="HeaderedContentControl.Header"/>, the content and
/// an optional <see cref="Footer"/>. Below <see cref="CompactWidth"/> it fills
/// the window (pseudo-class <c>:compact</c>), and only then does the header
/// clear the macOS traffic lights. Pressing the backdrop raises
/// <see cref="CloseRequested"/>, except over the title bar band, which still
/// moves the window. The template is in Styles/PlayerChrome.axaml; the
/// surface color is <see cref="TemplatedControl.Background"/>. Escape and
/// focus restoration belong to the host.
/// </summary>
public class ModalDialog : HeaderedContentControl
{
    /// <summary>Below this width the dialog fills the window.</summary>
    public const double CompactWidth = 720;

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<ModalDialog, object?>(nameof(Footer));

    public static readonly StyledProperty<Thickness> HeaderPaddingProperty =
        AvaloniaProperty.Register<ModalDialog, Thickness>(nameof(HeaderPadding), new Thickness(9, 0));

    private Border? _header;
    private Border? _scrim;

    public event EventHandler? CloseRequested;

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>The header's padding in the centered dialog; the compact one adds the macOS caption inset.</summary>
    public Thickness HeaderPadding
    {
        get => GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    /// <summary>True when the dialog fills the window (narrower than <see cref="CompactWidth"/>).</summary>
    public bool IsCompact => Bounds.Width > 0 && Bounds.Width < CompactWidth;

    /// <summary>The dialog surface (without the backdrop), for layout checks.</summary>
    internal Control? DialogPart { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_scrim is not null) _scrim.PointerPressed -= OnScrimPointerPressed;
        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        _header = e.NameScope.Find<Border>("PART_Header");
        DialogPart = e.NameScope.Find<Control>("PART_Dialog");
        if (_scrim is not null) _scrim.PointerPressed += OnScrimPointerPressed;
        UpdateLayoutMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == HeaderPaddingProperty)
            UpdateLayoutMode();
        else if (change.Property == FooterProperty)
        {
            if (change.OldValue is ILogical oldFooter) LogicalChildren.Remove(oldFooter);
            if (change.NewValue is ILogical newFooter) LogicalChildren.Add(newFooter);
        }
    }

    private void UpdateLayoutMode()
    {
        bool compact = IsCompact;
        PseudoClasses.Set(":compact", compact);
        // The centered dialog stays below the title bar; filling the window,
        // its header reaches the macOS traffic lights.
        if (_header is not null)
            _header.Padding = compact ? PlatformChrome.ClearOfCaptionButtons(HeaderPadding) : HeaderPadding;
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.GetPosition(this).Y < PlatformChrome.TitleBarHeight && TopLevel.GetTopLevel(this) is Window { WindowState: not WindowState.FullScreen } window)
            window.BeginMoveDrag(e);
        else
            CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

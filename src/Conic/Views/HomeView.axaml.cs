using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Conic.Controls;
using Conic.ViewModels;

namespace Conic.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this width the sidebar is always the icon rail.</summary>
    private const double CompactWidth = 720;
    private const double SidebarWidth = 217;
    private const double RailWidth = 50;
    /// <summary>Rows of list items each Home section shows (grid sections show one row of cards).</summary>
    private const int ListSectionRows = 3;
    /// <summary>The mini player's width in the design (970:3091) and in its one-row compact form.</summary>
    private const double MiniPlayerWidth = 391;
    private const double CompactMiniPlayerWidth = 420;
    /// <summary>Below this height the mini player drops its preview, so it never covers most of the content.</summary>
    private const double CompactMiniPlayerHeight = 540;

    private Window? _window;
    // The user's choice from the title bar toggle; kept for this session only.
    private bool _sidebarCollapsed;
    private PlayerPresentationViewModel? _viewModel;

    public event EventHandler? MoreRequested;

    public HomeView()
    {
        InitializeComponent();
        HomeHeader.Padding = PlatformChrome.ClearOfCaptionButtons(HomeHeader.Padding);
        SizeChanged += (_, _) => ApplySidebarLayout();
        AttachedToVisualTree += (_, _) => _window = TopLevel.GetTopLevel(this) as Window;
        ContentPanel.SizeChanged += (_, _) => UpdateSectionCardLimit();
        MiniPlayer.SizeChanged += (_, _) => ReserveMiniPlayerSpace();
        MiniPlayer.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ReserveMiniPlayerSpace(); };
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = DataContext as PlayerPresentationViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateSectionCardLimit();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerPresentationViewModel.HomeListLayout)) UpdateSectionCardLimit();
    }

    // Each Home section shows what fits in one row of cards (or a few rows of
    // the list), computed with the same rule as the section's MediaGridPanel.
    private void UpdateSectionCardLimit()
    {
        if (_viewModel is null) return;
        double width = ContentPanel.Bounds.Width - ContentPanel.Padding.Left - ContentPanel.Padding.Right;
        if (width <= 0) return;
        _viewModel.HomeSectionCardLimit = _viewModel.HomeListLayout
            ? MediaGridPanel.ColumnCount(width, MediaGridPanel.RowMinWidth, MediaGridPanel.RowMaxWidth, MediaGridPanel.DefaultSpacing) * ListSectionRows
            : MediaGridPanel.ColumnCount(width, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, MediaGridPanel.DefaultSpacing);
    }

    private void ApplySidebarLayout()
    {
        bool compact = Bounds.Width < CompactWidth;
        bool rail = compact || _sidebarCollapsed;
        Classes.Set("compact", compact);
        Classes.Set("rail", rail);
        double sidebar = rail ? RailWidth : SidebarWidth;
        HomeColumns.ColumnDefinitions[0].Width = new GridLength(sidebar);
        bool compactPlayer = compact || Bounds.Height < CompactMiniPlayerHeight;
        MiniPlayer.Classes.Set("compact", compactPlayer);
        double room = Bounds.Width - sidebar - (compactPlayer ? 16 : 24);
        MiniPlayer.Width = Math.Max(0, Math.Min(compactPlayer ? CompactMiniPlayerWidth : MiniPlayerWidth, room));
        string label = _sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";
        ToolTip.SetTip(SidebarToggle, label);
        AutomationProperties.SetName(SidebarToggle, label);
    }

    // The mini player floats over the end of the content; the scrolled
    // content ends its height lower, so nothing stays hidden under it.
    private void ReserveMiniPlayerSpace() =>
        HomeScrollContent.Margin = new Thickness(0, 0, 0, MiniPlayer.IsVisible ? MiniPlayer.Bounds.Height : 0);

    private void OnMiniPlayerPreviewTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlayerPresentationViewModel viewModel && viewModel.ShowPlayerCommand.CanExecute(null))
            viewModel.ShowPlayerCommand.Execute(null);
        e.Handled = true;
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        ApplySidebarLayout();
    }

    private void OnMore(object? sender, RoutedEventArgs e) => MoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnGridLayout(object? sender, RoutedEventArgs e) => SetListLayout(false);

    private void OnListLayout(object? sender, RoutedEventArgs e) => SetListLayout(true);

    private void SetListLayout(bool list)
    {
        if (DataContext is PlayerPresentationViewModel viewModel && viewModel.HomeListLayout != list)
            viewModel.ToggleHomeLayoutCommand.Execute(null);
    }

    // The title bar doubles as the window caption on the home screen: its
    // background and labels drag the window; buttons keep their clicks.
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null) return;
        if (e.Source is not Border and not TextBlock) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && _window.WindowState != WindowState.FullScreen)
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }
        if (_window.WindowState != WindowState.FullScreen)
        {
            _window.BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    /// <summary>Focus for the title bar's "…" button, where the More menu and its dialogs return.</summary>
    public void FocusMore() => HomeMoreButton.Focus();

    public void FocusPrimary()
    {
        if (HomeOpenFile.IsEffectivelyVisible) HomeOpenFile.Focus(); else HomeSearchBox.Focus();
    }
}

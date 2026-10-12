using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VLCAvalonia.Controls;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this width the sidebar is always the icon rail.</summary>
    private const double CompactWidth = 720;
    private const double SidebarWidth = 217;
    private const double RailWidth = 50;

    private Window? _window;
    // The user's choice from the title bar toggle; kept for this session only.
    private bool _sidebarCollapsed;

    public event EventHandler? MoreRequested;

    public HomeView()
    {
        InitializeComponent();
        HomeHeader.Padding = PlatformChrome.ClearOfCaptionButtons(HomeHeader.Padding);
        SizeChanged += (_, _) => ApplySidebarLayout();
        AttachedToVisualTree += (_, _) => _window = TopLevel.GetTopLevel(this) as Window;
    }

    private void ApplySidebarLayout()
    {
        bool compact = Bounds.Width < CompactWidth;
        bool rail = compact || _sidebarCollapsed;
        Classes.Set("compact", compact);
        Classes.Set("rail", rail);
        HomeColumns.ColumnDefinitions[0].Width = new GridLength(rail ? RailWidth : SidebarWidth);
        string label = _sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";
        ToolTip.SetTip(SidebarToggle, label);
        AutomationProperties.SetName(SidebarToggle, label);
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

    public void FocusPrimary()
    {
        if (HomeOpenFile.IsEffectivelyVisible) HomeOpenFile.Focus(); else HomeSearchBox.Focus();
    }
}

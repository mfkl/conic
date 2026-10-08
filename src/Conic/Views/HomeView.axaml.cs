using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Conic.Controls;

namespace Conic.Views;

public partial class HomeView : UserControl
{
    private Window? _window;

    public event EventHandler? MoreRequested;

    public HomeView()
    {
        InitializeComponent();
        HomeHeader.Margin = PlatformChrome.ClearOfCaptionButtons(HomeHeader.Margin);
        SizeChanged += (_, _) =>
        {
            bool compact = Bounds.Width < 720;
            Classes.Set("compact", compact);
            HomeColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 56 : 176);
        };
        AttachedToVisualTree += (_, _) => _window = TopLevel.GetTopLevel(this) as Window;
    }

    private void OnMore(object? sender, RoutedEventArgs e) => MoreRequested?.Invoke(this, EventArgs.Empty);

    // The header doubles as the window caption on the home screen.
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null) return;
        if (e.Source is not Grid and not TextBlock) return;
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

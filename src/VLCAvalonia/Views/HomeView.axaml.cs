using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VLCAvalonia.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? MoreRequested;
    public HomeView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            bool compact = Bounds.Width < 720;
            Classes.Set("compact", compact);
            HomeColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 56 : 176);
        };
    }
    private void OnMore(object? sender, RoutedEventArgs e) => MoreRequested?.Invoke(this, EventArgs.Empty);
    public void FocusPrimary()
    {
        if (HomeOpenFile.IsEffectivelyVisible) HomeOpenFile.Focus(); else HomeSearchBox.Focus();
    }
}

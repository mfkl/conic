using Avalonia.Controls;
using Avalonia.Interactivity;
using Conic.ViewModels;

namespace Conic.Views;

public partial class VideoAdjustmentsView : UserControl
{
    public VideoAdjustmentsView() => InitializeComponent();
    private void OnReset(object? sender, RoutedEventArgs e) => (DataContext as VideoAdjustmentsViewModel)?.Reset();
}

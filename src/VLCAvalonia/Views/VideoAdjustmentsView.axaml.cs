using Avalonia.Controls;
using Avalonia.Interactivity;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia.Views;

public partial class VideoAdjustmentsView : UserControl
{
    public VideoAdjustmentsView() => InitializeComponent();
    private void OnReset(object? sender, RoutedEventArgs e) => (DataContext as VideoAdjustmentsViewModel)?.Reset();
}

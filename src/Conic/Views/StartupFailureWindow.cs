using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Conic.Interop;

namespace Conic.Views;

internal sealed class StartupFailureWindow : Window
{
    public StartupFailureWindow(PlaybackStartupFailure failure)
    {
        Title = "Conic — playback unavailable";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Playback could not start", FontSize = 22, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = failure.Message, TextWrapping = TextWrapping.Wrap },
                new Expander
                {
                    Header = "Details",
                    Content = new TextBox { Text = failure.Detail, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 },
                },
                close,
            },
        };
        Opened += (_, _) => close.Focus();
    }
}

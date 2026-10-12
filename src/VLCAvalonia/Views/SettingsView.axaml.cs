using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VLCAvalonia.Controls;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia.Views;

public partial class SettingsView : UserControl
{
    /// <summary>Below this width the dialog fills the window and a ComboBox replaces the category pane.</summary>
    private const double CompactWidth = 720;
    private static readonly Thickness HeaderPadding = new(8, 0, 14, 0);

    private PlayerPresentationViewModel? _player;
    public event EventHandler? CloseRequested;
    public SettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    internal void Initialize(SettingsViewModel model, PlayerPresentationViewModel player)
    {
        _player = player;
        DataContext = model;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.SelectedPage))
            {
                SettingsContent.Offset = default;
                ResetTransientState();
            }
        };
    }

    internal void ApplyResponsiveLayout()
    {
        bool compact = Bounds.Width < CompactWidth;
        Classes.Set("compact", compact);
        SettingsCategoryPane.IsVisible = !compact;
        SettingsPages.IsVisible = !compact;
        CompactSettingsPages.IsVisible = compact;
        SettingsColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 216);
        SettingsPageContent.Margin = compact ? new Thickness(16, 16, 16, 24) : new Thickness(24, 20, 24, 32);
        // The compact dialog fills the window, so its header reaches the
        // macOS traffic lights; the centered dialog stays below the title bar.
        SettingsHeader.Padding = compact ? PlatformChrome.ClearOfCaptionButtons(HeaderPadding) : HeaderPadding;
    }

    // The dimmed backdrop: pressing it closes Settings, except over the
    // title bar band, where it still moves the window.
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.GetPosition(this).Y < PlatformChrome.TitleBarHeight && TopLevel.GetTopLevel(this) is Window { WindowState: not WindowState.FullScreen } window)
            window.BeginMoveDrag(e);
        else
            CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    internal void UpdateHistory(PlayerPresentationViewModel player)
    {
        SettingsClearRecentButton.IsEnabled = player.HasRecentItems;
        SettingsClearResumeButton.IsEnabled = player.HasResumeItems;
        SettingsHistoryError.IsVisible = player.HasHistoryError;
        SettingsHistoryError.Text = player.HistoryError;
    }

    internal void ResetTransientState()
    {
        SettingsClearRecentPrompt.IsVisible = false;
        SettingsClearResumePrompt.IsVisible = false;
        SettingsClearRecentButton.IsVisible = true;
        SettingsClearResumeButton.IsVisible = true;
        (DataContext as SettingsViewModel)?.CancelResetCommand.Execute(null);
    }

    private void OnSettingsClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnSettingsClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearRecentButton.IsVisible = false;
        SettingsClearRecentPrompt.IsVisible = true;
        Dispatcher.UIThread.Post(() => SettingsCancelClearRecentButton.Focus());
    }

    private void OnSettingsCancelClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearRecentPrompt.IsVisible = false;
        SettingsClearRecentButton.IsVisible = true;
        SettingsClearRecentButton.Focus();
    }

    private void OnSettingsConfirmClearRecent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _player!.Registry.Execute(Player.Application.PlayerCommandId.ClearRecent);
        if (!_player!.HasRecentItems)
        {
            SettingsClearRecentPrompt.IsVisible = false;
            SettingsClearRecentButton.IsVisible = true;
        }
    }

    private void OnSettingsClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearResumeButton.IsVisible = false;
        SettingsClearResumePrompt.IsVisible = true;
        Dispatcher.UIThread.Post(() => SettingsCancelClearResumeButton.Focus());
    }

    private void OnSettingsCancelClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsClearResumePrompt.IsVisible = false;
        SettingsClearResumeButton.IsVisible = true;
        SettingsClearResumeButton.Focus();
    }

    private void OnSettingsConfirmClearResume(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _player!.Registry.Execute(Player.Application.PlayerCommandId.ClearResumeHistory);
        if (!_player!.HasResumeItems)
        {
            SettingsClearResumePrompt.IsVisible = false;
            SettingsClearResumeButton.IsVisible = true;
        }
    }

}

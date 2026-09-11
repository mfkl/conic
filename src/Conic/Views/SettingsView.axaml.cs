using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Conic.ViewModels;

namespace Conic.Views;

public partial class SettingsView : UserControl
{
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
        bool compact = Bounds.Width < 720;
        Classes.Set("compact", compact);
        SettingsPages.IsVisible = !compact;
        CompactSettingsPages.IsVisible = compact;
        SettingsColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 176);
        SettingsBody.Padding = new Thickness(compact ? 16 : 24);
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

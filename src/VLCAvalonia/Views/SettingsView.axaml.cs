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
        // Below ModalDialog.CompactWidth the dialog fills the window and a
        // ComboBox replaces the category pane.
        bool compact = Bounds.Width < ModalDialog.CompactWidth;
        Classes.Set("compact", compact);
        SettingsCategoryPane.IsVisible = !compact;
        SettingsPages.IsVisible = !compact;
        CompactSettingsPages.IsVisible = compact;
        SettingsColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 216);
        SettingsPageContent.Margin = compact ? new Thickness(16, 16, 16, 24) : new Thickness(24, 20, 24, 32);
    }

    /// <summary>The dialog surface inside the shared shell (the backdrop excluded).</summary>
    internal Control SettingsDialog => SettingsShell.DialogPart ?? SettingsShell;

    // The backdrop (ModalDialog) closes Settings.
    private void OnShellCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Keyboard Shortcuts (More menu, right-click menu): scrolls the Controls page's shortcut editor into view once it is laid out.</summary>
    internal void BringShortcutsIntoView() =>
        Dispatcher.UIThread.Post(() => ShortcutsSection.BringIntoView(), DispatcherPriority.Loaded);

    // Enter in a hotkey field saves that shortcut, like its Save button.
    private void OnShortcutFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (sender as Control)?.DataContext is not ShortcutRow row) return;
        if (row.SaveCommand.CanExecute(null)) row.SaveCommand.Execute(null);
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

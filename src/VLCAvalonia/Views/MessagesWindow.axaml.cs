using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Player.Application;
using VLCAvalonia.Services;
using VLCAvalonia.ViewModels;

namespace VLCAvalonia.Views;

/// <summary>VLC's Messages window, shown beside the player (Ctrl+Shift+M, Cmd+Shift+M on macOS).</summary>
internal sealed partial class MessagesWindow : Window
{
    private readonly MessagesViewModel _viewModel;
    // VLC logs from its own threads into EngineMessageLog; the list catches up four times a second.
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _notice = new() { Interval = TimeSpan.FromSeconds(4) };

    public MessagesWindow() : this(new MessagesViewModel(new EngineMessageLog())) { }

    public MessagesWindow(MessagesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _refresh.Tick += (_, _) => Refresh();
        _notice.Tick += (_, _) => { _notice.Stop(); NoticeText.Text = ""; };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MessagesViewModel.Messages)) { UpdateEmptyText(); FollowLatest(); }
        };
        FollowToggle.IsCheckedChanged += (_, _) => FollowLatest();
        if (OperatingSystem.IsMacOS()) InitializeMacMenu();
        Opened += (_, _) => { UpdateEmptyText(); FollowLatest(); _refresh.Start(); };
        Closed += (_, _) => { _refresh.Stop(); _notice.Stop(); };
    }

    internal MessagesViewModel ViewModel => _viewModel;

    // The menu bar shows the key window's menu: without one, Cmd+W and Cmd+M
    // would do nothing here.
    private void InitializeMacMenu()
    {
        NativeMenu window = [];
        window.Items.Add(new NativeMenuItem("Minimize") { Command = new Commands.RelayCommand(_ => WindowState = WindowState.Minimized), Gesture = new KeyGesture(Key.M, KeyModifiers.Meta) });
        window.Items.Add(new NativeMenuItem("Close Window") { Command = new Commands.RelayCommand(_ => Close()), Gesture = new KeyGesture(Key.W, KeyModifiers.Meta) });
        NativeMenu.SetMenu(this, [new NativeMenuItem("Window") { Menu = window }]);
    }

    internal void Refresh()
    {
        if (_viewModel.Refresh() == 0) return;
        UpdateEmptyText();
        FollowLatest();
    }

    private void UpdateEmptyText() => EmptyText.IsVisible = _viewModel.Messages.Count == 0;

    private void FollowLatest()
    {
        if (FollowToggle.IsChecked == true && _viewModel.Messages.Count > 0)
            MessageList.ScrollIntoView(_viewModel.Messages.Count - 1);
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _viewModel.Clear();
        UpdateEmptyText();
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<EngineMessage> selected = MessageList.SelectedItems?.OfType<EngineMessage>().OrderBy(message => message.Sequence).ToArray() ?? [];
        string? error = await new AvaloniaClipboardService(() => Clipboard).SetTextAsync(_viewModel.Text(selected.Count > 0 ? selected : null));
        ShowNotice(error is null ? $"Copied {(selected.Count > 0 ? selected.Count : _viewModel.Messages.Count):N0} messages" : $"Copy failed: {error}");
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save messages",
                SuggestedFileName = $"vlc-avalonia-messages-{DateTime.Now:yyyyMMdd-HHmmss}.log",
                DefaultExtension = "log",
                FileTypeChoices = [new FilePickerFileType("Log file") { Patterns = ["*.log", "*.txt"] }],
            });
            if (file is null) return;
            await using Stream stream = await file.OpenWriteAsync();
            await using StreamWriter writer = new(stream);
            await writer.WriteAsync(_viewModel.Text());
            ShowNotice($"Saved to {file.Name}");
        }
        catch (Exception exception)
        {
            ShowNotice($"Save failed: {exception.Message}");
        }
    }

    private void ShowNotice(string text)
    {
        NoticeText.Text = text;
        _notice.Stop();
        _notice.Start();
    }
}

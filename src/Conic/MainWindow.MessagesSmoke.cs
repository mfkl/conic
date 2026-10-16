#if SMOKE
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Player.Application;
using Player.Contracts;
using Conic.Interop;
using Conic.Views;

namespace Conic;

// --messages-smoke: the Messages window lists a real VLC session's log. The
// shortcut's command opens one window; Debug, picked in the window, keeps the
// core's debug messages from its first one (VLC's version banner); the filter,
// a lower verbosity and Clear act on the list.
public partial class MainWindow
{
    internal void EnableMessagesSmoke(IClassicDesktopStyleApplicationLifetime desktop)
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 720;
        Opened += async (_, _) =>
        {
            try
            {
                async Task WaitFor(Func<bool> condition, string message, double seconds = 15)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
                    while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
                    Require(condition(), message);
                }

                _presentation.Registry.Execute(PlayerCommandId.ShowMessages);
                MessagesWindow messages = _messagesWindow ?? throw new InvalidOperationException("Messages did not open");
                await WaitFor(() => messages.IsVisible, "the Messages window shows");
                _presentation.Registry.Execute(PlayerCommandId.ShowMessages);
                Require(ReferenceEquals(messages, _messagesWindow), "Messages again activates the same window");
                Require(messages.ViewModel.SelectedVerbosity.Level == 0, "verbosity starts at errors and information, as in VLC");

                messages.VerbositySelector.SelectedIndex = 2;
                Require(VlcLog.Messages.Verbosity == 2, "picking Debug in the window sets the log's verbosity");
                OpenTestMedia();
                await WaitFor(() => VideoSurface.Player?.State.Status == PlaybackStatus.Playing, "the fixture plays");
                bool Has(Func<EngineMessage, bool> match) => messages.ViewModel.Messages.Any(match);
                await WaitFor(() => Has(m => m.Type == EngineMessageType.Debug && m.Text.StartsWith("VLC media player - 3.0", StringComparison.Ordinal)),
                    "the core's first debug message (VLC's version banner) is listed");
                await WaitFor(() => Has(m => m.Module == "main" && m.Text.StartsWith("using video decoder module", StringComparison.Ordinal)),
                    "VLC's decoder choice is listed");
                await Task.Delay(400);
                Require(!messages.EmptyText.IsVisible && messages.MessageList.GetVisualDescendants().OfType<ListBoxItem>().Any(item => item.IsEffectivelyVisible),
                    "the list shows rows");
                Console.WriteLine($"[messages-smoke] ok: {messages.ViewModel.Summary} at Debug; decoder: {messages.ViewModel.Messages.First(m => m.Text.StartsWith("using video decoder module", StringComparison.Ordinal)).Text}");

                string directory = Path.Combine(RepositoryPaths.ArtifactsDirectory, "messages-review");
                Directory.CreateDirectory(directory);
                // The window's content; with --video-capture the script also takes
                // the screen, window chrome included.
                async Task Capture(string name)
                {
                    using (RenderTargetBitmap bitmap = new(new PixelSize((int)messages.ClientSize.Width, (int)messages.ClientSize.Height)))
                    {
                        bitmap.Render(messages);
                        bitmap.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
                    }
                    if (desktop.Args?.Contains("--video-capture") != true) return;
                    messages.Activate();
                    await Task.Delay(300);
                    Console.WriteLine($"[video-capture] {name}");
                    await Task.Delay(1500);
                }
                await Capture("debug");

                // The light theme's palette, then back to the app's theme.
                if (Avalonia.Application.Current is { } application)
                {
                    Avalonia.Styling.ThemeVariant theme = application.RequestedThemeVariant ?? Avalonia.Styling.ThemeVariant.Default;
                    application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                    await Task.Delay(300);
                    await Capture("light");
                    application.RequestedThemeVariant = theme;
                    await Task.Delay(200);
                }

                messages.FilterBox.Text = "decoder";
                await Task.Delay(300);
                int total = messages.ViewModel.Messages.Count;
                Require(total > 0 && messages.ViewModel.Messages.All(m => m.Line.Contains("decoder", StringComparison.OrdinalIgnoreCase)) && messages.ViewModel.Summary.Contains(" of ", StringComparison.Ordinal),
                    $"the filter keeps matching messages ({messages.ViewModel.Summary})");
                await Capture("filtered");
                messages.FilterBox.Text = "";

                messages.VerbositySelector.SelectedIndex = 0;
                long before = VlcLog.Messages.LastSequence;
                _presentation.Registry.Execute(PlayerCommandId.Seek, .5);
                await Task.Delay(1500);
                Require(VlcLog.Messages.Since(before).All(m => m.Type is EngineMessageType.Error or EngineMessageType.Info),
                    "back at errors and information, debug messages are no longer kept");

                long cleared = VlcLog.Messages.LastSequence;
                messages.ViewModel.Clear();
                Require(messages.ViewModel.Messages.Count == 0 && VlcLog.Messages.Since(0).Count == 0, "Clear empties the list and the log");
                // Playback goes on logging: only what comes after Clear is listed again.
                await Task.Delay(500);
                messages.Refresh();
                Require(messages.ViewModel.Messages.All(m => m.Sequence > cleared), "only messages logged after Clear come back");
                if (OperatingSystem.IsMacOS())
                {
                    NativeMenuItem close = NativeMenu.GetMenu(messages)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Window")
                        .Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Close Window");
                    Require(close.Gesture?.ToString() == "Cmd+W" || close.Gesture is { Key: Avalonia.Input.Key.W, KeyModifiers: Avalonia.Input.KeyModifiers.Meta }, "Close Window is Cmd+W in the Messages window's menu");
                    close.Command!.Execute(null);
                    await WaitFor(() => _messagesWindow is null && !messages.IsVisible, "Close Window closes Messages", 3);
                }
                Console.WriteLine("[messages-smoke] PASS one window, Debug from the core's first message, decoder choice, filter, lower verbosity, Clear and (macOS) Close Window");
                desktop.Shutdown(0);
            }
            catch (Exception e) { Console.Error.WriteLine($"[messages-smoke] FAIL {e}"); desktop.Shutdown(1); }
        };
    }
}
#endif // SMOKE

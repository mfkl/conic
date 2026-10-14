using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Player.Application;
using Conic.Interop;

namespace Conic;

// Jump to Time (Ctrl+T) and About (Shift+F1): short dialogs in the shell
// Open Network Stream uses. Each closes with Escape, Cancel or close, or the
// backdrop, and gives the focus back to where the player had it.
public partial class MainWindow
{
    private void InitializeDialogs()
    {
        _presentation.JumpToTimeRequested += (_, _) => ShowJumpPanel();
        _presentation.AboutRequested += (_, _) => ShowAboutPanel();
        _presentation.MinimalInterfaceRequested += (_, _) => ToggleMinimalInterface();
        _presentation.HelpRequested += (_, _) => _ = Launcher.LaunchUriAsync(HelpUri);
    }

    /// <summary>F1: Conic's README, the closest it has to VLC's help.</summary>
    internal static readonly Uri HelpUri = new("https://github.com/mfkl/conic#readme");

    /// <summary>Hide UI (the design's Ctrl+\, VLC's minimal interface, Ctrl+H): the bars overlay the video and hide while it plays.</summary>
    internal void ToggleMinimalInterface()
    {
        _chromeVisibility.SetMinimal(!_chromeVisibility.IsMinimal);
        ApplyVideoLayout();
        WakeChrome();
    }

    private bool IsDialogOpen => JumpPanel.IsVisible || AboutPanel.IsVisible;

    private void ShowJumpPanel()
    {
        ExitPictureInPicture();
        CloseRightDrawers();
        OpenMenu.IsVisible = false;
        JumpHint.Text = $"Type h:mm:ss, m:ss or seconds, up to {_presentation.JumpToTimeLengthText}.";
        JumpTextBox.Text = _presentation.JumpToTimeStartText;
        JumpError.IsVisible = false;
        JumpPanel.IsVisible = true;
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => { JumpTextBox.Focus(); JumpTextBox.SelectAll(); });
    }

    private void CloseJumpPanel()
    {
        JumpPanel.IsVisible = false;
        RestoreRouteFocus(OpenOptionsButton);
    }

    private void OnJumpDialogCloseRequested(object? sender, EventArgs e) => CloseJumpPanel();

    private void OnJumpCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseJumpPanel();

    private void OnJumpSubmit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_presentation.JumpToTime(JumpTextBox.Text, out string? error)) { CloseJumpPanel(); return; }
        JumpError.Text = error;
        JumpError.IsVisible = true;
        Dispatcher.UIThread.Post(() => { JumpTextBox.Focus(); JumpTextBox.SelectAll(); });
    }

    private void OnJumpTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnJumpSubmit(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseJumpPanel(); e.Handled = true; }
    }

    private void ShowAboutPanel()
    {
        ExitPictureInPicture();
        CloseRightDrawers();
        OpenMenu.IsVisible = false;
        AboutVersion.Text = "Version " + AppVersion();
        string? engine = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ? VlcCoreApi.TryGetVersion() : null;
        AboutEngine.Text = "Engine: VLC " + (engine ?? "3") + ", through libvlccore.";
        AboutPanel.IsVisible = true;
        SetChromeVisibility(true);
        Dispatcher.UIThread.Post(() => AboutCloseButton.Focus());
    }

    private void CloseAboutPanel()
    {
        AboutPanel.IsVisible = false;
        RestoreMenuFocus();
    }

    private void OnAboutDialogCloseRequested(object? sender, EventArgs e) => CloseAboutPanel();

    private void OnAboutClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseAboutPanel();

    /// <summary>Opens media the app was started with (or asked to open by the system), once the window shows.</summary>
    internal void OpenOnStart(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0) return;
        void Open() { _presentation.OpenCommandLine(sources); UpdateOpenErrorSurface(); }
        if (IsVisible) { Dispatcher.UIThread.Post(Open); return; }
        void OnOpened(object? sender, EventArgs e) { Opened -= OnOpened; Dispatcher.UIThread.Post(Open); }
        Opened += OnOpened;
    }

    /// <summary>A later start's media (Allow only one instance): it opens here, and the window comes forward.</summary>
    internal void OpenFromOtherStart(IReadOnlyList<string> sources)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        OpenOnStart(sources);
    }

    /// <summary>The version packages are built with (eng/package.ps1 -Version), without the commit suffix.</summary>
    private static string AppVersion()
    {
        string? version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrEmpty(version) ? "unknown" : version.Split('+')[0];
    }
}

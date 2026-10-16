using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Conic;

public partial class MainWindow
{
    private sealed record WindowPlacement(double Width, double Height, PixelPoint Position);
    private sealed record PictureInPictureRestore(WindowPlacement Placement, WindowState State,
        WindowDecorations Decorations, double MinWidth, double MinHeight, double MaxWidth, double MaxHeight, bool Topmost, bool Home);

    private WindowPlacement? _normalPlacement;
    private PictureInPictureRestore? _pictureInPictureRestore;
    private bool _changingWindowMode;

    private void InitializePictureInPicture()
    {
        _presentation.PictureInPictureRequested += (_, _) => TogglePictureInPicture();
        _presentation.MainLayoutRequested += (_, _) => ExitPictureInPicture();
        PositionChanged += (_, _) => RememberNormalPlacement();
        SizeChanged += (_, _) => { RememberNormalPlacement(); KeepPictureInPictureOnScreen(); };
        Opened += (_, _) => RememberNormalPlacement();
    }

    private void RememberNormalPlacement()
    {
        if (!_changingWindowMode && !_presentation.IsPictureInPicture && WindowState == WindowState.Normal && ClientSize.Width >= 480)
            _normalPlacement = new(ClientSize.Width, ClientSize.Height, Position);
    }

    private void TogglePictureInPicture()
    {
        if (_presentation.IsPictureInPicture) { ExitPictureInPicture(); return; }
        if (!_presentation.CanTogglePlayback) return;
        RememberNormalPlacement();
        bool fromFullscreen = WindowState == WindowState.FullScreen;
        _pictureInPictureRestore = new(_normalPlacement ?? new(1280, 720, Position), WindowState,
            WindowDecorations, MinWidth, MinHeight, MaxWidth, MaxHeight, Topmost, _presentation.IsHomeOpen);
        _changingWindowMode = true;
        try
        {
            // Keep VideoSurface attached: changing window presentation must not reopen the native engine.
            CloseRightDrawers();
            OpenMenu.IsVisible = false;
            UrlPanel.IsVisible = false;
            SettingsPanel.IsVisible = false;
            VideoArea.ContextMenu?.Close();
            _presentation.IsHomeOpen = false;
            _presentation.IsPictureInPicture = true;
            _presentation.IsFullscreen = false;
            _chromeVisibility.SetFullscreen(false);
            RequestWindowState(WindowState.Normal);
            WindowDecorations = WindowDecorations.BorderOnly;
            MinWidth = 320;
            MinHeight = 180;
            SetCurrentValue(TopmostProperty, true);
            PlacePictureInPicture();
            WakeChrome();
            Dispatcher.UIThread.Post(() => PictureInPictureChrome.Focus());
        }
        finally { _changingWindowMode = false; }
        if (fromFullscreen && (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())) PlacePictureInPictureAfterFullscreen();
    }

    // macOS leaves fullscreen with an animation that ends by putting back the
    // window's frame from before fullscreen, over the compact one (CI saw PiP
    // at 1920x970): place it again until the animation is over.
    private async void PlacePictureInPictureAfterFullscreen()
    {
        for (DateTime settled = DateTime.UtcNow.AddSeconds(3); DateTime.UtcNow < settled && _presentation.IsPictureInPicture;)
        {
            await Task.Delay(100);
            if (_presentation.IsPictureInPicture && (Math.Abs(ClientSize.Width - 480) >= 2 || Math.Abs(ClientSize.Height - 270) >= 2))
                PlacePictureInPicture();
        }
    }

    // macOS applies frame changes and fullscreen exits asynchronously; CI
    // once left the PiP window at 480x2645370 (and an absurd Metal drawable
    // that aborted the app) right after leaving fullscreen. A maximum size
    // makes AppKit clamp such frames, and a size beyond the screen is never
    // the user's, so put the window back.
    private void PlacePictureInPicture()
    {
        Width = 480;
        Height = 270;
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        PixelRect area = screen.WorkingArea;
        MaxWidth = area.Width / RenderScaling;
        MaxHeight = area.Height / RenderScaling;
        Position = new(Math.Max(area.X, area.Right - (int)(496 * RenderScaling)),
            Math.Max(area.Y, area.Bottom - (int)(286 * RenderScaling)));
    }

    private void KeepPictureInPictureOnScreen()
    {
        if (_changingWindowMode || !_presentation.IsPictureInPicture || Screens.ScreenFromWindow(this) is not { } screen) return;
        PixelRect area = screen.WorkingArea;
        if (ClientSize.Width * RenderScaling <= area.Width && ClientSize.Height * RenderScaling <= area.Height) return;
        Console.WriteLine($"[pip] window left at {ClientSize.Width}x{ClientSize.Height}; restoring the compact size");
        Dispatcher.UIThread.Post(() => { if (_presentation.IsPictureInPicture) PlacePictureInPicture(); });
    }

    private void ExitPictureInPicture(bool restoreRoute = true)
    {
        if (_pictureInPictureRestore is not { } saved) return;
        _pictureInPictureRestore = null;
        _changingWindowMode = true;
        try
        {
            RequestWindowState(WindowState.Normal);
            WindowDecorations = saved.Decorations;
            MinWidth = saved.MinWidth;
            MinHeight = saved.MinHeight;
            MaxWidth = saved.MaxWidth;
            MaxHeight = saved.MaxHeight;
            Width = saved.Placement.Width;
            Height = saved.Placement.Height;
            Position = saved.Placement.Position;
            SetCurrentValue(TopmostProperty, saved.Topmost);
            _presentation.IsPictureInPicture = false;
            if (restoreRoute) _presentation.IsHomeOpen = saved.Home;
            RequestWindowState(saved.State);
            _presentation.IsFullscreen = WindowState == WindowState.FullScreen;
            _chromeVisibility.SetFullscreen(_presentation.IsFullscreen);
            _normalPlacement = saved.Placement;
            WakeChrome();
            Dispatcher.UIThread.Post(() => RestoreRouteFocus(HomeButton));
        }
        finally { _changingWindowMode = false; }
    }

    private void OnPictureInPictureDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null)) return;
        if (e.ClickCount == 2) ExitPictureInPicture(); else BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnPictureInPictureClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}

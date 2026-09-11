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
        WindowDecorations Decorations, double MinWidth, double MinHeight, bool Topmost, bool Home);

    private WindowPlacement? _normalPlacement;
    private PictureInPictureRestore? _pictureInPictureRestore;
    private bool _changingWindowMode;

    private void InitializePictureInPicture()
    {
        _presentation.PictureInPictureRequested += (_, _) => TogglePictureInPicture();
        _presentation.MainLayoutRequested += (_, _) => ExitPictureInPicture();
        PositionChanged += (_, _) => RememberNormalPlacement();
        SizeChanged += (_, _) => RememberNormalPlacement();
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
        _pictureInPictureRestore = new(_normalPlacement ?? new(1280, 720, Position), WindowState,
            WindowDecorations, MinWidth, MinHeight, Topmost, _presentation.IsHomeOpen);
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
            WindowState = WindowState.Normal;
            WindowDecorations = WindowDecorations.BorderOnly;
            MinWidth = 320;
            MinHeight = 180;
            Width = 480;
            Height = 270;
            SetCurrentValue(TopmostProperty, true);
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                PixelRect area = screen.WorkingArea;
                Position = new(Math.Max(area.X, area.Right - (int)(496 * RenderScaling)),
                    Math.Max(area.Y, area.Bottom - (int)(286 * RenderScaling)));
            }
            WakeChrome();
            Dispatcher.UIThread.Post(() => PictureInPictureChrome.Focus());
        }
        finally { _changingWindowMode = false; }
    }

    private void ExitPictureInPicture(bool restoreRoute = true)
    {
        if (_pictureInPictureRestore is not { } saved) return;
        _pictureInPictureRestore = null;
        _changingWindowMode = true;
        try
        {
            WindowState = WindowState.Normal;
            WindowDecorations = saved.Decorations;
            MinWidth = saved.MinWidth;
            MinHeight = saved.MinHeight;
            Width = saved.Placement.Width;
            Height = saved.Placement.Height;
            Position = saved.Placement.Position;
            SetCurrentValue(TopmostProperty, saved.Topmost);
            _presentation.IsPictureInPicture = false;
            if (restoreRoute) _presentation.IsHomeOpen = saved.Home;
            WindowState = saved.State;
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

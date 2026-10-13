using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Conic.Services;

namespace Conic;

// Restores the window at the size, position and state of the previous
// session instead of always opening maximized, while Settings › Interface ›
// Remember window size and position is on (it applies at the next start).
public partial class MainWindow
{
    private const double MinimumRestoredSize = 320;

    private void RestoreWindowGeometry()
    {
        if (!_settings.Current.RememberWindowGeometry) return;
        WindowGeometry? geometry = _windowGeometry.Load();
        if (geometry is null || !IsValidSize(geometry.Width, geometry.Height)) return;
        Width = geometry.Width;
        Height = geometry.Height;
        if (geometry.State == nameof(WindowState.Maximized))
        {
            WindowState = WindowState.Maximized;
            return;
        }
        if (geometry.X is int x && geometry.Y is int y && IsOnScreen(x, y, geometry.Width, geometry.Height))
            Position = new PixelPoint(x, y);
    }

    private void SaveWindowGeometry()
    {
        if (!_settings.Current.RememberWindowGeometry) return;
        WindowState state = WindowState == WindowState.FullScreen ? _preFullscreenState : WindowState;
        PixelPoint position = state == WindowState.Normal ? Position : default;
        _windowGeometry.Save(new WindowGeometry(
            Math.Max(Width, MinimumRestoredSize),
            Math.Max(Height, MinimumRestoredSize),
            state == WindowState.Normal ? position.X : null,
            state == WindowState.Normal ? position.Y : null,
            state.ToString()));
    }

    private bool IsOnScreen(int x, int y, double width, double height)
    {
        PixelRect window = new(new PixelPoint(x, y),
            new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(height)));
        PixelPoint center = new(
            window.X + window.Width / 2,
            window.Y + window.Height / 2);
        return Screens.All.Any(screen => screen.Bounds.Contains(center));
    }

    private static bool IsValidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) &&
        width >= MinimumRestoredSize && height >= MinimumRestoredSize &&
        width < 10000 && height < 10000;
}

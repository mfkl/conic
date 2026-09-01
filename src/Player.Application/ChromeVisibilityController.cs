namespace Player.Application;

/// <summary>Pure fullscreen chrome visibility policy, driven by UI interaction state.</summary>
public sealed class ChromeVisibilityController
{
    public bool IsFullscreen { get; private set; }
    public bool IsVisible { get; private set; } = true;

    public void SetFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        IsVisible = true;
    }

    public void Wake() => IsVisible = true;

    public bool CanHide(bool seekDragging, bool menuOpen, bool chromePointerOver, bool chromeFocused) =>
        IsFullscreen && !seekDragging && !menuOpen && !chromePointerOver && !chromeFocused;

    public bool TryHide(bool seekDragging, bool menuOpen, bool chromePointerOver, bool chromeFocused)
    {
        if (!CanHide(seekDragging, menuOpen, chromePointerOver, chromeFocused)) return false;
        IsVisible = false;
        return true;
    }
}

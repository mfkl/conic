namespace Player.Application;

/// <summary>
/// Pure playback chrome visibility policy, driven by UI interaction state.
/// In a normal window the title bar and control bar are docked around the
/// video and never hide. They only overlay the video, and so auto-hide, in
/// fullscreen and picture in picture, unless Settings › Interface › Auto-hide
/// the controls is off.
/// </summary>
public sealed class ChromeVisibilityController
{
    public bool IsFullscreen { get; private set; }
    public bool IsPictureInPicture { get; private set; }
    /// <summary>Hide UI (VLC's minimal interface): the bars overlay the video in a window too.</summary>
    public bool IsMinimal { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool AutoHide { get; private set; } = true;
    public bool IsVisible { get; private set; } = true;

    /// <summary>The chrome is drawn over the video instead of beside it.</summary>
    public bool OverlaysVideo => IsFullscreen || IsPictureInPicture || IsMinimal;

    /// <summary>The chrome hides after inactivity when nothing holds it.</summary>
    public bool HidesAutomatically => OverlaysVideo && IsPlaying && AutoHide;

    public void SetFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        IsVisible = true;
    }

    public void SetPictureInPicture(bool pictureInPicture)
    {
        IsPictureInPicture = pictureInPicture;
        IsVisible = true;
    }

    public void SetMinimal(bool minimal)
    {
        IsMinimal = minimal;
        IsVisible = true;
    }

    public void Wake() => IsVisible = true;

    public void SetPlaybackState(bool isPlaying, bool autoHide)
    {
        IsPlaying = isPlaying;
        AutoHide = autoHide;
        if (!HidesAutomatically) IsVisible = true;
    }

    public bool CanHide(bool seekDragging, bool menuOpen, bool chromePointerOver, bool chromeFocused) =>
        HidesAutomatically && !seekDragging && !menuOpen && !chromePointerOver && !chromeFocused;

    public bool TryHide(bool seekDragging, bool menuOpen, bool chromePointerOver, bool chromeFocused)
    {
        if (!CanHide(seekDragging, menuOpen, chromePointerOver, chromeFocused)) return false;
        IsVisible = false;
        return true;
    }
}

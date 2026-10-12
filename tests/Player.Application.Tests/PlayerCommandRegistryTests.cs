using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerCommandRegistryTests
{
    [Fact]
    public void RegisteredCommandPublishesMetadataAndExecutesItsSingleHandler()
    {
        PlayerCommandRegistry registry = new();
        int executions = 0;
        registry.Register(new(PlayerCommandId.Stop, "Stop", "Playback"), _ => executions++);

        registry.Execute(PlayerCommandId.Stop);

        Assert.Equal(1, executions);
        PlayerCommandDefinition command = Assert.Single(registry.Commands);
        Assert.Equal("Playback", command.Category);
    }

    [Fact]
    public void DisabledCommandDoesNotExecute()
    {
        PlayerCommandRegistry registry = new();
        bool executed = false;
        registry.Register(new(PlayerCommandId.Seek, "Seek", "Playback"), _ => executed = true, _ => false);

        registry.Execute(PlayerCommandId.Seek, .5);

        Assert.False(executed);
        Assert.False(registry.CanExecute(PlayerCommandId.Seek, .5));
    }

    [Fact]
    public void DuplicateAndUnknownCommandsAreRejected()
    {
        PlayerCommandRegistry registry = new();
        PlayerCommandDefinition definition = new(PlayerCommandId.OpenMedia, "Open media", "Media");
        registry.Register(definition, _ => { });

        Assert.Throws<InvalidOperationException>(() => registry.Register(definition, _ => { }));
        Assert.Throws<InvalidOperationException>(() => registry.Execute(PlayerCommandId.Stop));
    }
}

public sealed class ChromeVisibilityControllerTests
{
    [Fact]
    public void FullscreenChromeNeverHidesDuringAnInteraction()
    {
        ChromeVisibilityController controller = new();
        controller.SetFullscreen(true);
        controller.SetPlaybackState(isPlaying: true, reduceMotion: false);

        Assert.False(controller.TryHide(seekDragging: true, menuOpen: false, chromePointerOver: false, chromeFocused: false));
        Assert.True(controller.IsVisible);
        Assert.False(controller.TryHide(false, true, false, false));
        Assert.False(controller.TryHide(false, false, true, false));
        Assert.False(controller.TryHide(false, false, false, true));
        Assert.True(controller.TryHide(false, false, false, false));
        controller.Wake();
        Assert.True(controller.IsVisible);
    }

    // Windowed, the title bar and control bar are docked around the video
    // (phase 4a of the Figma redesign); they used to auto-hide like fullscreen.
    [Fact]
    public void WindowedDockedChromeNeverHides()
    {
        ChromeVisibilityController controller = new();
        controller.SetPlaybackState(isPlaying: false, reduceMotion: false);
        Assert.False(controller.TryHide(false, false, false, false));
        controller.SetPlaybackState(isPlaying: true, reduceMotion: false);
        Assert.False(controller.OverlaysVideo);
        Assert.False(controller.HidesAutomatically);
        Assert.False(controller.TryHide(false, false, false, false));
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public void FullscreenChromeOnlyHidesDuringActivePlayback()
    {
        ChromeVisibilityController controller = new();
        controller.SetFullscreen(true);
        controller.SetPlaybackState(isPlaying: false, reduceMotion: false);
        Assert.False(controller.TryHide(false, false, false, false));
        controller.SetPlaybackState(isPlaying: true, reduceMotion: false);
        Assert.True(controller.HidesAutomatically);
        Assert.True(controller.TryHide(false, false, false, false));
        Assert.False(controller.IsVisible);
        controller.SetPlaybackState(isPlaying: false, reduceMotion: false);
        Assert.True(controller.IsVisible);
        controller.SetPlaybackState(isPlaying: true, reduceMotion: true);
        Assert.False(controller.TryHide(false, false, false, false));
    }

    [Fact]
    public void LeavingFullscreenShowsTheDockedChromeAgain()
    {
        ChromeVisibilityController controller = new();
        controller.SetFullscreen(true);
        controller.SetPlaybackState(isPlaying: true, reduceMotion: false);
        Assert.True(controller.TryHide(false, false, false, false));

        controller.SetFullscreen(false);

        Assert.True(controller.IsVisible);
        Assert.False(controller.TryHide(false, false, false, false));
    }

    [Fact]
    public void PictureInPictureChromeOverlaysAndHidesLikeFullscreen()
    {
        ChromeVisibilityController controller = new();
        controller.SetPlaybackState(isPlaying: true, reduceMotion: false);
        controller.SetPictureInPicture(true);
        Assert.True(controller.OverlaysVideo);
        Assert.True(controller.TryHide(false, false, false, false));
        controller.SetPictureInPicture(false);
        Assert.True(controller.IsVisible);
        Assert.False(controller.TryHide(false, false, false, false));
    }
}

public sealed class PlayerLayoutPolicyTests
{
    [Theory]
    [InlineData(1280d, 1d, false)]
    [InlineData(1280d, 1.25d, false)]
    [InlineData(1280d, 1.5d, true)]
    [InlineData(1280d, 2d, true)]
    [InlineData(480d, 1d, true)]
    [InlineData(480d, 1.5d, true)]
    public void DeviceIndependentWidthsSelectASafeLayoutAtCommonDisplayScales(double physicalWidth, double scale, bool compact)
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(physicalWidth / scale);
        Assert.Equal(compact, layout.IsCompact);
        Assert.Equal(!compact, layout.ShowTimeLabels);
        Assert.True(layout.Spacing > 0);
        Assert.True(layout.Padding > 0);
    }

    // The bar is one row since phase 4a: narrower windows drop secondary
    // controls instead of wrapping the volume onto a second row.
    [Theory]
    [InlineData(1920, false, true, true, true, 20d, 20d)]
    [InlineData(1280, false, true, true, true, 20d, 20d)]
    [InlineData(900, false, true, true, true, 20d, 20d)]
    [InlineData(899, true, false, true, true, 16d, 16d)]
    [InlineData(720, true, false, true, true, 16d, 16d)]
    [InlineData(640, true, false, true, true, 16d, 16d)]
    [InlineData(639, true, false, false, false, 12d, 12d)]
    [InlineData(480, true, false, false, false, 12d, 12d)]
    public void BarDropsSecondaryControlsAsTheWindowNarrows(double width, bool compact, bool timeLabels, bool volumeSlider, bool trackButtons, double spacing, double padding)
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(width);
        Assert.Equal((compact, timeLabels, volumeSlider, trackButtons, spacing, padding),
            (layout.IsCompact, layout.ShowTimeLabels, layout.ShowVolumeSlider, layout.ShowTrackButtons, layout.Spacing, layout.Padding));
    }
}

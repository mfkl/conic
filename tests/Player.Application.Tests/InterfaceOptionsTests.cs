using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class InterfaceOptionsTests
{
    [Fact]
    public void TheWheelChangesTheVolumeAndTheHorizontalWheelSeeks()
    {
        PlayerSettings settings = PlayerSettings.Default with { VolumeStep = 5, VeryShortSkipSeconds = 4 };

        Assert.Equal(new WheelStep(WheelTarget.Volume, 5), InterfaceOptions.Wheel(settings, 0, 1));
        Assert.Equal(new WheelStep(WheelTarget.Volume, -10), InterfaceOptions.Wheel(settings, .2, -2));
        Assert.Equal(new WheelStep(WheelTarget.Seek, 4), InterfaceOptions.Wheel(settings, 1, 0));
        // A burst counts as at most three notches; nothing moves without a delta.
        Assert.Equal(new WheelStep(WheelTarget.Volume, 15), InterfaceOptions.Wheel(settings, 0, 12));
        Assert.Equal(default, InterfaceOptions.Wheel(settings, 0, 0));
        Assert.Equal(default, InterfaceOptions.Wheel(settings, double.NaN, 1));
    }

    [Fact]
    public void ActionsCanBeSwappedTurnedOffOrReversed()
    {
        PlayerSettings settings = PlayerSettings.Default with { WheelAction = "Seek", HorizontalWheelAction = "None", ReverseWheel = true };

        Assert.Equal(new WheelStep(WheelTarget.Seek, -3), InterfaceOptions.Wheel(settings, 0, 1));
        Assert.Equal(default, InterfaceOptions.Wheel(settings, 1, 0));
        Assert.Equal(new WheelStep(WheelTarget.Volume, -2), InterfaceOptions.Wheel(PlayerSettings.Default with { ReverseWheel = true }, 0, 1));
    }

    [Fact]
    public void DefaultsFollowTheDesignAndVlc()
    {
        PlayerSettings d = PlayerSettings.Default;

        // VLC's jump sizes (very short, short, medium, long).
        Assert.Equal((3, 10, 10, 60, 300), (d.VeryShortSkipSeconds, d.SkipBackwardSeconds, d.SkipForwardSeconds, d.MediumSkipSeconds, d.LongSkipSeconds));
        Assert.Equal(("None", "Fullscreen", "Volume", "Seek", false), (d.ClickAction, d.DoubleClickAction, d.WheelAction, d.HorizontalWheelAction, d.ReverseWheel));
        Assert.Equal((true, false, true, true, true), (d.RememberWindowGeometry, d.KeepOnTop, d.ShowFileNameInTitle, d.ShowControlsInFullscreen, d.AutoHideControls));
        Assert.Equal((false, "Subfolders", false), (d.EnqueueOnOpen, d.FolderOpening, d.ShowHiddenFiles));
        Assert.Equal(d, InterfaceOptions.Normalize(d));
    }
}

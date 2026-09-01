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

        Assert.False(controller.TryHide(seekDragging: true, menuOpen: false, chromePointerOver: false, chromeFocused: false));
        Assert.True(controller.IsVisible);
        Assert.False(controller.TryHide(false, false, true, false));
        Assert.True(controller.TryHide(false, false, false, false));
        controller.Wake();
        Assert.True(controller.IsVisible);
    }
}

public sealed class PlayerLayoutPolicyTests
{
    [Theory]
    [InlineData(1280, false)]
    [InlineData(900, false)]
    [InlineData(899, true)]
    [InlineData(480, true)]
    public void CompactBreakPointKeepsTheTimelineAndVolumeLaneAvailable(double width, bool compact)
    {
        PlayerLayoutResult layout = PlayerLayoutPolicy.ForWidth(width);
        Assert.Equal(compact, layout.IsCompact);
        if (compact)
            Assert.Equal((2, 0, 3, 2d), (layout.RightRow, layout.RightColumn, layout.RightColumnSpan, layout.Spacing));
        else
            Assert.Equal((1, 2, 1, 8d), (layout.RightRow, layout.RightColumn, layout.RightColumnSpan, layout.Spacing));
    }
}

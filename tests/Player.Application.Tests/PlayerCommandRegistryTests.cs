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

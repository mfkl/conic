using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class EngineMessageLogTests
{
    [Theory]
    [InlineData(0, EngineMessageType.Info, true)]
    [InlineData(0, EngineMessageType.Error, true)]
    [InlineData(0, EngineMessageType.Warning, false)]
    [InlineData(0, EngineMessageType.Debug, false)]
    [InlineData(1, EngineMessageType.Warning, true)]
    [InlineData(1, EngineMessageType.Debug, false)]
    [InlineData(2, EngineMessageType.Debug, true)]
    public void VerbosityFollowsVlc(int verbosity, EngineMessageType type, bool shown)
    {
        EngineMessageLog log = new() { Verbosity = verbosity };
        Assert.Equal(shown, log.Accepts(type));
    }

    [Fact]
    public void VerbosityStaysInVlcsRange()
    {
        EngineMessageLog log = new() { Verbosity = 7 };
        Assert.Equal(2, log.Verbosity);
        log.Verbosity = -3;
        Assert.Equal(0, log.Verbosity);
    }

    [Fact]
    public void KeepsTheNewestMessagesUpToItsCapacity()
    {
        EngineMessageLog log = new(capacity: 3);
        for (int index = 1; index <= 5; index++) log.Add(EngineMessageType.Info, "main", "input", $"message {index}");

        Assert.Equal(["message 3", "message 4", "message 5"], log.Since(0).Select(message => message.Text));
        Assert.Equal(["message 5"], log.Since(4).Select(message => message.Text));
        Assert.Equal(5, log.LastSequence);
    }

    [Fact]
    public void ClearKeepsCountingSoReadersSeeOnlyNewMessages()
    {
        EngineMessageLog log = new();
        log.Add(EngineMessageType.Error, "main", "input", "before");
        log.Clear();
        EngineMessage after = log.Add(EngineMessageType.Error, "main", "input", "after");

        Assert.Equal(2, after.Sequence);
        Assert.Equal([after], log.Since(0));
    }

    [Fact]
    public void LinesReadLikeVlcsMessagesWindow()
    {
        EngineMessage info = new(1, new DateTime(2026, 10, 13, 9, 5, 7, 42), EngineMessageType.Info, "main", "playlist", "playlist is empty");
        EngineMessage debug = info with { Type = EngineMessageType.Debug, Module = "videotoolbox", ObjectType = "decoder", Text = "Using Video Toolbox to decode 'h264'" };

        Assert.Equal("main playlist: playlist is empty", info.Line);
        Assert.Equal("videotoolbox decoder debug: Using Video Toolbox to decode 'h264'", debug.Line);
        Assert.Equal("09:05:07.042 main playlist: playlist is empty" + Environment.NewLine, EngineMessageLog.Format([info]));
        Assert.True(debug.Matches("VIDEO TOOLBOX".Replace(" ", "")));
        Assert.True(debug.Matches("decoder debug"));
        Assert.False(debug.Matches("avcodec"));
        Assert.True(debug.Matches("  "));
    }
}

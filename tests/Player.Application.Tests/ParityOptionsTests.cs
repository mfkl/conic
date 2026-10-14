using Player.Application;
using Xunit;

namespace Player.Application.Tests;

/// <summary>Jump to Time's times, VLC's fine speed steps and command-line media.</summary>
public sealed class ParityOptionsTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03", 3723)]
    [InlineData(" 2:05.5 ", 125.5)]
    [InlineData("0", 0)]
    public void JumpToTimeReadsSecondsMinutesAndHours(string text, double seconds)
    {
        Assert.True(PlaybackTime.TryParse(text, out TimeSpan time));
        Assert.Equal(TimeSpan.FromSeconds(seconds), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1.5:00")]
    public void JumpToTimeRefusesWhatIsNotATime(string text) => Assert.False(PlaybackTime.TryParse(text, out _));

    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(36000, "10:00:00")]
    public void TimesShowAsVlcShowsThem(double seconds, string text) => Assert.Equal(text, PlaybackTime.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1, 1, 1.1)]
    [InlineData(1, -1, .9)]
    [InlineData(1.15, 1, 1.2)]
    [InlineData(.1, -1, 1d / 32)]
    [InlineData(31.25, 1, 31.25)]
    public void FasterAndSlowerTakeVlcsFineSteps(double rate, int direction, double expected) =>
        Assert.Equal(expected, PlaybackTime.FineRate(rate, direction), 3);

    [Fact]
    public void CommandLineMediaSkipsOptions()
    {
        Assert.Equal(["movie.mkv", "https://example.com/a.mp4"], CommandLineSources.From(["--smoke", "movie.mkv", "-psn_0_1234", "https://example.com/a.mp4"]));
        Assert.Equal(["-odd name.mp4"], CommandLineSources.From(["--", "-odd name.mp4"]));
        Assert.Empty(CommandLineSources.From(null));
    }
}

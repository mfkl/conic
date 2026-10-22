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

    [Fact]
    public void MacOpenFilesEventsSkipTheCommandLinesFilesOnce()
    {
        string cwd = Directory.CreateTempSubdirectory("conic-args").FullName;
        try
        {
            string movie = Path.Combine(cwd, "Test clip \u00e9.mkv"), other = Path.Combine(cwd, "other.mkv");
            File.WriteAllText(movie, ""); File.WriteAllText(other, "");
            // AppKit echoes a relative argument as an absolute path, possibly decomposed, and may
            // send each file in its own event; a URL or a missing file is never echoed.
            List<string> echoed = CommandLineSources.EchoedByMacOS(["Test clip \u00e9.mkv", new Uri(other).AbsoluteUri, "https://example.com/a.mp4", "missing.mkv"], cwd);
            Assert.Equal(2, echoed.Count);
            Assert.Empty(CommandLineSources.NotOnCommandLine([movie.Normalize(System.Text.NormalizationForm.FormD)], echoed, cwd));
            Assert.Empty(CommandLineSources.NotOnCommandLine([other], echoed, cwd));
            // Each argument stands for one echoed file; later opens go through.
            Assert.Equal([movie], CommandLineSources.NotOnCommandLine([movie], echoed, cwd));
            Assert.Empty(echoed);
            // A Finder start names nothing on the command line.
            Assert.Equal([movie, other], CommandLineSources.NotOnCommandLine([movie, other], CommandLineSources.EchoedByMacOS([], cwd), cwd));
        }
        finally { Directory.Delete(cwd, recursive: true); }
    }
}

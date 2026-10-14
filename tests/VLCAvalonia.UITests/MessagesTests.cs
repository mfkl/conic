using System.Text;
using Player.Application;
using VLCAvalonia.Interop;
using VLCAvalonia.ViewModels;
using VLCLR.Native;
using Xunit;

namespace VLCAvalonia.UITests;

/// <summary>The Messages window's list, and VLC's message formatting through the C library.</summary>
public sealed unsafe class MessagesTests
{
    [Fact]
    public void TheListShowsNewMessagesThatMatchTheFilter()
    {
        EngineMessageLog log = new() { Verbosity = 2 };
        MessagesViewModel messages = new(log);
        log.Add(EngineMessageType.Debug, "main", "input", "Creating an input for 'a.mp4'");
        log.Add(EngineMessageType.Debug, "videotoolbox", "decoder", "Using Video Toolbox to decode 'h264'");

        Assert.Equal(2, messages.Refresh());
        messages.Filter = "toolbox";
        Assert.Equal(["Using Video Toolbox to decode 'h264'"], messages.Messages.Select(message => message.Text));
        Assert.Equal("1 of 2 messages", messages.Summary);

        log.Add(EngineMessageType.Error, "main", "input", "Your input can't be opened");
        Assert.Equal(0, messages.Refresh());
        messages.Filter = "";
        Assert.Equal(3, messages.Messages.Count);
        Assert.Equal("3 messages", messages.Summary);
    }

    [Fact]
    public void VerbosityIsTheLogsAndClearEmptiesBoth()
    {
        EngineMessageLog log = new();
        MessagesViewModel messages = new(log);
        Assert.Equal("Errors and information", messages.SelectedVerbosity.Label);

        messages.SelectedVerbosity = messages.VerbosityChoices[2];
        Assert.Equal(2, log.Verbosity);
        log.Add(EngineMessageType.Debug, "main", "input", "one");
        messages.Refresh();
        messages.Clear();

        Assert.Empty(messages.Messages);
        Assert.Empty(log.Since(0));
        Assert.Equal("0 messages", messages.Summary);
    }

    [Fact]
    public void TheListDropsWhatTheLogNoLongerKeeps()
    {
        EngineMessageLog log = new(capacity: 2);
        MessagesViewModel messages = new(log);
        for (int index = 1; index <= 3; index++) log.Add(EngineMessageType.Error, "main", "input", $"m{index}");
        messages.Refresh();
        log.Add(EngineMessageType.Error, "main", "input", "m4");
        messages.Refresh();

        Assert.Equal(["m3", "m4"], messages.Messages.Select(message => message.Text));
    }

    [Fact]
    public void VlcsC99FormatsAreFormattedByTheCLibrary()
    {
        if (!VLCPlatform.IsSupported) return;
        byte[] format = Encoding.ASCII.GetBytes("%s: %zu bytes, %lld us, %.1f fps, %d%%\0");
        byte[] text = Encoding.ASCII.GetBytes("es\0");
        fixed (byte* formatPointer = format) fixed (byte* textPointer = text)
        {
            long* slots = stackalloc long[5];
            slots[0] = (long)textPointer;
            slots[1] = 4096;
            slots[2] = 40_000_000_000;
            slots[3] = VLCVaList.Double(23.976);
            slots[4] = 100;
            VLCVaList.SystemVTag tag;
            Assert.Equal("es: 4096 bytes, 40000000000 us, 24.0 fps, 100%", VlcLog.Format(formatPointer, VLCVaList.Create(slots, &tag)));
        }
    }

    [Fact]
    public void LongMessagesAreCutRatherThanLost()
    {
        if (!VLCPlatform.IsSupported) return;
        byte[] format = Encoding.ASCII.GetBytes("%s\0");
        byte[] text = Encoding.ASCII.GetBytes(new string('x', 5000) + "\0");
        fixed (byte* formatPointer = format) fixed (byte* textPointer = text)
        {
            long* slots = stackalloc long[1];
            slots[0] = (long)textPointer;
            VLCVaList.SystemVTag tag;
            string formatted = VlcLog.Format(formatPointer, VLCVaList.Create(slots, &tag));
            Assert.EndsWith("x…", formatted);
            Assert.Equal(4096, formatted.Length);
        }
    }
}

public sealed class MessageColorsTests
{
    [Fact]
    public void QuotedValuesAreSplitOutWithTheirQuotes()
    {
        Assert.Equal(
            [("using video decoder module ", false), ("\"videotoolbox\"", true), (" for ", false), ("'h264'", true)],
            Views.MessageColors.Segments("using video decoder module \"videotoolbox\" for 'h264'"));
    }

    [Theory]
    [InlineData("Your input can't be opened")]
    [InlineData("VLC's \"unterminated")]
    [InlineData("")]
    public void ApostrophesAndUnclosedQuotesStayText(string text)
    {
        Assert.All(Views.MessageColors.Segments(text), segment => Assert.False(segment.IsValue));
        Assert.Equal(text, string.Concat(Views.MessageColors.Segments(text).Select(segment => segment.Text)));
    }

    [Fact]
    public void ModulesKeepOneColourAndTheCoreAndAppHaveTheirOwn()
    {
        Assert.Equal("core", Views.MessageColors.ModuleClass("main"));
        Assert.Equal("app", Views.MessageColors.ModuleClass("vlc-avalonia"));
        Assert.Equal(Views.MessageColors.ModuleClass("videotoolbox"), Views.MessageColors.ModuleClass("videotoolbox"));
        string[] modules = ["videotoolbox", "avcodec", "faad", "auhal", "scaletempo", "cvpx", "swscale", "mkv", "es", "freetype"];
        Assert.True(modules.Select(Views.MessageColors.ModuleClass).Distinct().Count() >= 4, "common modules spread over the hues");
        Assert.All(modules, module => Assert.Matches("^hue[0-5]$", Views.MessageColors.ModuleClass(module)));
    }
}

using Conic.Interop;
using Xunit;

namespace Conic.UITests;

public sealed class PlaybackStartupTests
{
    [Fact]
    public void AvailableRuntimeDoesNotReportFailure()
    {
        int calls = 0;
        Assert.Null(PlaybackStartup.TryInitialize(() => calls++));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("missing-directory")]
    [InlineData("missing-plugin")]
    [InlineData("missing-dll")]
    [InlineData("wrong-architecture")]
    [InlineData("missing-export")]
    [InlineData("access-denied")]
    public void BrokenInstallationHasActionableFailure(string kind)
    {
        Exception error = kind switch
        {
            "missing-directory" => new DirectoryNotFoundException("runtime detail"),
            "missing-plugin" => new FileNotFoundException("runtime detail"),
            "missing-dll" => new DllNotFoundException("runtime detail"),
            "wrong-architecture" => new BadImageFormatException("runtime detail"),
            "missing-export" => new EntryPointNotFoundException("runtime detail"),
            _ => new UnauthorizedAccessException("runtime detail"),
        };
        var failure = PlaybackStartup.TryInitialize(() => throw error);
        Assert.NotNull(failure);
        Assert.Contains("Extract the complete Windows x64 package", failure.Message);
        Assert.Equal(error.Message, failure.Detail);
    }

    [Fact]
    public void ProgrammingErrorsAreNotMisreportedAsBrokenInstallations()
    {
        Assert.Throws<InvalidOperationException>(() => PlaybackStartup.TryInitialize(() => throw new InvalidOperationException()));
    }
}

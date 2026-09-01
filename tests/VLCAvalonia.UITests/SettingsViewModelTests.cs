using Player.Application;
using VLCAvalonia.ViewModels;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData("theme", "Appearance")]
    [InlineData("skip", "Playback")]
    [InlineData("volume", "General")]
    public void SearchSelectsTheMatchingPage(string query, string page)
    {
        SettingsViewModel viewModel = Create(out _);
        viewModel.Search = query;
        Assert.Equal(page, viewModel.SelectedPage.Name);
    }

    [Fact]
    public void ExposesPersistedValuesOptionsAndApplyModes()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        service.Update(s => s with { Theme = "Dark", SkipForwardSeconds = 22 });
        viewModel.Refresh();
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Contains("Compact", viewModel.DensityOptions);
        Assert.True(viewModel.IsGeneral);
        Assert.Equal("Live", viewModel.LabelFor(nameof(PlayerSettings.Theme)));
        Assert.Equal("Next open", viewModel.LabelFor(nameof(PlayerSettings.DefaultSpeed)));
        Assert.Equal("Restart", viewModel.LabelFor("Unknown"));
    }

    [Fact]
    public void ResetConfirmationCanBeCancelledAndOnlyResetsOnSecondAction()
    {
        SettingsViewModel viewModel = Create(out PlayerSettingsService service);
        service.Update(s => s with { ResumePlayback = false, SkipForwardSeconds = 22 });
        viewModel.ResetAllCommand.Execute(null);
        Assert.True(viewModel.ConfirmReset);
        viewModel.CancelResetCommand.Execute(null);
        Assert.False(viewModel.ConfirmReset);
        viewModel.ResetAllCommand.Execute(null);
        viewModel.ResetAllCommand.Execute(null);
        Assert.Equal(PlayerSettings.Default, service.Current);
    }

    private static SettingsViewModel Create(out PlayerSettingsService service)
    {
        service = new PlayerSettingsService(new MemoryStorage());
        return new SettingsViewModel(service);
    }

    private sealed class MemoryStorage : IPlayerSettingsStorage
    {
        public string? Content { get; private set; }
        public string? Read() => Content;
        public void WriteAtomically(string content) => Content = content;
        public void PreserveInvalid(string content) { }
    }
}

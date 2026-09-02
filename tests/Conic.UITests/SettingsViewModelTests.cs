using Player.Application;
using Conic.ViewModels;
using Conic;
using Avalonia.Controls;
using Xunit;

namespace Conic.UITests;

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

    [Fact]
    public void FailedConfirmedResetKeepsConfirmationAndCurrentValue()
    {
        ThrowingStorage storage = new();
        PlayerSettingsService service = new(storage);
        SettingsViewModel viewModel = new(service);
        viewModel.ResetAllCommand.Execute(null);
        viewModel.ResetAllCommand.Execute(null);
        Assert.True(viewModel.ConfirmReset);
        Assert.Contains("could not be reset", viewModel.ErrorMessage);
        Assert.Equal(PlayerSettings.Default, service.Current);
    }

    [Fact]
    public void HotkeyRowsKeepPendingEditsDuringSearchAndRefreshAfterSaveOrReset()
    {
        PlayerSettingsService service = new(new MemoryStorage());
        PlayerCommandRegistry registry = new();
        registry.Register(new(PlayerCommandId.OpenMedia, "Open media", "Media", "Ctrl+O"), _ => { });
        registry.Register(new(PlayerCommandId.OpenSettings, "Settings", "General", "Ctrl+,"), _ => { });
        SettingsViewModel viewModel = new(service, registry);
        ShortcutRow row = Assert.Single(viewModel.EditableShortcutRows, x => x.Command == PlayerCommandId.OpenMedia);
        row.PendingGesture = "Ctrl+M";
        viewModel.ShortcutSearch = "open";
        Assert.Same(row, Assert.Single(viewModel.EditableShortcutRows));
        Assert.Equal("Ctrl+M", row.PendingGesture);
        Assert.True(viewModel.SaveShortcut(row));
        Assert.Equal("Ctrl+M", Assert.Single(viewModel.EditableShortcutRows).PendingGesture);
        viewModel.ResetShortcut(PlayerCommandId.OpenMedia);
        Assert.Equal("Ctrl+O", Assert.Single(viewModel.EditableShortcutRows).PendingGesture);
    }

    [Fact]
    public void ShortcutFocusPolicyProtectsInteractiveEditors()
    {
        Assert.True(ShortcutFocusPolicy.Suppresses(new TextBox()));
        Assert.True(ShortcutFocusPolicy.Suppresses(new Slider()));
        Assert.True(ShortcutFocusPolicy.Suppresses(new ComboBox()));
        Assert.False(ShortcutFocusPolicy.Suppresses(null));
    }

    [Fact]
    public void AudioStartupArgumentsAreAllowlistedExactAndEmptyByDefault()
    {
        Assert.Empty(MainWindow.ToAudioArguments(PlayerSettings.Default));
        PlayerSettings configured = PlayerSettings.Default with { AudioOutputModule = "mmdevice", NormalizationMaximumLevel = 1.25, AudioPassthrough = true };
        Assert.Equal(["--aout=mmdevice", "--audio-filter=normvol", "--norm-max-level=1.25", "--spdif"], MainWindow.ToAudioArguments(configured));
        Assert.Equal(["--no-spdif"], MainWindow.ToAudioArguments(PlayerSettings.Default with { AudioPassthrough = false }));
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

    private sealed class ThrowingStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) => throw new IOException();
        public void PreserveInvalid(string content) { }
    }
}

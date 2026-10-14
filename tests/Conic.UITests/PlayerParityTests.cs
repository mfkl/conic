using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

/// <summary>Jump to Time, track cycling, drops and the clipboard, as VLC 3 does them.</summary>
public sealed class PlayerParityTests
{
    [Fact]
    public void JumpToTimeSeeksToTheTypedTime()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport);

        Assert.True(vm.JumpToTime("1:15", out string? error), error);
        Assert.Equal([.75], transport.Seeks);
        Assert.Equal("0:50", vm.JumpToTimeStartText);
        Assert.Equal("1:40", vm.JumpToTimeLengthText);
    }

    [Theory]
    [InlineData("soon", "Enter a time such as 1:23:45, 12:30 or 90.")]
    [InlineData("2:00", "The media is 1:40 long.")]
    public void JumpToTimeSaysWhyItCannot(string text, string message)
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport);

        Assert.False(vm.JumpToTime(text, out string? error));
        Assert.Equal(message, error);
        Assert.Empty(transport.Seeks);
    }

    [Fact]
    public void AudioCyclingSkipsDisableAndSubtitleCyclingIncludesIt()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport);
        vm.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready,
            [new("-1", "Disable", true, false), new("1", "English", false, false), new("2", "French", false, true)],
            [new("-1", "Disable", true, false), new("3", "English", false, true)],
            [], [], new(0, 0)));

        Assert.Equal("1", vm.NextTrack(MediaTrackKind.Audio)!.Id);
        Assert.Equal("-1", vm.NextTrack(MediaTrackKind.Subtitle)!.Id);
    }

    [Fact]
    public void OneAudioTrackHasNothingToCycle()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport);
        vm.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready, [new("-1", "Disable", true, false), new("1", "English", false, true)], [], [], [], new(0, 0)));

        Assert.Null(vm.NextTrack(MediaTrackKind.Audio));
        Assert.False(vm.Registry.CanExecute(PlayerCommandId.CycleAudioTrack));
    }

    [Fact]
    public void ASubtitleFileDroppedOnPlayingMediaIsAddedToIt()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Playing(transport);

        vm.OpenDroppedSources(["C:/media/movie.EN.SRT"]);

        Assert.Equal(["attach Subtitle C:/media/movie.EN.SRT"], transport.Calls);
        Assert.Equal(["a.mp4"], transport.Opened);
    }

    [Fact]
    public void ASubtitleFileDroppedWithNothingPlayingOpensAsMedia()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()));

        vm.OpenDroppedSources(["movie.srt"]);

        Assert.Empty(transport.Calls);
        Assert.Equal(["movie.srt"], transport.Opened);
    }

    [Fact]
    public async Task DroppedLinksOpenOneAfterAnother()
    {
        OpeningParityTests.QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()));

        vm.OpenDroppedText("https://example.com/a.mp4\n# a comment\nrtsp://camera.local/live\n");
        await OpeningParityTests.WaitFor(() => transport.OpenedQueues.Count > 0);

        Assert.Equal(["https://example.com/a.mp4", "rtsp://camera.local/live"], Assert.Single(transport.OpenedQueues));
    }

    [Fact]
    public async Task OpenLocationFromClipboardOpensWhatItHolds()
    {
        OpeningParityTests.QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()),
            clipboard: new Clipboard("\"smb://server/share/movie.mkv\""));

        vm.Registry.Execute(PlayerCommandId.OpenFromClipboard);
        await OpeningParityTests.WaitFor(() => transport.OpenedQueues.Count > 0);

        Assert.Equal(["smb://server/share/movie.mkv"], Assert.Single(transport.OpenedQueues));
    }

    [Fact]
    public async Task AnEmptyClipboardSaysSo()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, clipboard: new Clipboard("  "));

        vm.Registry.Execute(PlayerCommandId.OpenFromClipboard);
        await OpeningParityTests.WaitFor(() => vm.HasOpenError);

        Assert.Equal("The clipboard has no media address or file path.", vm.OpenError);
    }

    [Fact]
    public void CommandLineMediaOpensAsAQueue()
    {
        OpeningParityTests.QueueTransport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()));

        vm.OpenCommandLine(["movie.mkv"]);

        Assert.Equal(["movie.mkv"], Assert.Single(transport.OpenedQueues));
    }

    [Theory]
    [InlineData(PlayerCommandId.VolumeUp, "Volume 52%")]
    [InlineData(PlayerCommandId.PlaybackSlower, "Speed 0.9×")]
    [InlineData(PlayerCommandId.AudioDelayUp, "Audio delay 50 ms")]
    [InlineData(PlayerCommandId.NextFrame, "Next frame")]
    public void HotkeysSayWhatTheyDo(PlayerCommandId command, string feedback) =>
        Assert.Equal(feedback, Playing(new Transport()).ShortcutFeedbackText(command));

    // A macOS key equivalent runs before a text field sees the key.
    [Theory]
    [InlineData("Meta+V", false)]
    [InlineData("Meta+Up", false)]
    [InlineData("Meta+T", true)]
    [InlineData("Ctrl+Meta+F", true)]
    public void TheMacMenuLeavesTextEditingKeysToTextFields(string gesture, bool inMenu) =>
        Assert.Equal(inMenu, MainWindow.MenuGesture(gesture) is not null);

    private static PlayerPresentationViewModel Playing(Transport transport)
    {
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { }, sourceValidator: new MediaSourceValidator(new OpeningParityTests.ExistingFiles()));
        vm.OpenSource("a.mp4", MediaSourceKind.File);
        vm.ApplyEngineState(new(1, PlaybackStatus.Playing, "a.mp4", .5, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100), .5, true, null));
        vm.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready, [], [], [], [], new(0, 0)));
        return vm;
    }

    private sealed class Transport : IPlayerTransport, IMediaCapabilitiesTransport, IPlaybackNavigationTransport
    {
        public List<double> Seeks { get; } = [];
        public List<string> Opened { get; } = [];
        public List<string> Calls { get; } = [];
        public void Open(string source) => Opened.Add(source);
        public void TogglePause() { } public void Stop() { } public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) { } public void SetMuted(bool muted) { } public void Skip(TimeSpan offset) { } public void SetPlaybackRate(double playbackRate) { }
        public void NextFrame() { } public void NextChapter() { } public void PreviousChapter() { }
        public MediaCapabilitiesState MediaCapabilities => MediaCapabilitiesState.Initial;
        public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged { add { } remove { } }
        public void SelectTrack(MediaTrackKind kind, string id) => Calls.Add($"track {kind} {id}");
        public void SelectTitle(string id) { } public void SelectChapter(string id) { }
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) { } public void ResetDelay(MediaTrackKind kind) { }
        public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) { Calls.Add($"attach {kind} {path}"); return new(true, true, false); }
    }

    private sealed class Clipboard(string? text) : IClipboardService
    {
        public Task<string?> SetTextAsync(string value) => Task.FromResult<string?>(null);
        public Task<string?> GetTextAsync() => Task.FromResult(text);
    }

    private sealed class NoPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}

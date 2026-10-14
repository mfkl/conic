using Avalonia.Input;
using Player.Application;
using Player.Contracts;
using Conic.Controls;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

/// <summary>
/// Every default shortcut, from the key press the window turns into a
/// gesture (<see cref="MenuShortcuts.FromKey"/>) to the command's effect.
/// The window's own routing (focus, panels, Escape) is checked by
/// --interactions-smoke.
/// </summary>
public sealed class KeyboardShortcutTests
{
    [Theory]
    [InlineData(PlayerCommandId.TogglePause, "pause")]
    [InlineData(PlayerCommandId.ToggleMute, "mute True")]
    [InlineData(PlayerCommandId.SkipBackward, "skip -11")]
    [InlineData(PlayerCommandId.SkipForward, "skip 12")]
    [InlineData(PlayerCommandId.SkipBackwardVeryShort, "skip -2")]
    [InlineData(PlayerCommandId.SkipForwardVeryShort, "skip 2")]
    [InlineData(PlayerCommandId.SkipBackwardMedium, "skip -7")]
    [InlineData(PlayerCommandId.SkipForwardMedium, "skip 7")]
    [InlineData(PlayerCommandId.SkipBackwardLong, "skip -13")]
    [InlineData(PlayerCommandId.SkipForwardLong, "skip 13")]
    [InlineData(PlayerCommandId.ToggleFullscreen, "fullscreen")]
    [InlineData(PlayerCommandId.OpenMedia, "pick media")]
    [InlineData(PlayerCommandId.OpenFolder, "pick folder")]
    [InlineData(PlayerCommandId.OpenUrl, "url panel")]
    [InlineData(PlayerCommandId.OpenSettings, "settings")]
    [InlineData(PlayerCommandId.OpenLibrary, "library drawer")]
    [InlineData(PlayerCommandId.ToggleQueue, "Queue drawer")]
    [InlineData(PlayerCommandId.MediaInformation, "Information drawer, refresh")]
    [InlineData(PlayerCommandId.Snapshot, "snapshot; snapshot notice")]
    [InlineData(PlayerCommandId.TogglePictureInPicture, "picture in picture")]
    [InlineData(PlayerCommandId.ShowHome, "home")]
    [InlineData(PlayerCommandId.ShowMessages, "messages")]
    [InlineData(PlayerCommandId.Stop, "stop")]
    [InlineData(PlayerCommandId.QueuePrevious, "previous")]
    [InlineData(PlayerCommandId.QueueNext, "next")]
    [InlineData(PlayerCommandId.VolumeUp, "volume 0.52")]
    [InlineData(PlayerCommandId.VolumeDown, "volume 0.48")]
    [InlineData(PlayerCommandId.PlaybackFaster, "rate 1.1")]
    [InlineData(PlayerCommandId.PlaybackSlower, "rate 0.9")]
    [InlineData(PlayerCommandId.PlaybackNormal, "rate 1")]
    [InlineData(PlayerCommandId.PreviousChapter, "previous chapter")]
    [InlineData(PlayerCommandId.NextChapter, "next chapter")]
    [InlineData(PlayerCommandId.NextFrame, "next frame")]
    [InlineData(PlayerCommandId.AudioDelayDown, "Audio delay -50")]
    [InlineData(PlayerCommandId.AudioDelayUp, "Audio delay 50")]
    [InlineData(PlayerCommandId.SubtitleDelayDown, "Subtitle delay -50")]
    [InlineData(PlayerCommandId.SubtitleDelayUp, "Subtitle delay 50")]
    [InlineData(PlayerCommandId.CycleAudioTrack, "track Audio 2")]
    [InlineData(PlayerCommandId.CycleSubtitleTrack, "track Subtitle 3")]
    [InlineData(PlayerCommandId.JumpToTime, "jump to time")]
    [InlineData(PlayerCommandId.OpenFromClipboard, "read clipboard")]
    [InlineData(PlayerCommandId.ShowAbout, "about")]
    [InlineData(PlayerCommandId.ToggleABLoop, "loop A set")]
    [InlineData(PlayerCommandId.ToggleMinimalInterface, "hide ui")]
    [InlineData(PlayerCommandId.ShowHelp, "help")]
    [InlineData(PlayerCommandId.CycleProgram, "program 2")]
    public async Task EachDefaultShortcutKeyRunsItsCommand(PlayerCommandId command, string effect)
    {
        Player player = new();
        string gesture = PlayerShortcutPolicy.Effective(player.ViewModel.Registry, null)[command];
        KeyGesture key = MenuShortcuts.ToKeyGesture(gesture) ?? throw new InvalidOperationException($"{gesture} has no key");

        string pressed = MenuShortcuts.FromKey(key.Key, key.KeyModifiers);
        Assert.Equal(gesture, pressed);
        Assert.True(PlayerShortcutPolicy.TryDispatch(player.ViewModel.Registry, PlayerShortcutPolicy.Effective(player.ViewModel.Registry, null), pressed), $"{pressed} runs a command");
        await Task.Yield();

        Assert.Equal(effect.Split("; "), player.Effects());
    }

    [Fact]
    public void TheDefaultShortcutsAreTheOnesChecked()
    {
        Player player = new();
        PlayerCommandId[] checkedCommands = typeof(KeyboardShortcutTests).GetMethod(nameof(EachDefaultShortcutKeyRunsItsCommand))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(data => (PlayerCommandId)data.GetData(null!).Single()[0]).ToArray();

        Assert.Equal(46, checkedCommands.Length);
        Assert.Equal(checkedCommands.Order(), PlayerShortcutPolicy.Defaults(player.ViewModel.Registry).Select(shortcut => shortcut.Command).Order());
    }

    // The shortcut editor accepts digits, brackets and "=" (PlayerShortcutPolicy);
    // their keys must make the same gesture.
    [Theory]
    [InlineData(Key.D1, KeyModifiers.Control, "Ctrl+1")]
    [InlineData(Key.NumPad7, KeyModifiers.Alt, "Alt+7")]
    [InlineData(Key.OemOpenBrackets, KeyModifiers.None, "[")]
    [InlineData(Key.OemCloseBrackets, KeyModifiers.Shift, "Shift+]")]
    [InlineData(Key.OemPlus, KeyModifiers.Control, "Ctrl+=")]
    [InlineData(Key.OemComma, KeyModifiers.Control, "Ctrl+,")]
    public void KeysForEditableShortcutsMakeTheirGesture(Key key, KeyModifiers modifiers, string gesture)
    {
        Player player = new();
        Dictionary<string, string> overrides = new() { [nameof(PlayerCommandId.ToggleMute)] = gesture };

        Assert.Equal(gesture, MenuShortcuts.FromKey(key, modifiers));
        Assert.True(PlayerShortcutPolicy.TryDispatch(player.ViewModel.Registry, PlayerShortcutPolicy.Effective(player.ViewModel.Registry, overrides), MenuShortcuts.FromKey(key, modifiers)));
        Assert.Equal(["mute True"], player.Effects());
    }

    /// <summary>A view model playing a seekable file, with every capability a default shortcut needs; it records what each command did.</summary>
    private sealed class Player
    {
        private readonly List<string> _effects = [];
        private readonly Transport _transport;

        public Player()
        {
            _transport = new Transport(_effects);
            PlayerSettingsService settings = new(new SettingsStorage());
            settings.Update(s => s with { SkipBackwardSeconds = 11, SkipForwardSeconds = 12, VeryShortSkipSeconds = 2, MediumSkipSeconds = 7, LongSkipSeconds = 13 });
            ViewModel = new PlayerPresentationViewModel(_transport, new Picker(_effects), () => { },
                toggleFullscreen: () => _effects.Add("fullscreen"), openSettings: () => _effects.Add("settings"), settings: settings,
                sourceValidator: new MediaSourceValidator(new ExistingFiles()), history: new MediaHistoryService(new HistoryStorage()),
                showUrlPanel: () => _effects.Add("url panel"), clipboard: new Clipboard(_effects));
            ViewModel.PictureInPictureRequested += (_, _) => _effects.Add("picture in picture");
            ViewModel.JumpToTimeRequested += (_, _) => _effects.Add("jump to time");
            ViewModel.AboutRequested += (_, _) => _effects.Add("about");
            ViewModel.MinimalInterfaceRequested += (_, _) => _effects.Add("hide ui");
            ViewModel.HelpRequested += (_, _) => _effects.Add("help");
            ViewModel.MessagesRequested += (_, _) => _effects.Add("messages");
            ViewModel.OpenSource("a.mp4", MediaSourceKind.File);
            ViewModel.ApplyEngineState(new(1, PlaybackStatus.Playing, "a.mp4", .5, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100), .5, true, null));
            // Three queue items with the middle one playing, two audio
            // tracks, a subtitle track and two chapters: every cycle and
            // step has somewhere to go.
            ViewModel.ApplyQueueState(new(1, 1, QueueLoadState.Ready, [new("1", "a", "a.mp4", 0, false), new("2", "b", "b.mp4", 1, true), new("3", "c", "c.mp4", 2, false)], 1, QueueRepeatMode.None, QueueOrder.Normal));
            ViewModel.ApplyMediaCapabilities(new(1, 1, MediaCapabilityLoadState.Ready,
                [new("-1", "Disable", true, false), new("1", "English", false, true), new("2", "French", false, false)],
                [new("-1", "Disable", true, true), new("3", "English", false, false)],
                [], [new("0", "One", true), new("1", "Two", false)], new(0, 0)) { Programs = [new("1", "News", true), new("2", "Sports", false)] });
            ViewModel.ShowPlayerCommand.Execute(null);
            _effects.Clear();
        }

        public PlayerPresentationViewModel ViewModel { get; }

        /// <summary>What the commands did: transport calls, callbacks, and the panels and notices they opened.</summary>
        public IReadOnlyList<string> Effects()
        {
            List<string> effects = [.. _effects];
            if (ViewModel.IsLibraryOpen) effects.Add("library drawer");
            if (ViewModel.IsQueueOpen) effects.Add(ViewModel.DrawerTab + " drawer" + (_transport.Refreshed ? ", refresh" : ""));
            if (ViewModel.HasMediaControlsNotice) effects.Add("snapshot notice");
            if (ViewModel.ABLoopLabel == "A-B Loop: Set B") effects.Add("loop A set");
            if (ViewModel.IsHomeOpen) effects.Add("home");
            return effects;
        }
    }

    private sealed class Clipboard(List<string> effects) : IClipboardService
    {
        public Task<string?> SetTextAsync(string text) => Task.FromResult<string?>(null);
        public Task<string?> GetTextAsync() { effects.Add("read clipboard"); return Task.FromResult<string?>(null); }
    }

    private sealed class Transport(List<string> effects) : IPlayerTransport, IQueueTransport, IMediaCapabilitiesTransport, IMediaInformationTransport, IPlaybackNavigationTransport, IVideoSnapshotTransport
    {
        public bool RequestSnapshot() { effects.Add("snapshot"); return true; }
        public void SelectProgram(string id) => effects.Add($"program {id}");
        public bool Refreshed { get; private set; }
        public void Open(string source) { }
        public void TogglePause() => effects.Add("pause");
        public void Stop() => effects.Add("stop");
        public void Seek(double normalizedPosition) => effects.Add($"seek {normalizedPosition}");
        public void SetVolume(double normalizedVolume) => effects.Add("volume " + normalizedVolume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void SetMuted(bool muted) => effects.Add($"mute {muted}");
        public void Skip(TimeSpan offset) => effects.Add($"skip {offset.TotalSeconds}");
        public void SetPlaybackRate(double playbackRate) => effects.Add("rate " + playbackRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void NextFrame() => effects.Add("next frame");
        public void NextChapter() => effects.Add("next chapter");
        public void PreviousChapter() => effects.Add("previous chapter");

        public PlaybackQueueState QueueState => PlaybackQueueState.Initial;
        public event EventHandler<PlaybackQueueState>? QueueStateChanged { add { } remove { } }
        public void OpenQueue(IReadOnlyList<string> sources) { }
        public void Enqueue(IEnumerable<string> sources) { }
        public void SelectAndPlay(string token) { }
        public void Move(string token, int destinationIndex) { }
        public void Remove(string token) { }
        public void ClearQueue() { }
        public void Previous() => effects.Add("previous");
        public void Next() => effects.Add("next");
        public void SetRepeatMode(QueueRepeatMode mode) { }
        public void SetOrder(QueueOrder order) { }

        public MediaCapabilitiesState MediaCapabilities => MediaCapabilitiesState.Initial;
        public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged { add { } remove { } }
        public void SelectTrack(MediaTrackKind kind, string id) => effects.Add($"track {kind} {id}");
        public void SelectTitle(string id) { }
        public void SelectChapter(string id) { }
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) => effects.Add($"{kind} delay {milliseconds}");
        public void ResetDelay(MediaTrackKind kind) { }
        public ExternalTrackAttachmentState AttachExternalTrack(MediaTrackKind kind, string path) => new(false, false, false);

        public MediaInformationState MediaInformation => MediaInformationState.Unavailable;
        public event EventHandler<MediaInformationState>? MediaInformationChanged { add { } remove { } }
        public void RefreshMediaInformation() => Refreshed = true;
    }

    private sealed class Picker(List<string> effects) : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) { effects.Add("pick media"); return Task.FromResult<string?>(null); }
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) { effects.Add("pick folder"); return Task.FromResult<string?>(null); }
    }

    private sealed class SettingsStorage : IPlayerSettingsStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
        public void PreserveInvalid(string content) { }
    }

    private sealed class HistoryStorage : IMediaHistoryStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
    }

    private sealed class ExistingFiles : IMediaFileSystem
    {
        public bool FileExists(string path) => true;
        public bool DirectoryExists(string path) => false;
        public string GetFullPath(string path) => path;
    }
}

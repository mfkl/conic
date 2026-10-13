using Avalonia.Input;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Controls;
using VLCAvalonia.ViewModels;
using Xunit;

namespace VLCAvalonia.UITests;

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
    [InlineData(PlayerCommandId.Snapshot, "snapshot notice")]
    [InlineData(PlayerCommandId.TogglePictureInPicture, "picture in picture")]
    [InlineData(PlayerCommandId.ShowHome, "home")]
    public async Task EachDefaultShortcutKeyRunsItsCommand(PlayerCommandId command, string effect)
    {
        Player player = new();
        string gesture = PlayerShortcutPolicy.Effective(player.ViewModel.Registry, null)[command];
        KeyGesture key = MenuShortcuts.ToKeyGesture(gesture) ?? throw new InvalidOperationException($"{gesture} has no key");

        string pressed = MenuShortcuts.FromKey(key.Key, key.KeyModifiers);
        Assert.Equal(gesture, pressed);
        Assert.True(PlayerShortcutPolicy.TryDispatch(player.ViewModel.Registry, PlayerShortcutPolicy.Effective(player.ViewModel.Registry, null), pressed), $"{pressed} runs a command");
        await Task.Yield();

        Assert.Equal([effect], player.Effects());
    }

    [Fact]
    public void TheDefaultShortcutsAreTheOnesChecked()
    {
        Player player = new();
        PlayerCommandId[] checkedCommands = typeof(KeyboardShortcutTests).GetMethod(nameof(EachDefaultShortcutKeyRunsItsCommand))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(data => (PlayerCommandId)data.GetData(null!).Single()[0]).ToArray();

        Assert.Equal(21, checkedCommands.Length);
        Assert.Equal(checkedCommands.Order(), PlayerShortcutPolicy.Defaults(player.ViewModel.Registry).Select(shortcut => shortcut.Command).Order());
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
                showUrlPanel: () => _effects.Add("url panel"));
            ViewModel.PictureInPictureRequested += (_, _) => _effects.Add("picture in picture");
            ViewModel.OpenSource("a.mp4", MediaSourceKind.File);
            ViewModel.ApplyEngineState(new(1, PlaybackStatus.Playing, "a.mp4", .5, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100), .5, true, null));
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
            if (ViewModel.IsHomeOpen) effects.Add("home");
            return effects;
        }
    }

    private sealed class Transport(List<string> effects) : IPlayerTransport, IQueueTransport, IMediaCapabilitiesTransport, IMediaInformationTransport
    {
        public bool Refreshed { get; private set; }
        public void Open(string source) { }
        public void TogglePause() => effects.Add("pause");
        public void Stop() => effects.Add("stop");
        public void Seek(double normalizedPosition) => effects.Add($"seek {normalizedPosition}");
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) => effects.Add($"mute {muted}");
        public void Skip(TimeSpan offset) => effects.Add($"skip {offset.TotalSeconds}");
        public void SetPlaybackRate(double playbackRate) { }

        public PlaybackQueueState QueueState => PlaybackQueueState.Initial;
        public event EventHandler<PlaybackQueueState>? QueueStateChanged { add { } remove { } }
        public void OpenQueue(IReadOnlyList<string> sources) { }
        public void Enqueue(IEnumerable<string> sources) { }
        public void SelectAndPlay(string token) { }
        public void Move(string token, int destinationIndex) { }
        public void Remove(string token) { }
        public void ClearQueue() { }
        public void Previous() { }
        public void Next() { }
        public void SetRepeatMode(QueueRepeatMode mode) { }
        public void SetOrder(QueueOrder order) { }

        public MediaCapabilitiesState MediaCapabilities => MediaCapabilitiesState.Initial;
        public event EventHandler<MediaCapabilitiesState>? MediaCapabilitiesChanged { add { } remove { } }
        public void SelectTrack(MediaTrackKind kind, string id) { }
        public void SelectTitle(string id) { }
        public void SelectChapter(string id) { }
        public void SetDelayMilliseconds(MediaTrackKind kind, int milliseconds) { }
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

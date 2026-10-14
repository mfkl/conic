using System.Windows.Input;
using Player.Application;
using Player.Contracts;
using Conic.Commands;

namespace Conic.ViewModels;

// VLC 3's hotkeys that take no value: volume, speed, chapters, Next Frame,
// the delays, track cycling, Jump to Time, opening what the clipboard holds,
// and About. Each has VLC 3's default key, except "+" and "-", which a
// shortcut can't use: faster and slower take VLC's fine steps, "]" and "[".
internal sealed partial class PlayerPresentationViewModel
{
    /// <summary>VLC 3's audio and subtitle delay hotkeys move by 50 ms.</summary>
    internal const int HotkeyDelayStepMilliseconds = 50;

    /// <summary>Jump to Time asks the window for its dialog.</summary>
    public event EventHandler? JumpToTimeRequested;

    /// <summary>About asks the window for its dialog.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>Hide UI asks the window to overlay its bars on the video.</summary>
    public event EventHandler? MinimalInterfaceRequested;

    /// <summary>Help asks the window to open the online help.</summary>
    public event EventHandler? HelpRequested;

    // VLC 3's A-B loop: the first press marks A, the second B (playback then
    // goes back to A whenever it reaches B), the third clears both.
    private TimeSpan? _loopStart, _loopEnd;
    public bool IsABLoopActive => _loopEnd is not null;
    public string ABLoopLabel => _loopStart is null ? "A-B Loop: Set A" : _loopEnd is null ? "A-B Loop: Set B" : "A-B Loop: Off";

    private void InitializeHotkeyCommands(PlayerCommandRegistry registry)
    {
        IPlaybackNavigationTransport? navigation = _transport as IPlaybackNavigationTransport;
        registry.Register(new(PlayerCommandId.VolumeUp, "Volume up", "Audio", "Ctrl+Up"), _ => Registry.Execute(PlayerCommandId.SetVolume, SteppedVolume(1)), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.VolumeDown, "Volume down", "Audio", "Ctrl+Down"), _ => Registry.Execute(PlayerCommandId.SetVolume, SteppedVolume(-1)), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.PlaybackFaster, "Faster", "Playback", "]"), _ => Registry.Execute(PlayerCommandId.SetPlaybackRate, PlaybackTime.FineRate(PlaybackRate, 1)), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.PlaybackSlower, "Slower", "Playback", "["), _ => Registry.Execute(PlayerCommandId.SetPlaybackRate, PlaybackTime.FineRate(PlaybackRate, -1)), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.PlaybackNormal, "Normal speed", "Playback", "="), _ => Registry.Execute(PlayerCommandId.SetPlaybackRate, 1d), _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.PreviousChapter, "Previous chapter", "Playback", "Shift+P"), _ => navigation?.PreviousChapter(), _ => navigation is not null && Chapters.Count > 1);
        registry.Register(new(PlayerCommandId.NextChapter, "Next chapter", "Playback", "Shift+N"), _ => navigation?.NextChapter(), _ => navigation is not null && Chapters.Count > 1);
        registry.Register(new(PlayerCommandId.NextFrame, "Next frame", "Playback", "E"), _ => navigation?.NextFrame(), _ => navigation is not null && PlaybackStatus is PlaybackStatus.Playing or PlaybackStatus.Paused);
        registry.Register(new(PlayerCommandId.AudioDelayDown, "Audio delay down", "Audio", "J"), _ => Registry.Execute(PlayerCommandId.AdjustAudioDelay, -HotkeyDelayStepMilliseconds), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.AudioDelayUp, "Audio delay up", "Audio", "K"), _ => Registry.Execute(PlayerCommandId.AdjustAudioDelay, HotkeyDelayStepMilliseconds), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.SubtitleDelayDown, "Subtitle delay down", "Subtitles", "G"), _ => Registry.Execute(PlayerCommandId.AdjustSubtitleDelay, -HotkeyDelayStepMilliseconds), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.SubtitleDelayUp, "Subtitle delay up", "Subtitles", "H"), _ => Registry.Execute(PlayerCommandId.AdjustSubtitleDelay, HotkeyDelayStepMilliseconds), _ => CanAdjustDelays);
        registry.Register(new(PlayerCommandId.CycleAudioTrack, "Cycle audio track", "Audio", "B"), _ => { if (NextTrack(MediaTrackKind.Audio) is { } track) _mediaControls?.SelectTrack(MediaTrackKind.Audio, track.Id); }, _ => CanAdjustDelays && NextTrack(MediaTrackKind.Audio) is not null);
        registry.Register(new(PlayerCommandId.CycleSubtitleTrack, "Cycle subtitle track", "Subtitles", "V"), _ => { if (NextTrack(MediaTrackKind.Subtitle) is { } track) _mediaControls?.SelectTrack(MediaTrackKind.Subtitle, track.Id); }, _ => CanAdjustDelays && NextTrack(MediaTrackKind.Subtitle) is not null);
        registry.Register(new(PlayerCommandId.JumpToTime, "Jump to time", "Playback", "Ctrl+T"), _ => JumpToTimeRequested?.Invoke(this, EventArgs.Empty), _ => CanSeek && _duration > TimeSpan.Zero);
        registry.Register(new(PlayerCommandId.OpenFromClipboard, "Open location from clipboard", "Media", "Ctrl+V"), _ => _ = OpenFromClipboardAsync(), _ => _clipboard is not null);
        registry.Register(new(PlayerCommandId.ShowAbout, "About", "Window", "Shift+F1"), _ => AboutRequested?.Invoke(this, EventArgs.Empty));
        registry.Register(new(PlayerCommandId.ToggleABLoop, "A-B loop", "Playback", "Shift+L"), _ => ToggleABLoop(), _ => CanSeek && _duration > TimeSpan.Zero);
        registry.Register(new(PlayerCommandId.ToggleMinimalInterface, "Hide UI", "Window", "Ctrl+H"), _ => MinimalInterfaceRequested?.Invoke(this, EventArgs.Empty));
        registry.Register(new(PlayerCommandId.ShowHelp, "Help", "Window", "F1"), _ => HelpRequested?.Invoke(this, EventArgs.Empty));
        registry.Register(new(PlayerCommandId.SortQueue, "Sort queue by name", "Playback"), _ => SortQueue(), _ => _queue is not null && QueueItems.Count > 1);
        // VLC 3's X: the next program of a multi-program stream (DVB, MPEG-TS).
        registry.Register(new(PlayerCommandId.CycleProgram, "Next program", "Playback", "X"), _ => { if (NextProgram() is { } program) _mediaControls?.SelectProgram(program.Id); }, _ => NextProgram() is not null);
        foreach (PlayerCommandId id in HotkeyMenuCommands) _hotkeyCommands[id] = new RegistryCommand(registry, id);
    }

    // The commands menus show (right-click, More, macOS), refreshed with the playback state.
    private static readonly PlayerCommandId[] HotkeyMenuCommands =
    [
        PlayerCommandId.JumpToTime, PlayerCommandId.NextFrame, PlayerCommandId.PreviousChapter, PlayerCommandId.NextChapter,
        PlayerCommandId.PlaybackFaster, PlayerCommandId.PlaybackSlower, PlayerCommandId.PlaybackNormal,
        PlayerCommandId.VolumeUp, PlayerCommandId.VolumeDown, PlayerCommandId.OpenFromClipboard, PlayerCommandId.ShowAbout,
        PlayerCommandId.CycleProgram, PlayerCommandId.ToggleABLoop, PlayerCommandId.ToggleMinimalInterface, PlayerCommandId.ShowHelp,
        PlayerCommandId.SortQueue,
    ];
    private readonly Dictionary<PlayerCommandId, RegistryCommand> _hotkeyCommands = [];
    public ICommand JumpToTimeCommand => _hotkeyCommands[PlayerCommandId.JumpToTime];
    public ICommand NextFrameCommand => _hotkeyCommands[PlayerCommandId.NextFrame];
    public ICommand PreviousChapterCommand => _hotkeyCommands[PlayerCommandId.PreviousChapter];
    public ICommand NextChapterCommand => _hotkeyCommands[PlayerCommandId.NextChapter];
    public ICommand PlaybackFasterCommand => _hotkeyCommands[PlayerCommandId.PlaybackFaster];
    public ICommand PlaybackSlowerCommand => _hotkeyCommands[PlayerCommandId.PlaybackSlower];
    public ICommand PlaybackNormalCommand => _hotkeyCommands[PlayerCommandId.PlaybackNormal];
    public ICommand VolumeUpCommand => _hotkeyCommands[PlayerCommandId.VolumeUp];
    public ICommand VolumeDownCommand => _hotkeyCommands[PlayerCommandId.VolumeDown];
    public ICommand OpenFromClipboardCommand => _hotkeyCommands[PlayerCommandId.OpenFromClipboard];
    public ICommand ShowAboutCommand => _hotkeyCommands[PlayerCommandId.ShowAbout];
    public ICommand CycleProgramCommand => _hotkeyCommands[PlayerCommandId.CycleProgram];
    public ICommand ToggleABLoopCommand => _hotkeyCommands[PlayerCommandId.ToggleABLoop];
    public ICommand ToggleMinimalInterfaceCommand => _hotkeyCommands[PlayerCommandId.ToggleMinimalInterface];
    public ICommand ShowHelpCommand => _hotkeyCommands[PlayerCommandId.ShowHelp];
    public ICommand SortQueueCommand => _hotkeyCommands[PlayerCommandId.SortQueue];

    /// <summary>Where playback is now, from the last engine state.</summary>
    private TimeSpan CurrentTime => _duration * Position;

    private void ToggleABLoop()
    {
        if (_loopStart is null) _loopStart = CurrentTime;
        else if (_loopEnd is null)
        {
            // B before A swaps them, so the loop is always forward.
            TimeSpan end = CurrentTime;
            if (end < _loopStart) (_loopStart, end) = (end, _loopStart.Value);
            _loopEnd = end;
            _transport.Seek(_loopStart.Value.TotalSeconds / _duration.TotalSeconds);
        }
        else ClearABLoop();
        OnPropertyChanged(nameof(ABLoopLabel)); OnPropertyChanged(nameof(IsABLoopActive));
    }

    private void ClearABLoop()
    {
        if (_loopStart is null && _loopEnd is null) return;
        _loopStart = _loopEnd = null;
        OnPropertyChanged(nameof(ABLoopLabel)); OnPropertyChanged(nameof(IsABLoopActive));
    }

    /// <summary>Each engine state: back to A once playback reaches B.</summary>
    private void FollowABLoop(MediaEngineState state)
    {
        if (_loopStart is not { } start || _loopEnd is not { } end || state.Duration <= TimeSpan.Zero) return;
        if (state.Status == PlaybackStatus.Playing && state.Elapsed >= end) _transport.Seek(start.TotalSeconds / state.Duration.TotalSeconds);
    }

    public IReadOnlyList<MediaProgram> Programs => MediaCapabilities.Programs;
    public bool HasPrograms => Programs.Count > 1;

    /// <summary>The program X selects: the one after the selected one, round.</summary>
    internal MediaProgram? NextProgram()
    {
        IReadOnlyList<MediaProgram> programs = Programs;
        if (programs.Count < 2) return null;
        int current = -1;
        for (int index = 0; index < programs.Count; index++) if (programs[index].IsSelected) current = index;
        return programs[(current + 1) % programs.Count];
    }

    private void RaiseHotkeyCommands()
    {
        foreach (RegistryCommand command in _hotkeyCommands.Values) command.RaiseCanExecuteChanged();
    }

    private double SteppedVolume(int direction) => Math.Clamp(Math.Round(Volume + direction * VolumeStep), 0, MaximumVolume);

    /// <summary>
    /// The track a cycle key selects next, as VLC 3's B and V keys pick it:
    /// audio goes round the tracks and skips Disable; subtitles go round
    /// Disable too.
    /// </summary>
    internal MediaTrack? NextTrack(MediaTrackKind kind)
    {
        IReadOnlyList<MediaTrack> tracks = kind == MediaTrackKind.Audio ? AudioTracks.Where(track => !track.IsDisabled).ToArray() : SubtitleTracks;
        if (tracks.Count < 2) return null;
        int current = -1;
        for (int index = 0; index < tracks.Count; index++) if (tracks[index].IsSelected) current = index;
        return tracks[(current + 1) % tracks.Count];
    }

    /// <summary>What a hotkey of this file is about to do, for the shortcut feedback (see <see cref="ShortcutFeedbackText"/>).</summary>
    private string? HotkeyFeedbackText(PlayerCommandId command) => command switch
    {
        PlayerCommandId.VolumeUp => $"Volume {SteppedVolume(1):0}%",
        PlayerCommandId.VolumeDown => $"Volume {SteppedVolume(-1):0}%",
        PlayerCommandId.PlaybackFaster => SpeedText(PlaybackTime.FineRate(PlaybackRate, 1)),
        PlayerCommandId.PlaybackSlower => SpeedText(PlaybackTime.FineRate(PlaybackRate, -1)),
        PlayerCommandId.PlaybackNormal => SpeedText(1),
        PlayerCommandId.PreviousChapter => "Previous chapter",
        PlayerCommandId.NextChapter => "Next chapter",
        PlayerCommandId.NextFrame => "Next frame",
        PlayerCommandId.AudioDelayDown => DelayText("Audio", AudioDelayMilliseconds - HotkeyDelayStepMilliseconds),
        PlayerCommandId.AudioDelayUp => DelayText("Audio", AudioDelayMilliseconds + HotkeyDelayStepMilliseconds),
        PlayerCommandId.SubtitleDelayDown => DelayText("Subtitle", SubtitleDelayMilliseconds - HotkeyDelayStepMilliseconds),
        PlayerCommandId.SubtitleDelayUp => DelayText("Subtitle", SubtitleDelayMilliseconds + HotkeyDelayStepMilliseconds),
        PlayerCommandId.CycleAudioTrack => NextTrack(MediaTrackKind.Audio) is { } audio ? "Audio track: " + audio.Label : null,
        PlayerCommandId.CycleSubtitleTrack => NextTrack(MediaTrackKind.Subtitle) is { } subtitle ? "Subtitle track: " + subtitle.Label : null,
        PlayerCommandId.ToggleABLoop => _loopStart is null ? "A-B loop: A set" : _loopEnd is null ? "A-B loop: B set, looping" : "A-B loop off",
        PlayerCommandId.CycleProgram => NextProgram() is { } program ? "Program: " + program.Label : null,
        PlayerCommandId.Snapshot => "Snapshot",
        _ => null,
    };

    private static string SpeedText(double rate) => "Speed " + rate.ToString("0.0#", System.Globalization.CultureInfo.CurrentCulture) + "×";

    private static string DelayText(string kind, int milliseconds) => $"{kind} delay {MediaDelayPolicy.Clamp(milliseconds)} ms";

    /// <summary>Jump to Time: seeks to the typed time. False, with the reason, when the time can't be used.</summary>
    public bool JumpToTime(string? text, out string? error)
    {
        if (!PlaybackTime.TryParse(text, out TimeSpan time)) { error = "Enter a time such as 1:23:45, 12:30 or 90."; return false; }
        if (!CanSeek || _duration <= TimeSpan.Zero) { error = "This media can't jump to a time."; return false; }
        if (time > _duration) { error = $"The media is {PlaybackTime.Format(_duration)} long."; return false; }
        error = null;
        Registry.Execute(PlayerCommandId.Seek, time.TotalSeconds / _duration.TotalSeconds);
        return true;
    }

    /// <summary>The time Jump to Time starts from: where the media is.</summary>
    public string JumpToTimeStartText => PlaybackTime.Format(_duration * Position);

    /// <summary>The media's length, for the Jump to Time dialog.</summary>
    public string JumpToTimeLengthText => PlaybackTime.Format(_duration);

    /// <summary>
    /// VLC 3's Open Location from Clipboard: each line that is a URL or a
    /// path opens, several as a queue, as a drop does.
    /// </summary>
    private async Task OpenFromClipboardAsync()
    {
        string? text = _clipboard is null ? null : await _clipboard.GetTextAsync();
        await OpenTextSourcesAsync(text, "The clipboard has no media address or file path.");
    }

    /// <summary>Media named on the command line or opened from the file manager: one opens, several open as a queue.</summary>
    public void OpenCommandLine(IReadOnlyList<string> sources)
    {
        if (sources.Count == 1) OpenSource(sources[0], MediaSourceKind.DragDrop);
        else if (sources.Count > 1) _ = OpenMultipleAsync(sources, MediaSourceKind.DragDrop);
    }

    /// <summary>Text dropped on the window, such as a link from a browser: one URL or path per line.</summary>
    public void OpenDroppedText(string? text) => _ = OpenTextSourcesAsync(text, "Drop a media file or a link to open it.");

    private async Task OpenTextSourcesAsync(string? text, string emptyMessage)
    {
        string[] lines = (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim('"'))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : line)
            .ToArray();
        if (lines.Length == 0) { OpenError = emptyMessage; return; }
        if (lines.Length == 1) { OpenSource(lines[0], MediaSourceKind.DragDrop); return; }
        await OpenMultipleAsync(lines, MediaSourceKind.DragDrop);
    }
}

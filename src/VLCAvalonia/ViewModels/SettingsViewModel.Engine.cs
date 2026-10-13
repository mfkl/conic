using Player.Application;

namespace VLCAvalonia.ViewModels;

// Playback rows (phase 9b). Most of them are
// libVLC options the next open's core starts with (PlayerEngineOptions); the
// choices offered are the ones PlayerEngineOptions knows how to map.
internal sealed partial class SettingsViewModel
{
    private static readonly string[] EngineRowProperties =
    [
        nameof(ResumeMode), nameof(AutoPlay), nameof(AudioLanguage), nameof(SubtitleLanguage), nameof(QueueRepeat), nameof(QueueShuffle),
        nameof(StopAfterEachItem), nameof(PlaybackFinished), nameof(SeekPrecisely), nameof(AviRepair),
    ];

    private PlayerSettings Current => _service.Current;
    private static SettingOption Find(IReadOnlyList<SettingOption> options, string value) => options.FirstOrDefault(option => option.Value == value) ?? options[0];

    // Playback › Startup. Continue where you left off: Never turns resuming
    // off, Ask shows the prompt, Always resumes without asking.
    public IReadOnlyList<SettingOption> ResumeModes => PlayerEngineOptions.ResumeModes;
    public SettingOption ResumeMode
    {
        get => Find(ResumeModes, !Current.ResumePlayback ? "Never" : Current.ResumeAutomatically ? "Always" : "Ask");
        set { if (value is not null) Update(s => s with { ResumePlayback = value.Value != "Never", ResumeAutomatically = value.Value == "Always" }); }
    }
    public bool AutoPlay { get => Current.AutoPlay; set => Update(s => s with { AutoPlay = value }); }

    // Playback › Tracks.
    public string AudioLanguage { get => Current.AudioLanguage; set => Update(s => s with { AudioLanguage = value ?? string.Empty }); }
    public string SubtitleLanguage { get => Current.SubtitleLanguage; set => Update(s => s with { SubtitleLanguage = value ?? string.Empty }); }

    // Playback › Order.
    public IReadOnlyList<SettingOption> RepeatModes => PlayerEngineOptions.RepeatModes;
    public SettingOption QueueRepeat { get => Find(RepeatModes, Current.QueueRepeat); set { if (value is not null) Update(s => s with { QueueRepeat = value.Value }); } }
    public bool QueueShuffle { get => Current.QueueShuffle; set => Update(s => s with { QueueShuffle = value }); }
    public bool StopAfterEachItem { get => Current.StopAfterEachItem; set => Update(s => s with { StopAfterEachItem = value }); }
    public IReadOnlyList<SettingOption> PlaybackFinishedActions => PlayerEngineOptions.PlaybackFinishedActions;
    public SettingOption PlaybackFinished { get => Find(PlaybackFinishedActions, Current.PlaybackFinished); set { if (value is not null) Update(s => s with { PlaybackFinished = value.Value }); } }

    // Playback › Seeking and Files.
    public bool SeekPrecisely { get => Current.SeekPrecisely; set => Update(s => s with { SeekPrecisely = value }); }
    public IReadOnlyList<SettingOption> AviRepairModes => PlayerEngineOptions.AviRepairModes;
    public SettingOption AviRepair { get => Find(AviRepairModes, Current.AviRepair); set { if (value is not null) Update(s => s with { AviRepair = value.Value }); } }

    private static PlayerSettings ResetPlaybackPage(PlayerSettings s, PlayerSettings d) => s with
    {
        ResumePlayback = d.ResumePlayback, ResumeAutomatically = d.ResumeAutomatically, ResumeMinimumElapsedSeconds = d.ResumeMinimumElapsedSeconds,
        ResumeMaximumRemainingSeconds = d.ResumeMaximumRemainingSeconds, DefaultSpeed = d.DefaultSpeed, AutoPlay = d.AutoPlay,
        AudioLanguage = d.AudioLanguage, SubtitleLanguage = d.SubtitleLanguage, QueueRepeat = d.QueueRepeat, QueueShuffle = d.QueueShuffle,
        StopAfterEachItem = d.StopAfterEachItem, PlaybackFinished = d.PlaybackFinished, SeekPrecisely = d.SeekPrecisely, AviRepair = d.AviRepair,
    };
}

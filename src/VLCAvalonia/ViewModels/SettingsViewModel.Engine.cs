using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

// Playback, Video and Performance rows (phase 9b). Most of them are
// libVLC options the next open's core starts with (PlayerEngineOptions); the
// choices offered are the ones PlayerEngineOptions knows how to map.
internal sealed partial class SettingsViewModel
{
    private static readonly string[] EngineRowProperties =
    [
        nameof(ResumeMode), nameof(AutoPlay), nameof(AudioLanguage), nameof(SubtitleLanguage), nameof(QueueRepeat), nameof(QueueShuffle),
        nameof(StopAfterEachItem), nameof(PlaybackFinished), nameof(SeekPrecisely), nameof(AviRepair),
        nameof(VideoEnabled), nameof(StartFullscreen), nameof(StopScreensaver), nameof(Hue), nameof(Brightness), nameof(Contrast),
        nameof(Saturation), nameof(Gamma), nameof(CanAdjustGamma), nameof(Deinterlace), nameof(DeinterlaceMethod), nameof(DefaultAspectRatio),
        nameof(DefaultCrop), nameof(DefaultZoom),
        nameof(HardwareDecoding), nameof(SkipLoopFilter), nameof(DecodingThreads), nameof(DropLateFrames),
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

    // Video › Output. The screensaver row is Windows only (DisplayRequest).
    public bool VideoEnabled { get => Current.VideoEnabled; set => Update(s => s with { VideoEnabled = value }); }
    public bool StartFullscreen { get => Current.StartFullscreen; set => Update(s => s with { StartFullscreen = value }); }
    public bool StopScreensaver { get => Current.StopScreensaver; set => Update(s => s with { StopScreensaver = value }); }
    public bool ShowsScreensaver { get; } = OperatingSystem.IsWindows();

    // Video › Picture. Sliders write on every step; equal values are skipped.
    public double Hue { get => Current.Hue; set => UpdatePicture(Current.Hue, value, (s, v) => s with { Hue = v }); }
    public double Brightness { get => Current.Brightness; set => UpdatePicture(Current.Brightness, value, (s, v) => s with { Brightness = v }); }
    public double Contrast { get => Current.Contrast; set => UpdatePicture(Current.Contrast, value, (s, v) => s with { Contrast = v }); }
    public double Saturation { get => Current.Saturation; set => UpdatePicture(Current.Saturation, value, (s, v) => s with { Saturation = v }); }
    public double Gamma { get => Current.Gamma; set => UpdatePicture(Current.Gamma, value, (s, v) => s with { Gamma = v }); }
    // VLC's Direct3D 11 adjust filter (hardware decoding) has no gamma.
    public bool CanAdjustGamma => Current.HardwareDecoding == "Off";
    public ICommand ResetPictureCommand => _resetPictureCommand ??= new RelayCommand(_ => Update(s => s with
    {
        Hue = PlayerSettings.Default.Hue, Brightness = PlayerSettings.Default.Brightness, Contrast = PlayerSettings.Default.Contrast,
        Saturation = PlayerSettings.Default.Saturation, Gamma = PlayerSettings.Default.Gamma,
    }));
    private ICommand? _resetPictureCommand;

    private void UpdatePicture(double current, double value, Func<PlayerSettings, double, PlayerSettings> update)
    {
        double rounded = Math.Round(value, 2);
        if (double.IsFinite(rounded) && rounded != current) Update(s => update(s, rounded));
    }

    // Video › Deinterlacing and Framing.
    public IReadOnlyList<SettingOption> DeinterlaceModes => PlayerEngineOptions.DeinterlaceModes;
    public SettingOption Deinterlace { get => Find(DeinterlaceModes, Current.Deinterlace); set { if (value is not null) Update(s => s with { Deinterlace = value.Value }); } }
    public IReadOnlyList<SettingOption> DeinterlaceMethods => PlayerEngineOptions.DeinterlaceMethods;
    public SettingOption DeinterlaceMethod { get => Find(DeinterlaceMethods, Current.DeinterlaceMethod); set { if (value is not null) Update(s => s with { DeinterlaceMethod = value.Value }); } }
    public IReadOnlyList<SettingOption> AspectRatios => PlayerEngineOptions.AspectRatios;
    public SettingOption DefaultAspectRatio { get => Find(AspectRatios, Current.DefaultAspectRatio); set { if (value is not null) Update(s => s with { DefaultAspectRatio = value.Value }); } }
    public IReadOnlyList<SettingOption> Crops => PlayerEngineOptions.Crops;
    public SettingOption DefaultCrop { get => Find(Crops, Current.DefaultCrop); set { if (value is not null) Update(s => s with { DefaultCrop = value.Value }); } }
    public IReadOnlyList<SettingOption> Zooms => PlayerEngineOptions.Zooms;
    public SettingOption DefaultZoom { get => Find(Zooms, Current.DefaultZoom); set { if (value is not null) Update(s => s with { DefaultZoom = value.Value }); } }

    // Performance.
    public IReadOnlyList<SettingOption> HardwareDecodingModes { get; } = PlayerEngineOptions.HardwareDecodingModes();
    public SettingOption HardwareDecoding { get => Find(HardwareDecodingModes, Current.HardwareDecoding); set { if (value is not null) Update(s => s with { HardwareDecoding = value.Value }); } }
    public IReadOnlyList<SettingOption> LoopFilterModes => PlayerEngineOptions.LoopFilterModes;
    public SettingOption SkipLoopFilter
    {
        get => Find(LoopFilterModes, Current.SkipLoopFilter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set { if (value is not null) Update(s => s with { SkipLoopFilter = int.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture) }); }
    }
    public decimal DecodingThreads { get => Current.DecodingThreads; set => Update(s => s with { DecodingThreads = (int)value }); }
    public bool DropLateFrames { get => Current.DropLateFrames; set => Update(s => s with { DropLateFrames = value }); }

    private static PlayerSettings ResetPlaybackPage(PlayerSettings s, PlayerSettings d) => s with
    {
        ResumePlayback = d.ResumePlayback, ResumeAutomatically = d.ResumeAutomatically, ResumeMinimumElapsedSeconds = d.ResumeMinimumElapsedSeconds,
        ResumeMaximumRemainingSeconds = d.ResumeMaximumRemainingSeconds, DefaultSpeed = d.DefaultSpeed, AutoPlay = d.AutoPlay,
        AudioLanguage = d.AudioLanguage, SubtitleLanguage = d.SubtitleLanguage, QueueRepeat = d.QueueRepeat, QueueShuffle = d.QueueShuffle,
        StopAfterEachItem = d.StopAfterEachItem, PlaybackFinished = d.PlaybackFinished, SeekPrecisely = d.SeekPrecisely, AviRepair = d.AviRepair,
    };

    private static PlayerSettings ResetVideoPage(PlayerSettings s, PlayerSettings d) => s with
    {
        VideoEnabled = d.VideoEnabled, StartFullscreen = d.StartFullscreen, StopScreensaver = d.StopScreensaver, Hue = d.Hue, Brightness = d.Brightness,
        Contrast = d.Contrast, Saturation = d.Saturation, Gamma = d.Gamma, Deinterlace = d.Deinterlace, DeinterlaceMethod = d.DeinterlaceMethod,
        DefaultAspectRatio = d.DefaultAspectRatio, DefaultCrop = d.DefaultCrop, DefaultZoom = d.DefaultZoom,
    };

    private static PlayerSettings ResetPerformancePage(PlayerSettings s, PlayerSettings d) => s with
    {
        HardwareDecoding = d.HardwareDecoding, SkipLoopFilter = d.SkipLoopFilter,
        DecodingThreads = d.DecodingThreads, DropLateFrames = d.DropLateFrames,
    };
}

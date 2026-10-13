using System.ComponentModel;
using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;
using VLCAvalonia.Interop.Windows;

namespace VLCAvalonia.ViewModels;

/// <summary>One band of the equalizer: its frequency and gain in dB.</summary>
internal sealed class EqualizerBand(int index, string frequency, Func<int, double> read, Action<int, double> write) : INotifyPropertyChanged
{
    public string Frequency { get; } = frequency;
    public double Gain { get => read(index); set => write(index, value); }
    public string GainText => $"{Gain:0.0} dB";
    public string Name => $"Equalizer {Frequency}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { PropertyChanged?.Invoke(this, new(nameof(Gain))); PropertyChanged?.Invoke(this, new(nameof(GainText))); }
}

// Audio rows (phase 9c): output, volume, loudness, effects, tracks and the
// advanced options. The effects, replay gain and the device reach a playing
// core at once; the rest apply at the next open (AudioEngineOptions), the
// startup volume at the next start.
internal sealed partial class SettingsViewModel
{
    private static readonly string[] AudioRowProperties =
    [
        nameof(AudioEnabled), nameof(AudioDevice), nameof(CanChooseAudioDevice), nameof(MaximumVolume), nameof(RemembersVolume),
        nameof(StartsAtFixedVolume), nameof(StartupVolume), nameof(VolumeStep), nameof(NormalizeLoudness), nameof(NormalizationTarget),
        nameof(ReplayGain), nameof(HasReplayGain), nameof(ReplayGainPreamp), nameof(Equalizer), nameof(EqualizerPreset),
        nameof(EqualizerPreamp), nameof(EqualizerPreampText), nameof(SpatialAudio), nameof(SpatialAudioMode),
        nameof(Compressor), nameof(PitchShift), nameof(Karaoke), nameof(AudioDelay), nameof(DolbySurround), nameof(ResamplerQuality),
    ];

    // Output. The device list is Windows' playback endpoints (the MMDevice
    // output's devices), read again each time the Audio page opens.
    public bool AudioEnabled { get => Current.AudioEnabled; set => Update(s => s with { AudioEnabled = value }); }
    public bool ShowsAudioDevice { get; } = OperatingSystem.IsWindows();
    public IReadOnlyList<SettingOption> AudioDevices
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return [];
            // A list without endpoints (Core Audio unavailable for a moment) is read again next time.
            if (_audioDevices is not { Count: > 1 }) _audioDevices = AudioEndpoints.List();
            // A device that is not connected now stays selected (MMDevice falls back to the default).
            return Current.AudioDevice.Length > 0 && _audioDevices.All(device => device.Value != Current.AudioDevice)
                ? [.. _audioDevices, new(Current.AudioDevice, "Not connected")]
                : _audioDevices;
        }
    }
    private IReadOnlyList<SettingOption>? _audioDevices;
    public SettingOption? AudioDevice
    {
        get => AudioDevices.FirstOrDefault(device => device.Value == Current.AudioDevice) ?? AudioDevices.FirstOrDefault();
        set { if (value is not null && value.Value != Current.AudioDevice) Update(s => s with { AudioDevice = value.Value }); }
    }
    public bool CanChooseAudioDevice => AudioEngineOptions.UsesDevice(Current);

    private void RefreshAudioDevices()
    {
        _audioDevices = null;
        Notify(nameof(AudioDevices));
        Notify(nameof(AudioDevice));
    }

    // Volume, in percent. Startup volume: the last session's volume
    // (Remember volume) or a fixed one.
    public double MaximumVolume
    {
        get => Current.MaximumVolume;
        set { int rounded = (int)Math.Round(value / 25) * 25; if (rounded != Current.MaximumVolume) Update(s => s with { MaximumVolume = rounded }); }
    }
    public bool RemembersVolume { get => Current.RememberVolume; set { if (value) Update(s => s with { RememberVolume = true }); } }
    public bool StartsAtFixedVolume { get => !Current.RememberVolume; set { if (value) Update(s => s with { RememberVolume = false }); } }
    public decimal StartupVolume { get => Current.StartupVolume; set => Update(s => s with { StartupVolume = (int)value }); }
    public decimal VolumeStep { get => Current.VolumeStep; set => Update(s => s with { VolumeStep = (int)value }); }

    // Loudness. Normalization is on while it has a level (VLC's default 2).
    public bool NormalizeLoudness
    {
        get => Current.NormalizationMaximumLevel is not null;
        set { if (value != NormalizeLoudness) Update(s => s with { NormalizationMaximumLevel = value ? AudioEngineOptions.DefaultNormalizationLevel : null }); }
    }
    public decimal NormalizationTarget
    {
        get => (decimal)(Current.NormalizationMaximumLevel ?? AudioEngineOptions.DefaultNormalizationLevel);
        set { if (NormalizeLoudness) Update(s => s with { NormalizationMaximumLevel = (double)value }); }
    }
    public IReadOnlyList<SettingOption> ReplayGainModes => AudioEngineOptions.ReplayGainModes;
    public SettingOption ReplayGain { get => Find(ReplayGainModes, Current.ReplayGain); set { if (value is not null) Update(s => s with { ReplayGain = value.Value }); } }
    public bool HasReplayGain => Current.ReplayGain != "none";
    public decimal ReplayGainPreamp { get => (decimal)Current.ReplayGainPreamp; set => Update(s => s with { ReplayGainPreamp = (double)value }); }

    // Effects. A preset fills the equalizer; changing a gain makes it Custom.
    public bool Equalizer { get => Current.Equalizer; set => Update(s => s with { Equalizer = value }); }
    public IReadOnlyList<EqualizerPreset> EqualizerPresets => AudioEngineOptions.EqualizerPresets;
    public EqualizerPreset EqualizerPreset
    {
        get => EqualizerPresets.FirstOrDefault(preset => preset.Value == Current.EqualizerPreset) ?? EqualizerPresets[^1];
        set
        {
            if (value is null || value.Value == Current.EqualizerPreset) return;
            Update(s => value.Value == AudioEngineOptions.CustomEqualizerPreset
                ? s with { EqualizerPreset = value.Value }
                : s with { EqualizerPreset = value.Value, EqualizerPreamp = value.Preamp, EqualizerBands = AudioEngineOptions.FormatBands(value.Bands) });
        }
    }
    public bool IsEqualizerOpen { get => _isEqualizerOpen; set { if (Set(ref _isEqualizerOpen, value)) Notify(nameof(EqualizerToggleText)); } }
    private bool _isEqualizerOpen;
    public string EqualizerToggleText => IsEqualizerOpen ? "Close equalizer" : "Open equalizer";
    public ICommand ToggleEqualizerCommand => _toggleEqualizerCommand ??= new RelayCommand(_ => IsEqualizerOpen = !IsEqualizerOpen);
    private ICommand? _toggleEqualizerCommand;
    public double EqualizerPreamp
    {
        get => Current.EqualizerPreamp;
        set => UpdateEqualizer(Math.Round(value, 1), Current.EqualizerBands, Current.EqualizerPreamp);
    }
    public string EqualizerPreampText => $"{EqualizerPreamp:0.0} dB";
    public IReadOnlyList<EqualizerBand> EqualizerBands => _equalizerBands ??= AudioEngineOptions.EqualizerFrequencies
        .Select((frequency, index) => new EqualizerBand(index, frequency, band => AudioEngineOptions.ParseBands(Current.EqualizerBands)[band], SetBand))
        .ToArray();
    private IReadOnlyList<EqualizerBand>? _equalizerBands;

    private void SetBand(int band, double gain)
    {
        double[] bands = [.. AudioEngineOptions.ParseBands(Current.EqualizerBands)];
        bands[band] = Math.Round(gain, 1);
        UpdateEqualizer(Current.EqualizerPreamp, AudioEngineOptions.FormatBands(bands), Current.EqualizerPreamp);
    }

    private void UpdateEqualizer(double preamp, string bands, double currentPreamp)
    {
        if (!double.IsFinite(preamp) || (preamp == currentPreamp && bands == Current.EqualizerBands)) return;
        Update(s => s with { EqualizerPreamp = preamp, EqualizerBands = bands, EqualizerPreset = AudioEngineOptions.PresetFor(preamp, bands) });
    }

    public bool SpatialAudio { get => Current.SpatialAudio; set => Update(s => s with { SpatialAudio = value }); }
    public IReadOnlyList<SettingOption> SpatialAudioModes => AudioEngineOptions.SpatialAudioModes;
    public SettingOption SpatialAudioMode { get => Find(SpatialAudioModes, Current.SpatialAudioMode); set { if (value is not null) Update(s => s with { SpatialAudioMode = value.Value }); } }
    public bool Compressor { get => Current.Compressor; set => Update(s => s with { Compressor = value }); }
    public decimal PitchShift { get => (decimal)Current.PitchShift; set => Update(s => s with { PitchShift = (double)value }); }
    public bool Karaoke { get => Current.Karaoke; set => Update(s => s with { Karaoke = value }); }

    // Tracks: the preferred language is Playback › Tracks' setting, shown on both pages.
    public decimal AudioDelay { get => Current.AudioDelay; set => Update(s => s with { AudioDelay = (int)value }); }
    public int MaximumAudioDelay => MediaDelayPolicy.MaximumMilliseconds;

    // Advanced.
    public IReadOnlyList<SettingOption> DolbySurroundModes => AudioEngineOptions.DolbySurroundModes;
    public SettingOption DolbySurround { get => Find(DolbySurroundModes, Current.DolbySurround); set { if (value is not null) Update(s => s with { DolbySurround = value.Value }); } }
    public IReadOnlyList<SettingOption> ResamplerQualities => AudioEngineOptions.ResamplerQualities;
    public SettingOption ResamplerQuality { get => Find(ResamplerQualities, Current.ResamplerQuality); set { if (value is not null) Update(s => s with { ResamplerQuality = value.Value }); } }

    private void RefreshAudioRows()
    {
        foreach (string name in AudioRowProperties) Notify(name);
        if (_equalizerBands is not null) foreach (EqualizerBand band in _equalizerBands) band.Refresh();
    }

    private static PlayerSettings ResetAudioPage(PlayerSettings s, PlayerSettings d) => s with
    {
        AudioEnabled = d.AudioEnabled, AudioDevice = d.AudioDevice, MaximumVolume = d.MaximumVolume, RememberVolume = d.RememberVolume,
        StartupVolume = d.StartupVolume, VolumeStep = d.VolumeStep, NormalizationMaximumLevel = d.NormalizationMaximumLevel, ReplayGain = d.ReplayGain,
        ReplayGainPreamp = d.ReplayGainPreamp, Equalizer = d.Equalizer, EqualizerPreset = d.EqualizerPreset, EqualizerPreamp = d.EqualizerPreamp,
        EqualizerBands = d.EqualizerBands, SpatialAudio = d.SpatialAudio, SpatialAudioMode = d.SpatialAudioMode,
        Compressor = d.Compressor, PitchShift = d.PitchShift, Karaoke = d.Karaoke, AudioLanguage = d.AudioLanguage, AudioDelay = d.AudioDelay,
        AudioOutputModule = d.AudioOutputModule, AudioPassthrough = d.AudioPassthrough, DolbySurround = d.DolbySurround, ResamplerQuality = d.ResamplerQuality,
    };
}

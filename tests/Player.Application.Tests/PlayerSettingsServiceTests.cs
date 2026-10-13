using Player.Application;
using Xunit;

namespace Player.Application.Tests;

public sealed class PlayerSettingsServiceTests
{
    [Fact]
    public void ReduceMotionMigratesAsALivePreference()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"Theme\":\"Dark\",\"ReduceMotion\":true}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.True(service.Current.ReduceMotion);
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.ReduceMotion)));
        Assert.Contains($"\"Version\": {PlayerSettings.CurrentVersion}", storage.Content);
    }

    [Fact]
    public void PersistsAndReloadsNormalizedSettings()
    {
        MemoryStorage storage = new();
        PlayerSettingsService first = new(storage);
        first.Update(s => s with { Theme = "Dark", SkipForwardSeconds = 33 });
        PlayerSettingsService reloaded = new(storage); reloaded.Load();
        Assert.Equal("Dark", reloaded.Current.Theme); Assert.Equal(33, reloaded.Current.SkipForwardSeconds);
    }
    [Fact]
    public void InvalidDocumentIsPreservedAndDefaultsAreRestored()
    {
        MemoryStorage storage = new() { Content = "not json" };
        PlayerSettingsService service = new(storage); service.Load();
        Assert.Equal(PlayerSettings.Default, service.Current); Assert.Equal("not json", storage.Preserved);
    }
    [Fact]
    public void OlderVersionMigratesAndLiveClassificationIsExplicit()
    {
        MemoryStorage storage = new() { Content = "{\"version\":1,\"resumePlayback\":false,\"rememberVolume\":true,\"theme\":\"Dark\",\"density\":\"Compact\",\"defaultSpeed\":1.5,\"skipBackwardSeconds\":8,\"skipForwardSeconds\":12}" };
        PlayerSettingsService service = new(storage); service.Load();
        Assert.Equal(PlayerSettings.CurrentVersion, service.Current.Version);
        Assert.Equal("Dark", service.Current.Theme); Assert.Equal("Compact", service.Current.Density); Assert.Equal(3, service.Current.ControlTimeoutSeconds); Assert.Equal(12, service.Current.SkipForwardSeconds);
        Assert.Contains($"\"Version\": {PlayerSettings.CurrentVersion}", storage.Content);
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.SkipForwardSeconds)));
        Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.DefaultSpeed)));
    }
    [Fact]
    public void AtomicWriteFailureDoesNotChangeCurrentSettings()
    {
        MemoryStorage storage = new() { ThrowOnWrite = true }; PlayerSettingsService service = new(storage);
        Assert.Throws<IOException>(() => service.Update(s => s with { Theme = "Light" }));
        Assert.Equal("Dark", service.Current.Theme);
    }
    [Fact]
    public void PageAndGlobalResetsHaveDistinctScopes()
    {
        MemoryStorage storage = new(); PlayerSettingsService service = new(storage);
        service.Update(s => s with { Theme = "Light", SkipForwardSeconds = 42, ResumePlayback = false }); service.ResetAppearance();
        Assert.Equal("Dark", service.Current.Theme); Assert.Equal(42, service.Current.SkipForwardSeconds); Assert.False(service.Current.ResumePlayback);
        service.ResetAll(); Assert.Equal(PlayerSettings.Default, service.Current);
    }
    [Fact]
    public void Version5DocumentMigratesToVersion8WithTheDefaultAccent()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":5,\"Theme\":\"Dark\",\"Density\":\"Compact\",\"SkipForwardSeconds\":25,\"ReduceMotion\":true}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(8, PlayerSettings.CurrentVersion);
        Assert.Equal(PlayerSettings.CurrentVersion, service.Current.Version);
        Assert.Equal("Dark", service.Current.Theme);
        Assert.Equal("Compact", service.Current.Density);
        Assert.Equal(25, service.Current.SkipForwardSeconds);
        Assert.True(service.Current.ReduceMotion);
        // Reduce motion kept the controls up before version 8.
        Assert.False(service.Current.AutoHideControls);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal(PlayerSettings.Default.HardwareDecoding, service.Current.HardwareDecoding);
        Assert.Equal(PlayerSettings.Default.Buffering, service.Current.Buffering);
        Assert.Contains("\"Version\": 8", storage.Content);
        Assert.Contains($"\"Accent\": \"{AccentPalette.DefaultHex}\"", storage.Content);
        Assert.Null(service.LoadDiagnostic);
    }

    [Fact]
    public void OlderVersionsGetTheDefaultAccent()
    {
        MemoryStorage storage = new() { Content = "{\"version\":3,\"theme\":\"Dark\",\"accent\":\"#1B5DD3\"}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        // Accent did not exist before version 6, so an older document cannot set it.
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal("Dark", service.Current.Theme);
    }

    [Fact]
    public void LightThemeAndPaletteAccentsPersistInTheirCanonicalSpelling()
    {
        MemoryStorage storage = new();
        new PlayerSettingsService(storage).Update(s => s with { Theme = "Light", Accent = " #1b5dd3 " });
        PlayerSettingsService reloaded = new(storage);

        reloaded.Load();

        Assert.Equal("Light", reloaded.Current.Theme);
        Assert.Equal("#1B5DD3", reloaded.Current.Accent);
    }

    [Theory]
    [InlineData("\"Theme\":\"Sepia\",\"Accent\":\"#123456\"")]
    [InlineData("\"Theme\":\"light\",\"Accent\":\"orange\"")]
    [InlineData("\"Theme\":null,\"Accent\":null")]
    [InlineData("\"Accent\":\"\"")]
    public void InvalidThemeAndAccentFallBackToDefaults(string values)
    {
        MemoryStorage storage = new() { Content = $"{{\"Version\":6,\"SkipForwardSeconds\":20,{values}}}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(PlayerSettings.Default.Theme, service.Current.Theme);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal(20, service.Current.SkipForwardSeconds);
    }

    [Theory]
    [InlineData(null, "Dark")]
    [InlineData("{\"Version\":6,\"Theme\":\"System\"}", "System")]
    [InlineData("{\"Version\":6,\"Theme\":\"Light\"}", "Light")]
    [InlineData("{\"Version\":6,\"Theme\":\"Sepia\"}", "Dark")]
    [InlineData("{\"Version\":4,\"Density\":\"Compact\"}", "Dark")]
    public void DarkIsTheDefaultThemeAndStoredChoicesAreKept(string? document, string expected)
    {
        MemoryStorage storage = new() { Content = document };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal("Dark", PlayerSettings.Default.Theme);
        // A stored "System" is a choice, not an old default: it is not migrated.
        Assert.Equal(expected, service.Current.Theme);
    }

    [Fact]
    public void ThemeOptionsAndPaletteMatchTheDesign()
    {
        Assert.Equal(["Dark", "Light", "System"], PlayerSettings.Themes);
        Assert.Equal(9, AccentPalette.Colors.Count);
        Assert.Equal(AccentPalette.DefaultHex, AccentPalette.Colors[0].Hex);
        Assert.Equal(AccentPalette.DefaultHex, PlayerSettings.Default.Accent);
        Assert.All(AccentPalette.Colors, color => Assert.Matches("^#[0-9A-F]{6}$", color.Hex));
        Assert.Equal(AccentPalette.Colors.Count, AccentPalette.Colors.Select(color => color.Name).Distinct().Count());
    }

    [Fact]
    public void ThemeAndAccentApplyLiveAndResetWithTheAppearance()
    {
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.Theme)));
        Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.Accent)));
        PlayerSettingsService service = new(new MemoryStorage());
        service.Update(s => s with { Theme = "Light", Accent = "#8E43FD", SkipForwardSeconds = 42 });

        service.ResetAppearance();

        Assert.Equal(PlayerSettings.Default.Theme, service.Current.Theme);
        Assert.Equal(AccentPalette.DefaultHex, service.Current.Accent);
        Assert.Equal(42, service.Current.SkipForwardSeconds);
    }

    [Theory]
    [InlineData("0.5", .5)]
    [InlineData("1.75", 1.75)]
    [InlineData("4", 2)]
    [InlineData("3", 2)]
    [InlineData("0.1", .25)]
    [InlineData("1.1", 1)]
    [InlineData("1.13", 1.25)]
    public void DefaultSpeedSnapsToTheDesignStepsAndKeepsOldValuesInRange(string stored, double expected)
    {
        // Before phase 9a the setting offered 0.5 to 2 and accepted up to 4.
        MemoryStorage storage = new() { Content = $"{{\"Version\":6,\"DefaultSpeed\":{stored}}}" };
        PlayerSettingsService service = new(storage);

        service.Load();

        Assert.Equal(expected, service.Current.DefaultSpeed);
        Assert.Null(service.LoadDiagnostic);
        Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.DefaultSpeed)));
    }

    [Fact]
    public void Version6DocumentKeepsEveryValueAndGetsTheVersion8Defaults()
    {
        // A complete phase 9a (version 6) document.
        const string document = """
            {"Version":6,"ResumePlayback":false,"RememberVolume":false,"Theme":"Light","Density":"Compact","ControlTimeoutSeconds":7,
             "DefaultSpeed":1.5,"SkipBackwardSeconds":5,"SkipForwardSeconds":30,"RememberRecentMedia":false,"RememberResumeHistory":false,
             "ResumeMinimumElapsedSeconds":60,"ResumeMaximumRemainingSeconds":90,"AudioOutputModule":"directsound","NormalizationMaximumLevel":2.5,
             "AudioPassthrough":true,"ShortcutOverrides":{"TogglePause":"K"},"ReduceMotion":true,"Accent":"#1B5DD3"}
            """;
        MemoryStorage storage = new() { Content = document };
        PlayerSettingsService service = new(storage);

        service.Load();

        PlayerSettings current = service.Current;
        Assert.Null(service.LoadDiagnostic);
        Assert.Equal(8, current.Version);
        Assert.Equal((false, false, "Light", "Compact", 7, 1.5, 5, 30), (current.ResumePlayback, current.RememberVolume, current.Theme, current.Density, current.ControlTimeoutSeconds, current.DefaultSpeed, current.SkipBackwardSeconds, current.SkipForwardSeconds));
        Assert.Equal((false, false, 60, 90, "directsound", (double?)2.5, (bool?)true, true, "#1B5DD3"), (current.RememberRecentMedia, current.RememberResumeHistory, current.ResumeMinimumElapsedSeconds, current.ResumeMaximumRemainingSeconds, current.AudioOutputModule, current.NormalizationMaximumLevel, current.AudioPassthrough, current.ReduceMotion, current.Accent));
        Assert.Equal("K", current.ShortcutOverrides!["TogglePause"]);
        // Every version 7 and 8 field takes its default, but Reduce motion
        // (on here) turns Auto-hide the controls off.
        PlayerSettings defaults = PlayerSettings.Default;
        Assert.Equal(defaults with
        {
            ResumePlayback = false, RememberVolume = false, Theme = "Light", Density = "Compact", ControlTimeoutSeconds = 7, DefaultSpeed = 1.5,
            SkipBackwardSeconds = 5, SkipForwardSeconds = 30, RememberRecentMedia = false, RememberResumeHistory = false,
            ResumeMinimumElapsedSeconds = 60, ResumeMaximumRemainingSeconds = 90, AudioOutputModule = "directsound", NormalizationMaximumLevel = 2.5,
            AudioPassthrough = true, ShortcutOverrides = current.ShortcutOverrides, ReduceMotion = true, Accent = "#1B5DD3", AutoHideControls = false,
        }, current);
        // The stored audio options are the next core's (phase 9c moved them from the engine's constructor).
        Assert.Equal(["--aout=directsound", "--spdif", "--audio-filter=normvol", "--norm-max-level=2.5"], PlayerEngineOptions.Arguments(current, windows: true, macOS: false));
        Assert.Contains("\"Version\": 8", storage.Content);
        Assert.Contains("\"HardwareDecoding\": \"Automatic\"", storage.Content);
    }

    [Fact]
    public void Version7ValuesRoundTripAndInvalidOnesFallBack()
    {
        MemoryStorage storage = new();
        new PlayerSettingsService(storage).Update(s => s with
        {
            ResumeAutomatically = true, AutoPlay = false, AudioLanguage = " FR, en;xx1 ", QueueRepeat = "All", QueueShuffle = true,
            PlaybackFinished = "Quit", Hue = 400, Brightness = 1.234, Gamma = double.NaN, Deinterlace = "On", DeinterlaceMethod = "yadif",
            DefaultCrop = "16:9", DefaultZoom = "3", SkipLoopFilter = 9, DecodingThreads = -2, Buffering = "Custom", NetworkCaching = 999999,
            HttpProxy = "  proxy:3128 ", CipherPreference = "Paranoid",
        });
        PlayerSettingsService reloaded = new(storage);

        reloaded.Load();

        PlayerSettings current = reloaded.Current;
        Assert.True(current.ResumeAutomatically); Assert.False(current.AutoPlay);
        Assert.Equal("fr,en", current.AudioLanguage);
        Assert.Equal(("All", true, "Quit"), (current.QueueRepeat, current.QueueShuffle, current.PlaybackFinished));
        Assert.Equal((180d, 1.23, 1d), (current.Hue, current.Brightness, current.Gamma));
        // "yadif" (single rate) is not a mode the Direct3D 11 deinterlacer has.
        Assert.Equal(("On", "Automatic"), (current.Deinterlace, current.DeinterlaceMethod));
        Assert.Equal(("16:9", "fit"), (current.DefaultCrop, current.DefaultZoom));
        Assert.Equal((4, 0), (current.SkipLoopFilter, current.DecodingThreads));
        Assert.Equal(("Custom", PlayerEngineOptions.MaximumCaching), (current.Buffering, current.NetworkCaching));
        Assert.Equal(("proxy:3128", "Normal"), (current.HttpProxy, current.CipherPreference));
    }

    [Fact]
    public void EngineSettingsApplyAtTheNextOpenAndAppSettingsLive()
    {
        foreach (string key in PlayerEngineOptions.Keys)
            Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(key));
        foreach (string key in new[] { nameof(PlayerSettings.ResumeAutomatically), nameof(PlayerSettings.StartFullscreen) })
            Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(key));
        foreach (string key in new[] { nameof(PlayerSettings.QueueRepeat), nameof(PlayerSettings.QueueShuffle), nameof(PlayerSettings.PlaybackFinished), nameof(PlayerSettings.StopScreensaver) })
            Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(key));
        // Every Keys entry names a settings field.
        Assert.All(PlayerEngineOptions.Keys, key => Assert.NotNull(typeof(PlayerSettings).GetProperty(key)));
    }

    [Fact]
    public void Version7DocumentWithoutTheAudioFieldsGetsTheirDefaults()
    {
        MemoryStorage storage = new() { Content = """
            {"Version":7,"ResumePlayback":true,"RememberVolume":false,"Theme":"Light","Density":"Compact","ControlTimeoutSeconds":5,"DefaultSpeed":1.5,
             "SkipBackwardSeconds":10,"SkipForwardSeconds":10,"AudioOutputModule":"directsound","NormalizationMaximumLevel":3,"AudioPassthrough":true,"AudioLanguage":"fr"}
            """ };
        PlayerSettingsService service = new(storage);

        service.Load();

        PlayerSettings current = service.Current;
        Assert.Equal((8, false, "directsound", (double?)3, (bool?)true, "fr"), (current.Version, current.RememberVolume, current.AudioOutputModule, current.NormalizationMaximumLevel, current.AudioPassthrough, current.AudioLanguage));
        PlayerSettings d = PlayerSettings.Default;
        Assert.Equal((d.AudioEnabled, d.AudioDevice, d.MaximumVolume, d.StartupVolume, d.VolumeStep), (current.AudioEnabled, current.AudioDevice, current.MaximumVolume, current.StartupVolume, current.VolumeStep));
        Assert.Equal((true, "", 200, 100, 2), (current.AudioEnabled, current.AudioDevice, current.MaximumVolume, current.StartupVolume, current.VolumeStep));
        Assert.Equal(("none", 0d, false, "flat", 12d, "0 0 0 0 0 0 0 0 0 0"), (current.ReplayGain, current.ReplayGainPreamp, current.Equalizer, current.EqualizerPreset, current.EqualizerPreamp, current.EqualizerBands));
        Assert.Equal((false, "Headphones", false, 0d, false), (current.SpatialAudio, current.SpatialAudioMode, current.Compressor, current.PitchShift, current.Karaoke));
        Assert.Equal(("Automatic", "Balanced", 0), (current.DolbySurround, current.ResamplerQuality, current.AudioDelay));
        // Version 8 changed what Reduce motion means, so a version 7
        // document is rewritten as version 8 (Reduce motion off here keeps
        // the controls hiding).
        Assert.True(current.AutoHideControls);
        Assert.Contains("\"Version\": 8", storage.Content);
        Assert.Contains("\"Equalizer\": false", storage.Content);
    }

    [Fact]
    public void AudioValuesRoundTripAndInvalidOnesFallBack()
    {
        MemoryStorage storage = new();
        new PlayerSettingsService(storage).Update(s => s with
        {
            AudioEnabled = false, AudioDevice = " {0.0.0.00000000}.{id} ", MaximumVolume = 450, StartupVolume = -5, VolumeStep = 0,
            ReplayGain = "loud", ReplayGainPreamp = 6.04, Equalizer = true, EqualizerPreset = "rock", EqualizerPreamp = 5,
            EqualizerBands = "8 4.8 -5.6 -8 -3.2 4 8.8 11.2 11.2 11.2", SpatialAudioMode = "Hall", PitchShift = 40, DolbySurround = "Maybe",
            ResamplerQuality = "Best", AudioDelay = 99999,
        });
        PlayerSettingsService reloaded = new(storage);

        reloaded.Load();

        PlayerSettings current = reloaded.Current;
        Assert.Equal((false, "{0.0.0.00000000}.{id}", 300, 0, 1), (current.AudioEnabled, current.AudioDevice, current.MaximumVolume, current.StartupVolume, current.VolumeStep));
        Assert.Equal(("none", 6d), (current.ReplayGain, current.ReplayGainPreamp));
        Assert.Equal((true, "rock", 5d), (current.Equalizer, current.EqualizerPreset, current.EqualizerPreamp));
        Assert.Equal(("Headphones", 12d, "Automatic", "Best", 10_000), (current.SpatialAudioMode, current.PitchShift, current.DolbySurround, current.ResamplerQuality, current.AudioDelay));
    }

    [Fact]
    public void AudioSettingsApplyLiveAtTheNextOpenOrAtTheNextStart()
    {
        foreach (string key in AudioEngineOptions.LiveKeys.Concat([nameof(PlayerSettings.MaximumVolume), nameof(PlayerSettings.VolumeStep)]))
            Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(key));
        foreach (string key in AudioEngineOptions.NextOpenKeys)
            Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(key));
        Assert.Equal(SettingApplyMode.Restart, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.StartupVolume)));
        Assert.All(AudioEngineOptions.LiveKeys.Concat(AudioEngineOptions.NextOpenKeys), key => Assert.NotNull(typeof(PlayerSettings).GetProperty(key)));
    }

    [Fact]
    public void Version7ReduceMotionKeepsTheControlsUpAsAutoHideOff()
    {
        // A complete phase 9c (version 7) document with Reduce motion on:
        // it used to keep the playback controls from hiding.
        MemoryStorage storage = new() { Content = """
            {"Version":7,"ResumePlayback":true,"RememberVolume":true,"Theme":"Dark","Density":"Comfortable","ControlTimeoutSeconds":4,"DefaultSpeed":1,
             "SkipBackwardSeconds":10,"SkipForwardSeconds":10,"ReduceMotion":true,"SubtitleLanguage":"none","Equalizer":true,"EqualizerPreset":"rock"}
            """ };
        PlayerSettingsService service = new(storage);

        service.Load();

        PlayerSettings current = service.Current;
        Assert.Null(service.LoadDiagnostic);
        Assert.Equal((8, true, false, 4), (current.Version, current.ReduceMotion, current.AutoHideControls, current.ControlTimeoutSeconds));
        Assert.Equal(("none", true, "rock"), (current.SubtitleLanguage, current.Equalizer, current.EqualizerPreset));
        // The version 8 fields other than auto-hide take their defaults.
        PlayerSettings d = PlayerSettings.Default;
        Assert.Equal((d.SubtitlesEnabled, d.SubtitleAutoload, d.SubtitleColor, d.ShowControlsInFullscreen, d.KeepOnTop, d.FolderOpening),
            (current.SubtitlesEnabled, current.SubtitleAutoload, current.SubtitleColor, current.ShowControlsInFullscreen, current.KeepOnTop, current.FolderOpening));
        Assert.Contains("\"Version\": 8", storage.Content);
        Assert.Contains("\"AutoHideControls\": false", storage.Content);

        // A version 8 document is read as it is: Reduce motion and auto-hide are separate.
        MemoryStorage version8 = new() { Content = "{\"Version\":8,\"ReduceMotion\":true,\"AutoHideControls\":true}" };
        PlayerSettingsService reloaded = new(version8);
        reloaded.Load();
        Assert.Equal((true, true), (reloaded.Current.ReduceMotion, reloaded.Current.AutoHideControls));
        Assert.Equal("{\"Version\":8,\"ReduceMotion\":true,\"AutoHideControls\":true}", version8.Content);
    }

    [Fact]
    public void OlderReduceMotionDocumentsKeepTheControlsUpToo()
    {
        MemoryStorage storage = new() { Content = "{\"Version\":4,\"Theme\":\"Dark\",\"ReduceMotion\":true}" };
        PlayerSettingsService service = new(storage);
        service.Load();
        Assert.False(service.Current.AutoHideControls);

        MemoryStorage without = new() { Content = "{\"Version\":6,\"Theme\":\"Dark\"}" };
        PlayerSettingsService other = new(without);
        other.Load();
        Assert.True(other.Current.AutoHideControls);
    }

    [Fact]
    public void Version8ValuesRoundTripAndInvalidOnesFallBack()
    {
        MemoryStorage storage = new();
        new PlayerSettingsService(storage).Update(s => s with
        {
            SubtitlesEnabled = false, SubtitleEncoding = "Klingon", SubtitleMatching = 9, SubtitleFont = "  Segoe UI	 ", SubtitleTextScale = 20,
            SubtitleColor = "ffcc00", SubtitleOpacity = 140, SubtitleOutline = "Huge", SubtitleOutlineColor = "red", SubtitleShadowDistance = "Large",
            SubtitleBackgroundOpacity = -4, SubtitleMargin = 5000, SubtitleDelay = 1234, FileNameSeconds = 0, ShortcutFeedbackSeconds = 1.3,
            VeryShortSkipSeconds = 0, LongSkipSeconds = 99999, ClickAction = "Triple", WheelAction = "Seek", HorizontalWheelAction = "Zoom",
            AutoHideControls = false, KeepOnTop = true, FolderOpening = "Recursive", ShowHiddenFiles = true,
        });
        PlayerSettingsService reloaded = new(storage);

        reloaded.Load();

        PlayerSettings current = reloaded.Current;
        Assert.Equal((false, "", 4, "Segoe UI", 50), (current.SubtitlesEnabled, current.SubtitleEncoding, current.SubtitleMatching, current.SubtitleFont, current.SubtitleTextScale));
        Assert.Equal(("#FFCC00", 100, "Normal", "#000000", "Large"), (current.SubtitleColor, current.SubtitleOpacity, current.SubtitleOutline, current.SubtitleOutlineColor, current.SubtitleShadowDistance));
        Assert.Equal((0, SubtitleEngineOptions.MaximumMargin, 1200, 1, 1.5), (current.SubtitleBackgroundOpacity, current.SubtitleMargin, current.SubtitleDelay, current.FileNameSeconds, current.ShortcutFeedbackSeconds));
        Assert.Equal((1, InterfaceOptions.MaximumSkipSeconds, "None", "Seek", "Seek"), (current.VeryShortSkipSeconds, current.LongSkipSeconds, current.ClickAction, current.WheelAction, current.HorizontalWheelAction));
        Assert.Equal((false, true, "Subfolders", true), (current.AutoHideControls, current.KeepOnTop, current.FolderOpening, current.ShowHiddenFiles));
    }

    [Fact]
    public void Phase9dSettingsApplyLiveAtTheNextOpenOrAtTheNextStart()
    {
        foreach (string key in SubtitleEngineOptions.LiveKeys.Concat(InterfaceOptions.LiveKeys))
            Assert.Equal(SettingApplyMode.Live, PlayerSettingsService.ApplyMode(key));
        foreach (string key in SubtitleEngineOptions.NextOpenKeys.Concat(InterfaceOptions.NextOpenKeys))
            Assert.Equal(SettingApplyMode.NextOpen, PlayerSettingsService.ApplyMode(key));
        // The window opens at its remembered size, or not, when the app starts.
        Assert.Equal(SettingApplyMode.Restart, PlayerSettingsService.ApplyMode(nameof(PlayerSettings.RememberWindowGeometry)));
        Assert.All(SubtitleEngineOptions.LiveKeys.Concat(SubtitleEngineOptions.NextOpenKeys).Concat(InterfaceOptions.LiveKeys).Concat(InterfaceOptions.NextOpenKeys),
            key => Assert.NotNull(typeof(PlayerSettings).GetProperty(key)));
        Assert.Empty(SubtitleEngineOptions.LiveKeys.Intersect(SubtitleEngineOptions.NextOpenKeys));
    }

    private sealed class MemoryStorage : IPlayerSettingsStorage
    { public string? Content; public string? Preserved; public bool ThrowOnWrite; public string? Read() => Content; public void WriteAtomically(string content) { if (ThrowOnWrite) throw new IOException(); Content = content; } public void PreserveInvalid(string content) => Preserved = content; }
}

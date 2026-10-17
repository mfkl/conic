using System.Runtime.InteropServices;
using Conic.Interop.MacOS;
using Conic.Services;
using Conic.ViewModels;
using Player.Application;
using Player.Contracts;
using Xunit;

namespace Conic.UITests;

public sealed class MacOSFactAttribute : FactAttribute
{
    public MacOSFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Needs macOS.";
    }
}

public sealed unsafe class MacNowPlayingTests
{
    [MacOSFact]
    public void NowPlayingShowsWhatPlaysAndRemoteCommandsControlIt()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Transport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { });
        using MediaSession session = new(vm);
        MacNowPlaying? nowPlaying = MacNowPlaying.Create(session, action => action());
        Assert.NotNull(nowPlaying);
        session.Attach(nowPlaying);
        vm.ApplyEngineState(new(1, PlaybackStatus.Playing, "sintel.mkv", 0.25, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), 0.5, true, null, Title: "Sintel"));
        Assert.Equal("Sintel", nowPlaying.NowPlayingTitleForTests());
        Assert.Equal(100, nowPlaying.NowPlayingDurationForTests());

        nint send = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");
        nint pause = sel_registerName("conicPause:");
        nint status = ((delegate* unmanaged<nint, nint, nint, nint>)send)(nowPlaying.TargetForTests, pause, 0);
        Assert.Equal(0, status);
        Assert.Equal(1, transport.Toggles);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    private sealed class NoPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class Transport : IPlayerTransport
    {
        public int Toggles { get; private set; }
        public void Open(string source) { }
        public void TogglePause() => Toggles++;
        public void Stop() { }
        public void Seek(double normalizedPosition) { }
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
    }
}

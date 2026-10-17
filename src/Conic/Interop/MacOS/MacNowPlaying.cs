using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Conic.Services;

namespace Conic.Interop.MacOS;

[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacNowPlaying : IMediaSessionBackend
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string MediaPlayerFramework = "/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer";
    private const string TargetClassName = "ConicRemoteCommandTarget";
    private const nint Success = 0, NoActionableNowPlayingItem = 110;
    private const nint StatePlaying = 1, StatePaused = 2, StateStopped = 3;
    private const nuint MediaTypeVideo = 2;

    private static readonly nint MessageSend = NativeLibrary.GetExport(NativeLibrary.Load(ObjectiveCLibrary), "objc_msgSend");
    private static MacNowPlaying? s_current;

    private readonly MediaSession _session;
    private readonly Action<Action> _post;
    private readonly nint _center;
    private readonly nint _target;
    private readonly (nint Command, nint Action)[] _commands;
    private readonly nint _play, _pause, _toggle, _stop, _next, _previous, _seek;
    private readonly nint _titleKey, _albumKey, _durationKey, _elapsedKey, _rateKey, _mediaTypeKey;

    private MacNowPlaying(nint mediaPlayer, MediaSession session, Action<Action> post)
    {
        _session = session;
        _post = post;
        _titleKey = Constant(mediaPlayer, "MPMediaItemPropertyTitle");
        _albumKey = Constant(mediaPlayer, "MPMediaItemPropertyAlbumTitle");
        _durationKey = Constant(mediaPlayer, "MPMediaItemPropertyPlaybackDuration");
        _elapsedKey = Constant(mediaPlayer, "MPNowPlayingInfoPropertyElapsedPlaybackTime");
        _rateKey = Constant(mediaPlayer, "MPNowPlayingInfoPropertyPlaybackRate");
        _mediaTypeKey = Constant(mediaPlayer, "MPNowPlayingInfoPropertyMediaType");
        _center = Send(objc_getClass("MPNowPlayingInfoCenter"), Selector("defaultCenter"));
        nint commands = Send(objc_getClass("MPRemoteCommandCenter"), Selector("sharedCommandCenter"));
        _target = Send(Send(TargetClass(), Selector("alloc")), Selector("init"));
        _play = Send(commands, Selector("playCommand"));
        _pause = Send(commands, Selector("pauseCommand"));
        _toggle = Send(commands, Selector("togglePlayPauseCommand"));
        _stop = Send(commands, Selector("stopCommand"));
        _next = Send(commands, Selector("nextTrackCommand"));
        _previous = Send(commands, Selector("previousTrackCommand"));
        _seek = Send(commands, Selector("changePlaybackPositionCommand"));
        _commands =
        [
            (_play, Selector("conicPlay:")),
            (_pause, Selector("conicPause:")),
            (_toggle, Selector("conicToggle:")),
            (_stop, Selector("conicStop:")),
            (_next, Selector("conicNext:")),
            (_previous, Selector("conicPrevious:")),
            (_seek, Selector("conicSeek:")),
        ];
        nint addTarget = Selector("addTarget:action:");
        foreach ((nint command, nint action) in _commands)
            if (command != 0) ((delegate* unmanaged<nint, nint, nint, nint, nint>)MessageSend)(command, addTarget, _target, action);
        s_current = this;
    }

    internal nint TargetForTests => _target;

    public static MacNowPlaying? Create(MediaSession session, Action<Action>? post = null)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 12, 2) || s_current is not null) return null;
        if (!NativeLibrary.TryLoad(MediaPlayerFramework, out nint mediaPlayer)) return null;
        nint pool = objc_autoreleasePoolPush();
        try { return new MacNowPlaying(mediaPlayer, session, post ?? (action => Dispatcher.UIThread.Post(action))); }
        catch (EntryPointNotFoundException) { return null; }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public void Update(MediaSessionState state)
    {
        nint pool = objc_autoreleasePoolPush();
        try
        {
            SetEnabled(_play, state.HasMedia);
            SetEnabled(_pause, state.HasMedia);
            SetEnabled(_toggle, state.HasMedia);
            SetEnabled(_stop, state.HasMedia);
            SetEnabled(_next, state.CanNext);
            SetEnabled(_previous, state.CanPrevious);
            SetEnabled(_seek, state.CanSeek);
            if (!state.HasMedia)
            {
                Send(_center, Selector("setNowPlayingInfo:"), 0);
                SetPlaybackState(StateStopped);
                return;
            }
            nint info = Send(objc_getClass("NSMutableDictionary"), Selector("dictionary"));
            Put(info, _titleKey, String(state.Title));
            if (state.Subtitle.Length != 0) Put(info, _albumKey, String(state.Subtitle));
            if (state.Duration > TimeSpan.Zero) Put(info, _durationKey, Number(state.Duration.TotalSeconds));
            Put(info, _elapsedKey, Number(state.Position.TotalSeconds));
            Put(info, _rateKey, Number(state.Status == MediaSessionStatus.Playing ? state.Rate : 0));
            Put(info, _mediaTypeKey, ((delegate* unmanaged<nint, nint, nuint, nint>)MessageSend)(objc_getClass("NSNumber"), Selector("numberWithUnsignedInteger:"), MediaTypeVideo));
            Send(_center, Selector("setNowPlayingInfo:"), info);
            SetPlaybackState(state.Status switch
            {
                MediaSessionStatus.Playing or MediaSessionStatus.Opening => StatePlaying,
                MediaSessionStatus.Paused => StatePaused,
                _ => StateStopped,
            });
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public void Dispose()
    {
        if (s_current != this) return;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            nint removeTarget = Selector("removeTarget:");
            foreach ((nint command, _) in _commands)
                if (command != 0) Send(command, removeTarget, _target);
            Send(_center, Selector("setNowPlayingInfo:"), 0);
            SetPlaybackState(StateStopped);
            Send(_target, Selector("release"));
        }
        finally { objc_autoreleasePoolPop(pool); }
        s_current = null;
    }

    internal string? NowPlayingTitleForTests()
    {
        nint info = Send(_center, Selector("nowPlayingInfo"));
        nint title = info == 0 ? 0 : Send(info, Selector("objectForKey:"), _titleKey);
        return title == 0 ? null : Marshal.PtrToStringUTF8(Send(title, Selector("UTF8String")));
    }

    internal double? NowPlayingDurationForTests()
    {
        nint info = Send(_center, Selector("nowPlayingInfo"));
        nint duration = info == 0 ? 0 : Send(info, Selector("objectForKey:"), _durationKey);
        return duration == 0 ? null : ((delegate* unmanaged<nint, nint, double>)MessageSend)(duration, Selector("doubleValue"));
    }

    private void SetEnabled(nint command, bool enabled)
    {
        if (command != 0) ((delegate* unmanaged<nint, nint, byte, void>)MessageSend)(command, Selector("setEnabled:"), enabled ? (byte)1 : (byte)0);
    }

    private void SetPlaybackState(nint state) =>
        ((delegate* unmanaged<nint, nint, nint, void>)MessageSend)(_center, Selector("setPlaybackState:"), state);

    private static void Put(nint dictionary, nint key, nint value)
    {
        if (key != 0 && value != 0) ((delegate* unmanaged<nint, nint, nint, nint, void>)MessageSend)(dictionary, Selector("setObject:forKey:"), value, key);
    }

    private static nint String(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), Selector("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static nint Number(double value) =>
        ((delegate* unmanaged<nint, nint, double, nint>)MessageSend)(objc_getClass("NSNumber"), Selector("numberWithDouble:"), value);

    private static nint Constant(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);

    private static nint Selector(string name) => sel_registerName(name);

    private static nint Send(nint target, nint selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MessageSend)(target, selector);

    private static nint Send(nint target, nint selector, nint argument) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MessageSend)(target, selector, argument);

    private static nint TargetClass()
    {
        nint existing = objc_getClass(TargetClassName);
        if (existing != 0) return existing;
        nint created = objc_allocateClassPair(objc_getClass("NSObject"), TargetClassName, 0);
        AddHandler(created, "conicPlay:", (delegate* unmanaged<nint, nint, nint, nint>)&OnPlay);
        AddHandler(created, "conicPause:", (delegate* unmanaged<nint, nint, nint, nint>)&OnPause);
        AddHandler(created, "conicToggle:", (delegate* unmanaged<nint, nint, nint, nint>)&OnToggle);
        AddHandler(created, "conicStop:", (delegate* unmanaged<nint, nint, nint, nint>)&OnStop);
        AddHandler(created, "conicNext:", (delegate* unmanaged<nint, nint, nint, nint>)&OnNext);
        AddHandler(created, "conicPrevious:", (delegate* unmanaged<nint, nint, nint, nint>)&OnPrevious);
        AddHandler(created, "conicSeek:", (delegate* unmanaged<nint, nint, nint, nint>)&OnSeek);
        objc_registerClassPair(created);
        return created;
    }

    private static void AddHandler(nint type, string selector, delegate* unmanaged<nint, nint, nint, nint> handler) =>
        class_addMethod(type, Selector(selector), (nint)handler, "q@:@");

    private static nint Press(MediaSessionButton button)
    {
        if (s_current is not { } current) return NoActionableNowPlayingItem;
        current._post(() => current._session.Press(button));
        return Success;
    }

    [UnmanagedCallersOnly]
    private static nint OnPlay(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.Play);

    [UnmanagedCallersOnly]
    private static nint OnPause(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.Pause);

    [UnmanagedCallersOnly]
    private static nint OnToggle(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.TogglePause);

    [UnmanagedCallersOnly]
    private static nint OnStop(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.Stop);

    [UnmanagedCallersOnly]
    private static nint OnNext(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.Next);

    [UnmanagedCallersOnly]
    private static nint OnPrevious(nint self, nint selector, nint commandEvent) => Press(MediaSessionButton.Previous);

    [UnmanagedCallersOnly]
    private static nint OnSeek(nint self, nint selector, nint commandEvent)
    {
        if (s_current is not { } current || commandEvent == 0) return NoActionableNowPlayingItem;
        double seconds = ((delegate* unmanaged<nint, nint, double>)MessageSend)(commandEvent, Selector("positionTime"));
        current._post(() => current._session.SeekTo(TimeSpan.FromSeconds(Math.Max(0, seconds))));
        return Success;
    }

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(ObjectiveCLibrary)]
    private static partial void objc_registerClassPair(nint type);

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(nint type, nint selector, nint implementation, string types);

    [LibraryImport(ObjectiveCLibrary)]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport(ObjectiveCLibrary)]
    private static partial void objc_autoreleasePoolPop(nint pool);
}

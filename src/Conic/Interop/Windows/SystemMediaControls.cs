using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Conic.Services;

namespace Conic.Interop.Windows;

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class SystemMediaControls : IMediaSessionBackend
{
    private static readonly Guid InteropId = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid ControlsId = new("99fa3ff4-1742-42a6-902e-087d41f965ec");
    private static readonly Guid Controls2Id = new("ea98d2f6-7f3c-4af2-a586-72889808efb1");
    private static readonly Guid TimelineId = new("5125316a-c3a2-475b-8507-93534dc88f15");
    private static readonly Guid ButtonPressedId = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
    private static readonly Guid PositionRequestedId = new("44e34f15-bdc0-50a7-ace4-39e91fb753f1");
    private static readonly Guid UnknownId = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid AgileId = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    private static readonly void** HandlerVtbl = CreateHandlerVtbl();

    private const int StatusClosed = 0, StatusChanging = 1, StatusStopped = 2, StatusPlaying = 3, StatusPaused = 4;
    private const int TypeVideo = 2;

    private readonly MediaSession _session;
    private readonly GCHandle _self;
    private void* _controls;
    private void* _controls2;
    private Handler* _buttonHandler;
    private Handler* _positionHandler;
    private long _buttonToken;
    private long _positionToken;
    private MediaSessionState? _shown;

    private SystemMediaControls(void* controls, MediaSession session)
    {
        _controls = controls;
        _session = session;
        _self = GCHandle.Alloc(this);
        void* controls2 = null;
        Guid controls2Id = Controls2Id;
        if (QueryInterface(controls, &controls2Id, &controls2) >= 0) _controls2 = controls2;
        _buttonHandler = CreateHandler(ButtonPressedId, HandlerKind.Button);
        long token;
        if (((delegate* unmanaged<void*, void*, long*, int>)Vtbl(_controls)[32])(_controls, _buttonHandler, &token) >= 0) _buttonToken = token;
        if (_controls2 != null)
        {
            _positionHandler = CreateHandler(PositionRequestedId, HandlerKind.Position);
            if (((delegate* unmanaged<void*, void*, long*, int>)Vtbl(_controls2)[13])(_controls2, _positionHandler, &token) >= 0) _positionToken = token;
        }
    }

    public static SystemMediaControls? Create(nint window, MediaSession session)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240)) return null;
        nint name = 0;
        void* interop = null;
        try
        {
            name = CreateString("Windows.Media.SystemMediaTransportControls");
            Guid interopId = InteropId;
            if (RoGetActivationFactory(name, &interopId, &interop) < 0) return null;
            Guid controlsId = ControlsId;
            void* controls = null;
            if (((delegate* unmanaged<void*, nint, Guid*, void**, int>)Vtbl(interop)[6])(interop, window, &controlsId, &controls) < 0 || controls == null) return null;
            return new SystemMediaControls(controls, session);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { return null; }
        finally
        {
            if (interop != null) Release(interop);
            DeleteString(name);
        }
    }

    public void Update(MediaSessionState state)
    {
        if (_controls == null) return;
        SetFlag(11, state.HasMedia);
        if (!state.HasMedia)
        {
            SetStatus(StatusClosed);
            if (_shown?.HasMedia == true) ClearDisplay();
            _shown = state;
            return;
        }
        SetFlag(13, true);
        SetFlag(17, true);
        SetFlag(15, state.Status is MediaSessionStatus.Playing or MediaSessionStatus.Paused or MediaSessionStatus.Opening);
        SetFlag(25, state.CanPrevious);
        SetFlag(27, state.CanNext);
        SetStatus(state.Status switch
        {
            MediaSessionStatus.Opening => StatusChanging,
            MediaSessionStatus.Playing => StatusPlaying,
            MediaSessionStatus.Paused => StatusPaused,
            _ => StatusStopped,
        });
        if (_shown is null || _shown.Title != state.Title || _shown.Subtitle != state.Subtitle) ShowDisplay(state.Title, state.Subtitle);
        UpdateTimeline(state);
        _shown = state;
    }

    public void Dispose()
    {
        if (_controls == null) return;
        if (_buttonHandler != null)
        {
            ((delegate* unmanaged<void*, long, int>)Vtbl(_controls)[33])(_controls, _buttonToken);
            _buttonHandler->Owner = 0;
            ReleaseHandler(_buttonHandler);
            _buttonHandler = null;
        }
        if (_positionHandler != null)
        {
            ((delegate* unmanaged<void*, long, int>)Vtbl(_controls2)[14])(_controls2, _positionToken);
            _positionHandler->Owner = 0;
            ReleaseHandler(_positionHandler);
            _positionHandler = null;
        }
        SetFlag(11, false);
        if (_controls2 != null) Release(_controls2);
        Release(_controls);
        _controls2 = null;
        _controls = null;
        _self.Free();
    }

    private void SetFlag(int slot, bool value) =>
        ((delegate* unmanaged<void*, byte, int>)Vtbl(_controls)[slot])(_controls, value ? (byte)1 : (byte)0);

    private void SetStatus(int status) =>
        ((delegate* unmanaged<void*, int, int>)Vtbl(_controls)[7])(_controls, status);

    private void ShowDisplay(string title, string subtitle)
    {
        void* updater = null;
        if (((delegate* unmanaged<void*, void**, int>)Vtbl(_controls)[8])(_controls, &updater) < 0 || updater == null) return;
        try
        {
            ((delegate* unmanaged<void*, int, int>)Vtbl(updater)[7])(updater, TypeVideo);
            void* video = null;
            if (((delegate* unmanaged<void*, void**, int>)Vtbl(updater)[13])(updater, &video) >= 0 && video != null)
            {
                SetString(video, 7, title);
                SetString(video, 9, subtitle);
                Release(video);
            }
            ((delegate* unmanaged<void*, int>)Vtbl(updater)[17])(updater);
        }
        finally { Release(updater); }
    }

    private void ClearDisplay()
    {
        void* updater = null;
        if (((delegate* unmanaged<void*, void**, int>)Vtbl(_controls)[8])(_controls, &updater) < 0 || updater == null) return;
        ((delegate* unmanaged<void*, int>)Vtbl(updater)[16])(updater);
        ((delegate* unmanaged<void*, int>)Vtbl(updater)[17])(updater);
        Release(updater);
    }

    private void UpdateTimeline(MediaSessionState state)
    {
        if (_controls2 == null) return;
        ((delegate* unmanaged<void*, double, int>)Vtbl(_controls2)[11])(_controls2, state.Rate);
        nint name = CreateString("Windows.Media.SystemMediaTransportControlsTimelineProperties");
        void* instance = null, timeline = null;
        try
        {
            if (RoActivateInstance(name, &instance) < 0 || instance == null) return;
            Guid timelineId = TimelineId;
            if (QueryInterface(instance, &timelineId, &timeline) < 0 || timeline == null) return;
            long end = state.Duration.Ticks;
            SetTime(timeline, 7, 0);
            SetTime(timeline, 9, end);
            SetTime(timeline, 11, 0);
            SetTime(timeline, 13, state.CanSeek ? end : 0);
            SetTime(timeline, 15, Math.Min(state.Position.Ticks, end));
            ((delegate* unmanaged<void*, void*, int>)Vtbl(_controls2)[12])(_controls2, timeline);
        }
        finally
        {
            if (timeline != null) Release(timeline);
            if (instance != null) Release(instance);
            DeleteString(name);
        }
    }

    private void OnButton(int button)
    {
        MediaSessionButton? pressed = button switch
        {
            0 => MediaSessionButton.Play,
            1 => MediaSessionButton.Pause,
            2 => MediaSessionButton.Stop,
            6 => MediaSessionButton.Next,
            7 => MediaSessionButton.Previous,
            _ => null,
        };
        if (pressed is { } value) Dispatcher.UIThread.Post(() => _session.Press(value));
    }

    private void OnSeek(long ticks) => Dispatcher.UIThread.Post(() => _session.SeekTo(TimeSpan.FromTicks(ticks)));

    private static void SetTime(void* timeline, int slot, long ticks) =>
        ((delegate* unmanaged<void*, long, int>)Vtbl(timeline)[slot])(timeline, ticks);

    private static void SetString(void* target, int slot, string value)
    {
        nint text = CreateString(value);
        try { ((delegate* unmanaged<void*, nint, int>)Vtbl(target)[slot])(target, text); }
        finally { DeleteString(text); }
    }

    private static void** Vtbl(void* instance) => *(void***)instance;

    private static int QueryInterface(void* instance, Guid* iid, void** result) =>
        ((delegate* unmanaged<void*, Guid*, void**, int>)Vtbl(instance)[0])(instance, iid, result);

    private static void Release(void* instance) =>
        ((delegate* unmanaged<void*, uint>)Vtbl(instance)[2])(instance);

    private static nint CreateString(string value)
    {
        nint text = 0;
        fixed (char* chars = value)
            if (WindowsCreateString(chars, (uint)value.Length, &text) < 0) return 0;
        return text;
    }

    private static void DeleteString(nint text)
    {
        if (text != 0) WindowsDeleteString(text);
    }

    private enum HandlerKind { Button, Position }

    [StructLayout(LayoutKind.Sequential)]
    private struct Handler
    {
        public void** Vtbl;
        public int References;
        public HandlerKind Kind;
        public Guid Iid;
        public nint Owner;
    }

    private Handler* CreateHandler(Guid iid, HandlerKind kind)
    {
        Handler* handler = (Handler*)NativeMemory.AllocZeroed((nuint)sizeof(Handler));
        handler->Vtbl = HandlerVtbl;
        handler->References = 1;
        handler->Kind = kind;
        handler->Iid = iid;
        handler->Owner = GCHandle.ToIntPtr(_self);
        return handler;
    }

    private static void** CreateHandlerVtbl()
    {
        void** vtbl = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        vtbl[0] = (delegate* unmanaged<Handler*, Guid*, void**, int>)&HandlerQueryInterface;
        vtbl[1] = (delegate* unmanaged<Handler*, uint>)&HandlerAddRef;
        vtbl[2] = (delegate* unmanaged<Handler*, uint>)&HandlerRelease;
        vtbl[3] = (delegate* unmanaged<Handler*, void*, void*, int>)&HandlerInvoke;
        return vtbl;
    }

    [UnmanagedCallersOnly]
    private static int HandlerQueryInterface(Handler* handler, Guid* iid, void** result)
    {
        if (*iid == handler->Iid || *iid == UnknownId || *iid == AgileId)
        {
            Interlocked.Increment(ref handler->References);
            *result = handler;
            return 0;
        }
        *result = null;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly]
    private static uint HandlerAddRef(Handler* handler) => (uint)Interlocked.Increment(ref handler->References);

    [UnmanagedCallersOnly]
    private static uint HandlerRelease(Handler* handler) => ReleaseHandler(handler);

    private static uint ReleaseHandler(Handler* handler)
    {
        int references = Interlocked.Decrement(ref handler->References);
        if (references == 0) NativeMemory.Free(handler);
        return (uint)references;
    }

    [UnmanagedCallersOnly]
    private static int HandlerInvoke(Handler* handler, void* sender, void* args)
    {
        try
        {
            nint owner = handler->Owner;
            if (owner == 0 || args == null || GCHandle.FromIntPtr(owner).Target is not SystemMediaControls controls) return 0;
            if (handler->Kind == HandlerKind.Button)
            {
                int button;
                if (((delegate* unmanaged<void*, int*, int>)Vtbl(args)[6])(args, &button) >= 0) controls.OnButton(button);
            }
            else
            {
                long ticks;
                if (((delegate* unmanaged<void*, long*, int>)Vtbl(args)[6])(args, &ticks) >= 0) controls.OnSeek(ticks);
            }
        }
        catch (Exception) { }
        return 0;
    }

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, void** factory);

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint activatableClassId, void** instance);

    [LibraryImport("combase.dll")]
    private static partial int WindowsCreateString(char* source, uint length, nint* text);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint text);
}

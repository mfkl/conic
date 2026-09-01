using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Player.Contracts;
using Player.Domain;
using VLCLR;
using VLCLR.Native;

namespace Conic.Interop;

internal sealed unsafe class VLCCorePlayer : IMediaEngine
{
    private const int PlaylistStopped = 0;
    private const int PlaylistRunning = 1;
    private const int PlaylistPaused = 2;
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly MediaEngineStateStore _states = new();
    private readonly CompositionPluginBridge _playlistBridge;
    private readonly bool _ownsPlaylistBridge;
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private nint _playlist;
    private long _generation;
    private int _disposed;

    public VLCCorePlayer()
    {
        _playlistBridge = new CompositionPluginBridge(
            registerFrameCallback: false);
        _ownsPlaylistBridge = true;
        _playlistBridge.ConnectPlaylist();
    }

    public MediaEngineState State => _states.State;

    public event EventHandler<MediaEngineState>? StateChanged
    {
        add => _states.Changed += value;
        remove => _states.Changed -= value;
    }

    public void Open(string source)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Stop();
        lock (_gate)
        {
            if (_thread is not null)
                throw new InvalidOperationException(
                    "The previous VLC playback thread did not stop.");
        }

        string fullPath = Path.GetFullPath(source);
        long generation = _states.BeginOpen(fullPath);
        ManualResetEventSlim stop = new(false);
        Thread thread = new(() => Run(fullPath, generation, stop))
        {
            IsBackground = true,
            Name = "VLCLR composition playback",
        };

        lock (_gate)
        {
            _generation = generation;
            _stop = stop;
            _thread = thread;
        }
        thread.Start();
    }

    public void TogglePause()
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            VLC3PlaylistControl.TogglePause(_playlist);
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void Seek(double normalizedPosition)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            nint input = VLCCore.PlaylistCurrentInput(_playlist);
            if (input == 0)
                return;
            try
            {
                int result = VLC3PlaylistControl.SetInputPosition(
                    input,
                    Math.Clamp(normalizedPosition, 0, 1));
                if (result != 0)
                    throw new InvalidOperationException(
                        $"input_Control(INPUT_SET_POSITION) failed with " +
                        $"{result}.");
            }
            finally
            {
                VLCCore.ObjectRelease(input);
            }
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    public void SetVolume(double normalizedVolume)
    {
        ThrowIfDisposed();
        long generation;
        lock (_gate)
        {
            if (_playlist == 0)
                return;
            VLCCore.PlaylistVolumeSet(
                _playlist,
                (float)Math.Clamp(normalizedVolume, 0, 1.25));
            generation = _generation;
        }
        PublishSnapshot(generation);
    }

    private void Run(
        string path,
        long generation,
        ManualResetEventSlim stop)
    {
        string[] arguments =
        [
            "conic",
            "--intf=vlc_avalonia_control",
            "--no-video-title-show",
            "--vout=vlc_avalonia",
            "--avcodec-hw=any",
            path,
        ];

        nint[] strings = new nint[arguments.Length];
        nint argv = 0;
        nint core = 0;
        nint playlist = 0;
        bool initialized = false;
        bool faulted = false;
        try
        {
            _playlistBridge.ResetPlaylist();
            for (int index = 0; index < arguments.Length; index++)
                strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);

            argv = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            for (int index = 0; index < strings.Length; index++)
                Marshal.WriteIntPtr(argv, index * IntPtr.Size, strings[index]);

            core = VLCCore.libvlc_InternalCreate();
            if (core == 0)
                throw new InvalidOperationException(
                    "libvlc_InternalCreate failed.");

            int result = VLCCore.libvlc_InternalInit(
                core,
                arguments.Length,
                argv);
            if (result != 0)
                throw new InvalidOperationException(
                    $"libvlc_InternalInit failed with {result}.");
            initialized = true;

            result = VLC3PlaylistControl.AddInterface(
                core,
                "vlc_avalonia_control");
            if (result != 0)
                throw new InvalidOperationException(
                    "libvlc_InternalAddIntf could not start interface " +
                    $"vlc_avalonia_control (result {result}).");

            playlist = _playlistBridge.WaitForPlaylist(
                PlaylistTimeout,
                stop);

            lock (_gate)
            {
                if (_generation == generation)
                    _playlist = playlist;
            }

            VLCCore.libvlc_InternalPlay(core);
            PublishSnapshot(generation);
            while (!stop.Wait(TimeSpan.FromMilliseconds(200)))
                PublishSnapshot(generation);
        }
        catch (Exception exception)
        {
            faulted = true;
            _states.Fault(generation, exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (_generation == generation && _playlist == playlist)
                    _playlist = 0;
            }

            // The interface callback lends the playlist for the interface/core
            // lifetime. It must be forgotten before core cleanup and is never
            // released independently.
            _playlistBridge.ResetPlaylist();
            playlist = 0;
            if (initialized)
                VLCCore.libvlc_InternalCleanup(core);
            if (core != 0)
                VLCCore.libvlc_InternalDestroy(core);
            if (argv != 0)
                Marshal.FreeHGlobal(argv);
            foreach (nint value in strings)
            {
                if (value != 0)
                    Marshal.FreeCoTaskMem(value);
            }

            if (!faulted)
                _states.Stop(generation);
        }
    }

    private void PublishSnapshot(long generation)
    {
        PlaybackStatus status;
        double position;
        long elapsed;
        long duration;
        bool canSeek;
        double volume;
        lock (_gate)
        {
            if (_playlist == 0 || _generation != generation)
                return;

            status = VLCCore.PlaylistStatus(_playlist) switch
            {
                PlaylistRunning => PlaybackStatus.Playing,
                PlaylistPaused => PlaybackStatus.Paused,
                PlaylistStopped => PlaybackStatus.Stopped,
                _ => PlaybackStatus.Opening,
            };

            position = 0;
            elapsed = 0;
            duration = 0;
            canSeek = false;
            nint input = VLCCore.PlaylistCurrentInput(_playlist);
            if (input != 0)
            {
                try
                {
                    VLCVariable variables = new(input);
                    position = variables.GetFloat("position");
                    elapsed = variables.GetInteger("time");
                    duration = variables.GetInteger("length");
                    canSeek = variables.GetBool("can-seek");
                }
                finally
                {
                    VLCCore.ObjectRelease(input);
                }
            }
            volume = VLCCore.PlaylistVolumeGet(_playlist);
        }

        _states.Update(
            generation,
            status,
            position,
            TimeSpan.FromMicroseconds(Math.Max(0, elapsed)),
            TimeSpan.FromMicroseconds(Math.Max(0, duration)),
            volume,
            canSeek);
    }

    public void Stop()
    {
        Thread? thread;
        ManualResetEventSlim? stop;
        long generation;
        lock (_gate)
        {
            thread = _thread;
            stop = _stop;
            generation = _generation;
            if (_playlist != 0)
                VLC3PlaylistControl.Stop(_playlist);
            stop?.Set();
        }

        if (thread is not null && thread != Thread.CurrentThread &&
            !thread.Join(ShutdownTimeout))
        {
            const string message =
                "VLC playback did not stop within 10 seconds.";
            if (generation != 0)
                _states.Fault(generation, message);
            throw new TimeoutException(message);
        }

        lock (_gate)
        {
            if (_thread == thread)
            {
                _thread = null;
                _stop = null;
            }
        }
        if (thread != Thread.CurrentThread)
            stop?.Dispose();
        if (generation != 0)
            _states.Stop(generation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Stop();
        _playlistBridge.DisconnectPlaylist();
        if (_ownsPlaylistBridge)
            _playlistBridge.Dispose();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    private static class VLC3PlaylistControl
    {
        private const int TogglePauseQuery = 2;
        private const int StopQuery = 3;
        private const int InputSetPositionQuery = 1;
        private static readonly nint Library =
            NativeLibrary.Load("libvlccore.dll");
        private static readonly delegate* unmanaged[Cdecl]<nint, int, int, void>
            Control =
                (delegate* unmanaged[Cdecl]<nint, int, int, void>)
                NativeLibrary.GetExport(Library, "playlist_Control");
        private static readonly delegate* unmanaged[Cdecl]<nint, byte*, int>
            AddIntf =
                (delegate* unmanaged[Cdecl]<nint, byte*, int>)
                NativeLibrary.GetExport(Library, "libvlc_InternalAddIntf");
        private static readonly delegate* unmanaged[Cdecl]<nint, int, nint, int>
            InputVaControl =
                (delegate* unmanaged[Cdecl]<nint, int, nint, int>)
                NativeLibrary.GetExport(Library, "input_vaControl");

        public static int AddInterface(nint core, string name)
        {
            nint pointer = Marshal.StringToCoTaskMemUTF8(name);
            try
            {
                return AddIntf(core, (byte*)pointer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        public static void TogglePause(nint playlist) =>
            Control(playlist, TogglePauseQuery, 0);

        public static int SetInputPosition(nint input, double position)
        {
            // On the pinned Win64 VLC 3 ABI, va_list is a pointer into the
            // argument save area. Calling exported input_vaControl with a
            // single double avoids pretending variadic input_Control is a
            // fixed-signature P/Invoke.
            double argument = position;
            return InputVaControl(
                input,
                InputSetPositionQuery,
                (nint)(void*)(&argument));
        }

        public static void Stop(nint playlist) =>
            Control(playlist, StopQuery, 0);
    }
}

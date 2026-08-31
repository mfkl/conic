using System.Runtime.InteropServices;
using VLCLR.Native;

namespace Conic.Interop;

internal sealed class VLCCorePlayer : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private bool _disposed;

    public event Action<string>? StatusChanged;

    public void Play(string path)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VLCCorePlayer));
        Stop();

        ManualResetEventSlim stop = new(false);
        Thread thread = new(() => Run(path, stop))
        {
            IsBackground = true,
            Name = "VLCLR composition playback",
        };

        lock (_gate)
        {
            _stop = stop;
            _thread = thread;
        }
        thread.Start();
    }

    private void Run(string path, ManualResetEventSlim stop)
    {
        string[] arguments =
        [
            "vlclr-avalonia",
            "--intf=dummy",
            "--no-video-title-show",
            "--vout=vlc_avalonia",
            "--avcodec-hw=none",
            Path.GetFullPath(path),
        ];

        nint[] strings = new nint[arguments.Length];
        nint argv = 0;
        nint core = 0;
        bool initialized = false;
        try
        {
            for (int index = 0; index < arguments.Length; index++)
                strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);

            argv = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            for (int index = 0; index < strings.Length; index++)
                Marshal.WriteIntPtr(argv, index * IntPtr.Size, strings[index]);

            core = VLCCore.libvlc_InternalCreate();
            if (core == 0)
                throw new InvalidOperationException("libvlc_InternalCreate failed.");

            int result = VLCCore.libvlc_InternalInit(
                core,
                arguments.Length,
                argv);
            if (result != 0)
                throw new InvalidOperationException(
                    $"libvlc_InternalInit failed with {result}.");
            initialized = true;

            StatusChanged?.Invoke("Playing through managed vout display");
            VLCCore.libvlc_InternalPlay(core);
            stop.Wait();
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(exception.Message);
        }
        finally
        {
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

            StatusChanged?.Invoke("Stopped");
        }
    }

    public void Stop()
    {
        Thread? thread;
        ManualResetEventSlim? stop;
        lock (_gate)
        {
            thread = _thread;
            stop = _stop;
            _thread = null;
            _stop = null;
        }

        stop?.Set();
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(10));
        stop?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}

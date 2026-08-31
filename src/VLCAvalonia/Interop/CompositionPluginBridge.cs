using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace VLCAvalonia.Interop;

internal sealed unsafe class CompositionPluginBridge : IDisposable
{
    internal sealed record GPUFrame(
        nint SharedHandle,
        int Width,
        int Height,
        ulong Generation,
        long PresentationTimestamp);

    internal sealed record Frame(
        int Width,
        int Height,
        int RowBytes,
        byte[] Pixels,
        long PresentationTimestamp);

    private static CompositionPluginBridge? s_active;

    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, void> _setGPUCallback;
    private Frame? _latestFrame;
    private GPUFrame? _latestGPUFrame;
    private bool _disposed;

    public string PluginDirectory { get; }

    public CompositionPluginBridge()
    {
        string pluginPath = FindPluginPath();
        PluginDirectory = Path.GetDirectoryName(pluginPath)!;
        _library = NativeLibrary.Load(pluginPath);
        nint setter = NativeLibrary.GetExport(
            _library,
            "vlc_avalonia_set_frame_callback");
        _setCallback =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)setter;
        nint GPUSetter = NativeLibrary.GetExport(
            _library,
            "vlc_avalonia_set_gpu_frame_callback");
        _setGPUCallback =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)GPUSetter;

        Volatile.Write(ref s_active, this);
        _setCallback(
            (nint)(delegate* unmanaged[Cdecl]<nint, uint, uint, int, nint, long, void>)&OnFrame,
            0);
    }

    public void EnableGPUFrames()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _setGPUCallback(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, ulong, long, void>)&OnGPUFrame,
            0);
    }

    public void DisableGPUFrames()
    {
        if (!_disposed)
            _setGPUCallback(0, 0);
        Interlocked.Exchange(ref _latestGPUFrame, null);
    }

    public bool TryTakeLatestGPU(out GPUFrame? frame)
    {
        frame = Interlocked.Exchange(ref _latestGPUFrame, null);
        return frame is not null;
    }

    public bool TryTakeLatest(out Frame? frame)
    {
        frame = Interlocked.Exchange(ref _latestFrame, null);
        return frame is not null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(
        nint opaque,
        uint width,
        uint height,
        int stride,
        nint pixels,
        long presentationTimestamp)
    {
        try
        {
            CompositionPluginBridge? bridge = Volatile.Read(ref s_active);
            if (bridge is null || bridge._disposed || pixels == 0)
                return;
            if (width is 0 or > 16384 || height is 0 or > 16384)
                return;

            int rowBytes = checked((int)width * 4);
            if (Math.Abs((long)stride) < rowBytes)
                return;

            byte[] destination = GC.AllocateUninitializedArray<byte>(
                checked(rowBytes * (int)height));
            for (int row = 0; row < height; row++)
            {
                nint sourceRow = pixels + checked(row * stride);
                new ReadOnlySpan<byte>((void*)sourceRow, rowBytes).CopyTo(
                    destination.AsSpan(row * rowBytes, rowBytes));
            }

            Interlocked.Exchange(
                ref bridge._latestFrame,
                new Frame(
                    (int)width,
                    (int)height,
                    rowBytes,
                    destination,
                    presentationTimestamp));
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGPUFrame(
        nint opaque,
        nint sharedHandle,
        uint width,
        uint height,
        ulong generation,
        long presentationTimestamp)
    {
        try
        {
            CompositionPluginBridge? bridge = Volatile.Read(ref s_active);
            if (bridge is null || bridge._disposed || sharedHandle == 0)
                return;
            if (width is 0 or > 16384 || height is 0 or > 16384)
                return;

            Interlocked.Exchange(
                ref bridge._latestGPUFrame,
                new GPUFrame(
                    sharedHandle,
                    (int)width,
                    (int)height,
                    generation,
                    presentationTimestamp));
        }
        catch
        {
        }
    }

    private static string FindPluginPath()
    {
        string root = RepositoryPaths.Root;
        string fileName = "libvlc_avalonia_plugin.dll";
        string[] candidates =
        [
            Path.Combine(
                RepositoryPaths.VLCDirectory,
                "plugins",
                "video_output",
                fileName),
            Path.Combine(
                root,
                "src",
                "VLCAvalonia.Vout",
                "bin",
                "Release",
                "net10.0",
                "win-x64",
                "native",
                fileName),
            Path.Combine(
                root,
                "src",
                "VLCAvalonia.Vout",
                "bin",
                "Debug",
                "net10.0",
                "win-x64",
                "native",
                fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Publish src/VLCAvalonia.Vout for win-x64 before starting " +
            "VLC Avalonia.",
            candidates[1]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _setGPUCallback(0, 0);
        _setCallback(0, 0);
        Interlocked.CompareExchange(ref s_active, null, this);
        Interlocked.Exchange(ref _latestFrame, null);
        Interlocked.Exchange(ref _latestGPUFrame, null);

        // Do not unload: VLC may hold the same module after discovering it.
        GC.SuppressFinalize(this);
    }
}

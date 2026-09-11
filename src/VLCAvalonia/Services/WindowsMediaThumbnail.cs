using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VLCAvalonia.Services;

/// <summary>Windows Shell thumbnail provider on a dedicated STA, independent of VLC playback.</summary>
internal static partial class WindowsMediaThumbnail
{
    private sealed record Request(string Path, TaskCompletionSource<byte[]?> Completion);
    private static readonly Lazy<BlockingCollection<Request>> Queue = new(StartWorker);

    public static Task<byte[]?> ReadAsync(string path)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<byte[]?>(null);
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Queue.Value.TryAdd(new(path, completion))) completion.SetResult(null);
        return completion.Task;
    }

    private static BlockingCollection<Request> StartWorker()
    {
        BlockingCollection<Request> requests = new(64);
        Thread thread = new(() =>
        {
            int initialized = CoInitializeEx(0, 2); // COINIT_APARTMENTTHREADED
            try
            {
                foreach (Request request in requests.GetConsumingEnumerable())
                {
                    try { request.Completion.SetResult(initialized >= 0 ? Read(request.Path) : null); }
                    catch (Exception) { request.Completion.SetResult(null); }
                }
            }
            finally { if (initialized >= 0) CoUninitialize(); }
        }) { IsBackground = true, Name = "Home thumbnails" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return requests;
    }

    private static unsafe byte[]? Read(string path)
    {
        // Windows SDK ShObjIdl_core.h: IShellItemImageFactory, slot 3 after IUnknown.
        Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        nint factory = 0, handle = 0, dc = 0;
        try
        {
            if (SHCreateItemFromParsingName(path, 0, in iid, out factory) < 0 || factory == 0) return null;
            var getImage = (delegate* unmanaged[Stdcall]<nint, NativeSize, uint, out nint, int>)(*(nint**)factory)[3];
            // THUMBNAILONLY: never mistake an application/file icon for media artwork.
            if (getImage(factory, new(384, 216), 0x8, out handle) < 0 || handle == 0) return null;
            if (GetObjectW(handle, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap native) == 0 ||
                native.Width is <= 0 or > 1024 || native.Height is <= 0 or > 1024) return null;
            using WriteableBitmap bitmap = new(new PixelSize(native.Width, native.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                if (buffer.RowBytes != native.Width * 4) return null;
                BitmapInfo info = new() { Size = 40, Width = native.Width, Height = -native.Height, Planes = 1, BitCount = 32 };
                dc = GetDC(0);
                if (dc == 0 || GetDIBits(dc, handle, 0, (uint)native.Height, buffer.Address, ref info, 0) != native.Height) return null;
            }
            using MemoryStream stream = new();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
        finally
        {
            if (dc != 0) ReleaseDC(0, dc);
            if (handle != 0) DeleteObject(handle);
            if (factory != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)factory)[2])(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeSize(int Width, int Height);
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant, Color; }
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint mode);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint factory);
    [LibraryImport("gdi32.dll")] private static partial int GetObjectW(nint bitmap, int size, out NativeBitmap result);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint dc, nint bitmap, uint first, uint lines, nint bits, ref BitmapInfo info, uint usage);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint handle);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
}

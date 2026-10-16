using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Conic.Interop;

namespace Conic.Services;

/// <summary>
/// Makes a thumbnail with VLC when the desktop has none: Linux desktops only
/// thumbnail video when a thumbnailer is installed (Ubuntu ships none by
/// default), so Home would show none. One frame a tenth of the way in is
/// decoded through libvlc's video callbacks, scaled by VLC to fit 384x216,
/// and encoded as PNG. One thumbnail is made at a time.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe class VlcThumbnail
{
    private const int MaximumWidth = 384;
    private const int MaximumHeight = 216;
    private const int ParsedDone = 4;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim Gate = new(1);
    private static Api? s_api;
    private static nint s_instance;

    public static Task<byte[]?> ReadAsync(string path) =>
        !OperatingSystem.IsLinux() || !NativeEngineSupport.IsAvailable ? Task.FromResult<byte[]?>(null) : Task.Run(() =>
        {
            Gate.Wait();
            try { return Extract(path); }
            catch (Exception) { return null; }
            finally { Gate.Release(); }
        });

    private static byte[]? Extract(string path)
    {
        Api api = s_api ??= new Api(NativeLibrary.Load(RepositoryPaths.PublicLibraryPath));
        if (s_instance == 0)
        {
            string[] arguments = ["--intf=dummy", "--no-audio", "--no-spu", "--no-osd", "--no-video-title-show",
                "--no-sub-autodetect-file", "--no-stats", "--avcodec-hw=none", "--quiet"];
            nint[] native = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            try
            {
                fixed (nint* argv = native)
                    s_instance = api.New(native.Length, argv);
            }
            finally { foreach (nint argument in native) Marshal.FreeCoTaskMem(argument); }
            if (s_instance == 0) return null;
        }

        nint pathUtf8 = Marshal.StringToCoTaskMemUTF8(path);
        nint media;
        try { media = api.MediaNewPath(s_instance, (byte*)pathUtf8); }
        finally { Marshal.FreeCoTaskMem(pathUtf8); }
        if (media == 0) return null;
        Request request = new();
        GCHandle handle = GCHandle.Alloc(request);
        nint player = 0;
        try
        {
            if (api.MediaParseWithOptions(media, 0, 3000) == 0)
                for (DateTime deadline = DateTime.UtcNow.AddSeconds(3); api.MediaGetParsedStatus(media) != ParsedDone && DateTime.UtcNow < deadline;)
                    Thread.Sleep(20);
            long duration = api.MediaGetDuration(media);
            AddOption(api, media, ":no-audio");
            if (duration > 2000)
                AddOption(api, media, FormattableString.Invariant($":start-time={duration / 10000.0:0.###}"));

            player = api.PlayerNewFromMedia(media);
            if (player == 0) return null;
            api.SetFormatCallbacks(player, &Setup, &Cleanup);
            api.SetCallbacks(player, &Lock, &Unlock, &Display, GCHandle.ToIntPtr(handle));
            if (api.Play(player) != 0 || !request.Done.Wait(FrameTimeout)) return null;
            api.Stop(player);
            return Encode(request);
        }
        finally
        {
            if (player != 0)
            {
                api.Stop(player);
                api.PlayerRelease(player);
            }
            api.MediaRelease(media);
            handle.Free();
            if (request.Pixels != 0) NativeMemory.Free((void*)request.Pixels);
        }
    }

    private static void AddOption(Api api, nint media, string option)
    {
        nint value = Marshal.StringToCoTaskMemUTF8(option);
        try { api.MediaAddOption(media, (byte*)value); }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    private static byte[]? Encode(Request request)
    {
        if (request.Width <= 0 || request.Height <= 0) return null;
        using WriteableBitmap bitmap = new(new PixelSize(request.Width, request.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer locked = bitmap.Lock())
        {
            int rowBytes = request.Width * 4;
            for (int row = 0; row < request.Height; row++)
                Buffer.MemoryCopy((byte*)request.Pixels + (long)row * rowBytes, (byte*)locked.Address + (long)row * locked.RowBytes, rowBytes, rowBytes);
        }
        using MemoryStream stream = new();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>One thumbnail's frame: VLC writes into Pixels, then Done is set.</summary>
    private sealed class Request
    {
        public nint Pixels;
        public int Width;
        public int Height;
        public ManualResetEventSlim Done { get; } = new();
    }

    private static Request From(nint opaque) => (Request)GCHandle.FromIntPtr(opaque).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static uint Setup(nint* opaque, byte* chroma, uint* width, uint* height, uint* pitches, uint* lines)
    {
        Request request = From(*opaque);
        if (*width == 0 || *height == 0) return 0;
        double scale = Math.Min(1, Math.Min((double)MaximumWidth / *width, (double)MaximumHeight / *height));
        int w = Math.Max(2, (int)Math.Round(*width * scale) & ~1), h = Math.Max(2, (int)Math.Round(*height * scale) & ~1);
        "RV32"u8.CopyTo(new Span<byte>(chroma, 4));
        *width = (uint)w;
        *height = (uint)h;
        pitches[0] = (uint)(w * 4);
        lines[0] = (uint)h;
        if (request.Pixels != 0) NativeMemory.Free((void*)request.Pixels);
        request.Pixels = (nint)NativeMemory.Alloc((nuint)(w * 4 * h));
        request.Width = w;
        request.Height = h;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Cleanup(nint opaque) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Lock(nint opaque, nint* planes)
    {
        planes[0] = From(opaque).Pixels;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Unlock(nint opaque, nint picture, nint* planes) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Display(nint opaque, nint picture) => From(opaque).Done.Set();

    /// <summary>The libvlc 3 calls used here, bound to the runtime's own libvlc.</summary>
    private sealed class Api(nint library)
    {
        private nint Export(string name) => NativeLibrary.GetExport(library, name);
        public delegate* unmanaged[Cdecl]<int, nint*, nint> New => (delegate* unmanaged[Cdecl]<int, nint*, nint>)Export("libvlc_new");
        public delegate* unmanaged[Cdecl]<nint, byte*, nint> MediaNewPath => (delegate* unmanaged[Cdecl]<nint, byte*, nint>)Export("libvlc_media_new_path");
        public delegate* unmanaged[Cdecl]<nint, byte*, void> MediaAddOption => (delegate* unmanaged[Cdecl]<nint, byte*, void>)Export("libvlc_media_add_option");
        public delegate* unmanaged[Cdecl]<nint, int, int, int> MediaParseWithOptions => (delegate* unmanaged[Cdecl]<nint, int, int, int>)Export("libvlc_media_parse_with_options");
        public delegate* unmanaged[Cdecl]<nint, int> MediaGetParsedStatus => (delegate* unmanaged[Cdecl]<nint, int>)Export("libvlc_media_get_parsed_status");
        public delegate* unmanaged[Cdecl]<nint, long> MediaGetDuration => (delegate* unmanaged[Cdecl]<nint, long>)Export("libvlc_media_get_duration");
        public delegate* unmanaged[Cdecl]<nint, void> MediaRelease => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_release");
        public delegate* unmanaged[Cdecl]<nint, nint> PlayerNewFromMedia => (delegate* unmanaged[Cdecl]<nint, nint>)Export("libvlc_media_player_new_from_media");
        public delegate* unmanaged[Cdecl]<nint, int> Play => (delegate* unmanaged[Cdecl]<nint, int>)Export("libvlc_media_player_play");
        public delegate* unmanaged[Cdecl]<nint, void> Stop => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_player_stop");
        public delegate* unmanaged[Cdecl]<nint, void> PlayerRelease => (delegate* unmanaged[Cdecl]<nint, void>)Export("libvlc_media_player_release");
        public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint*, byte*, uint*, uint*, uint*, uint*, uint>, delegate* unmanaged[Cdecl]<nint, void>, void> SetFormatCallbacks =>
            (delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint*, byte*, uint*, uint*, uint*, uint*, uint>, delegate* unmanaged[Cdecl]<nint, void>, void>)Export("libvlc_video_set_format_callbacks");
        public delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, nint*, nint>, delegate* unmanaged[Cdecl]<nint, nint, nint*, void>, delegate* unmanaged[Cdecl]<nint, nint, void>, nint, void> SetCallbacks =>
            (delegate* unmanaged[Cdecl]<nint, delegate* unmanaged[Cdecl]<nint, nint*, nint>, delegate* unmanaged[Cdecl]<nint, nint, nint*, void>, delegate* unmanaged[Cdecl]<nint, nint, void>, nint, void>)Export("libvlc_video_set_callbacks");
    }
}

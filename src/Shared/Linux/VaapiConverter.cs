using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.Linux;

/// <summary>A converted target exported as one DMA-BUF plane. The converter owns the descriptor.</summary>
internal sealed record ExportedSurface(int Fd, uint DrmFormat, ulong Modifier, uint Pitch, uint Offset, ulong Size, int Objects, int Layers);

/// <summary>
/// VA-API on a DRM render node: one YUV source surface (NV12 or P010) at the
/// decoded size and a ring of BGRA targets at the visible size, written by
/// video processing and exported as DMA-BUFs. Not thread-safe; one caller
/// drives it. Shared by the Linux video output and the DMA-BUF probe.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe partial class VaapiConverter : IDisposable
{
    private const int OpenReadWrite = 2;
    private const int OpenCloseOnExec = 0x80000;

    private readonly int _width;
    private readonly int _height;
    private readonly VARectangle _visible;
    private readonly uint _sourceFourCC;
    private int _drmFd = -1;
    private nint _display;
    private uint _config = VaApi.InvalidId;
    private uint _context = VaApi.InvalidId;
    private uint _source = VaApi.InvalidId;
    private readonly uint[] _targets;
    private readonly List<int> _exportedFds = [];

    public string RenderNode { get; private set; } = "";
    public string Vendor { get; private set; } = "";
    /// <summary>Size of the source surface, the decoded (coded) size.</summary>
    public int Width => _width;
    public int Height => _height;
    /// <summary>Size of the exported targets: the visible part of the source.</summary>
    public int TargetWidth => _visible.Width;
    public int TargetHeight => _visible.Height;
    public int RingSize => _targets.Length;

    private VaapiConverter(int width, int height, VARectangle visible, uint sourceFourCC, int ringSize)
    {
        _width = width;
        _height = height;
        _visible = visible;
        _sourceFourCC = sourceFourCC;
        _targets = Enumerable.Repeat(VaApi.InvalidId, ringSize).ToArray();
    }

    /// <param name="sourceFourCC"><see cref="VaApi.FourCCNv12"/> or <see cref="VaApi.FourCCP010"/>.</param>
    public static VaapiConverter Open(int width, int height, uint sourceFourCC, int ringSize) =>
        Open(width, height, 0, 0, width, height, sourceFourCC, ringSize);

    /// <summary>Opens a converter whose targets hold the visible rectangle of a <paramref name="width"/> x <paramref name="height"/> source.</summary>
    public static VaapiConverter Open(int width, int height, int visibleX, int visibleY, int visibleWidth, int visibleHeight,
        uint sourceFourCC, int ringSize)
    {
        if (sourceFourCC is not (VaApi.FourCCNv12 or VaApi.FourCCP010))
            throw new ArgumentOutOfRangeException(nameof(sourceFourCC));
        if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || visibleX < 0 || visibleY < 0 ||
            visibleWidth <= 0 || visibleHeight <= 0 || visibleX + visibleWidth > width || visibleY + visibleHeight > height)
            throw new ArgumentOutOfRangeException(nameof(visibleWidth), $"Visible {visibleX},{visibleY} {visibleWidth}x{visibleHeight} of {width}x{height}.");
        VARectangle visible = new() { X = (short)visibleX, Y = (short)visibleY, Width = (ushort)visibleWidth, Height = (ushort)visibleHeight };
        VaapiConverter converter = new(width, height, visible, sourceFourCC, ringSize);
        try
        {
            converter.OpenDisplay();
            converter._source = converter.CreateSurface(
                sourceFourCC == VaApi.FourCCP010 ? VaApi.RtFormatYuv420TenBit : VaApi.RtFormatYuv420,
                sourceFourCC, VaApi.UsageHintVppRead, width, height);
            for (int i = 0; i < ringSize; i++)
                converter._targets[i] = converter.CreateSurface(VaApi.RtFormatRgb32, VaApi.FourCCBgra,
                    VaApi.UsageHintVppWrite | VaApi.UsageHintExport, visibleWidth, visibleHeight);
            uint config, context;
            Check(VaApi.CreateConfig(converter._display, VaApi.ProfileNone, VaApi.EntrypointVideoProc, 0, 0, &config), "vaCreateConfig");
            converter._config = config;
            fixed (uint* targets = converter._targets)
                Check(VaApi.CreateContext(converter._display, config, visibleWidth, visibleHeight, 0, targets, ringSize, &context), "vaCreateContext");
            converter._context = context;
            return converter;
        }
        catch
        {
            converter.Dispose();
            throw;
        }
    }

    private void OpenDisplay()
    {
        foreach (string node in Directory.GetFiles("/dev/dri", "renderD*").Order(StringComparer.Ordinal))
        {
            int fd = OpenFile(node, OpenReadWrite | OpenCloseOnExec);
            if (fd < 0) continue;
            nint display = VaApi.GetDisplayDrm(fd);
            int major, minor;
            if (display != 0 && VaApi.Initialize(display, &major, &minor) == VaApi.Success)
            {
                _drmFd = fd;
                _display = display;
                RenderNode = node;
                Vendor = Marshal.PtrToStringUTF8(VaApi.QueryVendorString(display)) ?? "";
                return;
            }
            if (display != 0) VaApi.Terminate(display);
            CloseFile(fd);
        }
        throw new InvalidOperationException("No DRM render node initializes VA-API.");
    }

    private uint CreateSurface(uint format, uint fourcc, int usage, int width, int height)
    {
        VASurfaceAttrib* attributes = stackalloc VASurfaceAttrib[2];
        attributes[0] = new() { Type = VASurfaceAttrib.PixelFormat, Flags = VASurfaceAttrib.Settable };
        attributes[0].Value.Type = VAGenericValue.TypeInteger;
        attributes[0].Value.Integer = (int)fourcc;
        attributes[1] = new() { Type = VaApi.SurfaceAttribUsageHint, Flags = VASurfaceAttrib.Settable };
        attributes[1].Value.Type = VAGenericValue.TypeInteger;
        attributes[1].Value.Integer = usage;
        uint surface;
        Check(VaApi.CreateSurfaces(_display, format, (uint)width, (uint)height, &surface, 1, attributes, 2), "vaCreateSurfaces");
        return surface;
    }

    /// <summary>A mapped view of the source surface; dispose it to unmap.</summary>
    public readonly ref struct SourceView
    {
        private readonly VaapiConverter _owner;
        private readonly VAImage _image;
        public readonly byte* Luma;
        public readonly int LumaPitch;
        public readonly byte* Chroma;
        public readonly int ChromaPitch;

        internal SourceView(VaapiConverter owner, VAImage image, byte* data)
        {
            _owner = owner;
            _image = image;
            Luma = data + image.Offsets[0];
            LumaPitch = (int)image.Pitches[0];
            Chroma = data + image.Offsets[1];
            ChromaPitch = (int)image.Pitches[1];
        }

        public void Dispose()
        {
            VaApi.UnmapBuffer(_owner._display, _image.Buffer);
            VaApi.DestroyImage(_owner._display, _image.Id);
        }
    }

    /// <summary>Maps the source surface for writing.</summary>
    public SourceView MapSource()
    {
        VAImage image;
        Check(VaApi.DeriveImage(_display, _source, &image), "vaDeriveImage");
        void* data = null;
        int status = image.FourCC == _sourceFourCC ? VaApi.MapBuffer(_display, image.Buffer, &data) : VaApi.Success;
        if (image.FourCC != _sourceFourCC || status != VaApi.Success)
        {
            VaApi.DestroyImage(_display, image.Id);
            throw new InvalidOperationException(image.FourCC != _sourceFourCC
                ? $"The source surface derives as {FourCCText(image.FourCC)}, not {FourCCText(_sourceFourCC)}."
                : $"vaMapBuffer failed: {VaApi.Describe(status)}");
        }
        return new SourceView(this, image, (byte*)data);
    }

    /// <summary>
    /// Copies a CPU frame of the source's format (NV12 or P010) into the source
    /// surface. Rows past <paramref name="lumaLines"/> (luma) are not read.
    /// </summary>
    public void Upload(byte* luma, int lumaPitch, int lumaLines, byte* chroma, int chromaPitch, int chromaLines)
    {
        int rowBytes = _width * (_sourceFourCC == VaApi.FourCCP010 ? 2 : 1);
        if (lumaPitch < rowBytes || chromaPitch < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(lumaPitch));
        int lumaRows = Math.Min(_height, lumaLines);
        int chromaRows = Math.Min(_height / 2, chromaLines);
        using SourceView view = MapSource();
        for (int y = 0; y < lumaRows; y++)
            Buffer.MemoryCopy(luma + (long)y * lumaPitch, view.Luma + (long)y * view.LumaPitch, rowBytes, rowBytes);
        for (int y = 0; y < chromaRows; y++)
            Buffer.MemoryCopy(chroma + (long)y * chromaPitch, view.Chroma + (long)y * view.ChromaPitch, rowBytes, rowBytes);
    }

    /// <summary>
    /// Converts the visible source to full-range sRGB BGRA in target
    /// <paramref name="index"/>, on the GPU, finished before returning.
    /// </summary>
    public void Convert(int index, int colorStandard = VaApi.ColorStandardBT709, bool fullRange = false)
    {
        uint target = _targets[index];
        VARectangle visible = _visible;
        VAProcPipelineParameterBuffer parameters = new()
        {
            Surface = _source,
            SurfaceRegion = (nint)(&visible),
            SurfaceColorStandard = colorStandard,
            OutputColorStandard = VaApi.ColorStandardSRGB,
            OutputBackgroundColor = 0xFF000000,
            FilterFlags = VaApi.FilterScalingDefault,
        };
        parameters.InputColorProperties.ColorRange = fullRange ? VaApi.SourceRangeFull : VaApi.SourceRangeReduced;
        parameters.OutputColorProperties.ColorRange = VaApi.SourceRangeFull;
        uint buffer;
        Check(VaApi.CreateBuffer(_display, _context, VaApi.ProcPipelineParameterBufferType,
            (uint)sizeof(VAProcPipelineParameterBuffer), 1, &parameters, &buffer), "vaCreateBuffer");
        try
        {
            Check(VaApi.BeginPicture(_display, _context, target), "vaBeginPicture");
            Check(VaApi.RenderPicture(_display, _context, &buffer, 1), "vaRenderPicture");
            Check(VaApi.EndPicture(_display, _context), "vaEndPicture");
        }
        finally { VaApi.DestroyBuffer(_display, buffer); }
        Check(VaApi.SyncSurface(_display, target), "vaSyncSurface");
    }

    /// <summary>Reads BGRA pixels of one row of a converted target (diagnostics only).</summary>
    public byte[] ReadBackRow(int index, int y, int[] columns)
    {
        VAImage image;
        Check(VaApi.DeriveImage(_display, _targets[index], &image), "vaDeriveImage");
        try
        {
            void* data;
            Check(VaApi.MapBuffer(_display, image.Buffer, &data), "vaMapBuffer");
            try
            {
                byte[] pixels = new byte[columns.Length * 4];
                byte* row = (byte*)data + image.Offsets[0] + (long)y * image.Pitches[0];
                for (int i = 0; i < columns.Length; i++)
                    new ReadOnlySpan<byte>(row + columns[i] * 4, 4).CopyTo(pixels.AsSpan(i * 4));
                return pixels;
            }
            finally { VaApi.UnmapBuffer(_display, image.Buffer); }
        }
        finally { VaApi.DestroyImage(_display, image.Id); }
    }

    /// <summary>Exports a target once; the descriptor stays valid while the converter lives.</summary>
    public ExportedSurface Export(int index)
    {
        VADRMPRIMESurfaceDescriptor descriptor;
        Check(VaApi.ExportSurfaceHandle(_display, _targets[index], VaApi.SurfaceAttribMemTypeDrmPrime2,
            VaApi.ExportSurfaceReadOnly | VaApi.ExportSurfaceComposedLayers, &descriptor), "vaExportSurfaceHandle");
        for (int i = 0; i < descriptor.ObjectCount; i++)
            _exportedFds.Add(descriptor.GetObject(i).Fd);
        if (descriptor.ObjectCount != 1 || descriptor.LayerCount != 1 || descriptor.Layer0.PlaneCount != 1)
            throw new InvalidOperationException(
                $"Expected one object, layer and plane, got {descriptor.ObjectCount}/{descriptor.LayerCount}/{descriptor.Layer0.PlaneCount}.");
        VADRMPRIMEObject memory = descriptor.Object0;
        return new(memory.Fd, descriptor.Layer0.DrmFormat, memory.DrmFormatModifier,
            descriptor.Layer0.Pitch[0], descriptor.Layer0.Offset[0], memory.Size,
            (int)descriptor.ObjectCount, (int)descriptor.LayerCount);
    }

    public void Dispose()
    {
        foreach (int fd in _exportedFds) CloseFile(fd);
        _exportedFds.Clear();
        if (_display != 0)
        {
            if (_context != VaApi.InvalidId) VaApi.DestroyContext(_display, _context);
            if (_config != VaApi.InvalidId) VaApi.DestroyConfig(_display, _config);
            uint[] surfaces = [.. _targets.Where(s => s != VaApi.InvalidId), .. _source != VaApi.InvalidId ? [_source] : Array.Empty<uint>()];
            fixed (uint* pointer = surfaces)
                if (surfaces.Length > 0) VaApi.DestroySurfaces(_display, pointer, surfaces.Length);
            VaApi.Terminate(_display);
            _display = 0;
        }
        if (_drmFd >= 0) CloseFile(_drmFd);
        _drmFd = -1;
    }

    /// <summary>The VA-API color standard for a VLC 3 <c>video_color_space_t</c>, guessing from the height when it is undefined.</summary>
    public static int ColorStandardFor(int vlcColorSpace, int height) => vlcColorSpace switch
    {
        1 => VaApi.ColorStandardBT601,
        2 => VaApi.ColorStandardBT709,
        3 => VaApi.ColorStandardBT2020,
        _ => height >= 720 ? VaApi.ColorStandardBT709 : VaApi.ColorStandardBT601,
    };

    internal static string FourCCText(uint value) =>
        new([(char)(value & 0xFF), (char)((value >> 8) & 0xFF), (char)((value >> 16) & 0xFF), (char)(value >> 24)]);

    private static void Check(int status, string call)
    {
        if (status != VaApi.Success) throw new InvalidOperationException($"{call} failed: {VaApi.Describe(status)}");
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenFile(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    internal static partial int CloseFile(int fd);

    [LibraryImport("libc", EntryPoint = "dup")]
    internal static partial int DuplicateFile(int fd);
}

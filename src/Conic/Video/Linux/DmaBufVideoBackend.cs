using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Player.Contracts;
using Conic.Controls;
using Conic.Interop;
using Conic.Interop.Linux;

namespace Conic.Video.Linux;

/// <summary>
/// Linux VLC vout bridge: imports the DMA-BUF targets the video output
/// converts each frame into (VA-API, BGRA) into Avalonia's compositor.
/// </summary>
/// <remarks>
/// Avalonia imports DMA-BUFs only with EGL, and copies an image on the GPU
/// when the drawing surface is updated, so a target goes back to the video
/// output as soon as the update completes. Synchronization is implicit:
/// the output finishes converting before it lends a target.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class DmaBufVideoBackend : IVideoBackend
{
    private const string HandleType = KnownPlatformGraphicsExternalImageHandleTypes.DmaBufFileDescriptor;

    private readonly Control _host;
    private readonly VideoBackendOptions _options;
    private DmaBufPluginBridge? _bridge;
    private bool _disposed;
    private WriteableBitmap? _bitmap;
    private ICompositionGpuInterop? _interop;
    private readonly Dictionary<int, ICompositionImportedGpuImage> _imported = [];
    private ulong _importedGeneration;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _surfaceVisual;
    private CompositionContainerVisual? _videoClip;
    private VideoGeometry _geometry;
    private VideoSubtitle? _subtitle;
    private VideoSurfaceLayout _notifiedLayout;
    private int _videoWidth;
    private int _videoHeight;
    private bool _gpuUpdatePending;
    private bool _gpuEnabled;
    private bool _showingCpuFrame;
    private bool _reportedFirstGpuFrame;
    private bool _reportedFirstCpuFrame;
    private long _gpuFramesPresented;
    private readonly HashSet<(ulong, int)> _targets = [];

    public DmaBufVideoBackend(Control host, VideoBackendOptions options)
    {
        _host = host;
        _options = options;
        _bridge = new DmaBufPluginBridge();
    }

    public event Action<string>? StatusChanged;
    public event Action<ulong, ulong, string>? GpuFramePresented;
    public event Action? PresentationChanged;
    public VideoGeometry Geometry => _geometry;
    public VideoSubtitle? Subtitle => _subtitle;
    public Size VideoSize => new(_videoWidth, _videoHeight);
    public bool HasCpuFrame => _reportedFirstCpuFrame;
    public bool HasGpuFrame => _reportedFirstGpuFrame;
    public SharedVideoSurface? SharedSurface =>
        _gpuEnabled && !_showingCpuFrame && _reportedFirstGpuFrame && _drawingSurface is not null && _videoWidth > 0 && _videoHeight > 0
            ? new(_drawingSurface, new Size(_videoWidth, _videoHeight), new Rect(0, 0, _videoWidth, _videoHeight))
            : null;
    internal VideoSurfaceLayout CurrentVideoLayout => VideoSurfaceLayout.Calculate(
        _host.Bounds.Size, VideoSize, _geometry, TopLevel.GetTopLevel(_host)?.RenderScaling ?? 1);

    /// <summary>Distinct targets presented so far, for smoke diagnostics.</summary>
    internal int DistinctTargets => _targets.Count;
    internal long GpuFramesPresentedCount => _gpuFramesPresented;
    internal DmaBufPluginBridge.PoolTelemetry? ReadPoolTelemetry() => _bridge?.ReadPoolTelemetry();

    public IMediaEngine CreatePlayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativePlaybackSupport.RequireAvailable();
        return new VlcCorePlayer(verboseLogging: Environment.GetEnvironmentVariable("CONIC_VLC_VERBOSE") == "1");
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReportStatus("Managed vout ready (initializing DMA-BUF sharing)");
        try
        {
            CompositionVisual? elementVisual = ElementComposition.GetElementVisual(_host)
                ?? throw new InvalidOperationException("Avalonia did not expose a compositor visual.");
            ICompositionGpuInterop? interop = await elementVisual.Compositor.TryGetCompositionGpuInterop();
            if (_disposed)
                return;
            if (interop is null || interop.IsLost || !interop.SupportedImageHandleTypes.Contains(HandleType))
            {
                ReportStatus("DMA-BUF import unavailable; using CPU BGRA fallback");
                return;
            }
            if (_options.ForceUnavailableAdapter)
            {
                ReportStatus("DMA-BUF composition disabled for this run; using CPU BGRA fallback");
                return;
            }

            _interop = interop;
            _drawingSurface = elementVisual.Compositor.CreateDrawingSurface();
            _surfaceVisual = elementVisual.Compositor.CreateSurfaceVisual();
            _surfaceVisual.Surface = _drawingSurface;
            _videoClip = elementVisual.Compositor.CreateContainerVisual();
            _videoClip.ClipToBounds = true;
            _videoClip.Children.Add(_surfaceVisual);
            ElementComposition.SetElementChildVisual(_host, _videoClip);
            _gpuEnabled = true;
            _bridge?.EnableDmaBufFrames();
            ReportStatus($"DMA-BUF composition active; synchronization={interop.GetSynchronizationCapabilities(HandleType)}");
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            _bridge?.DisableDmaBufFrames();
            ReleaseGpuResources();
            ReportStatus($"GPU composition unavailable: {exception.Message}");
        }
    }

    public async Task PresentLatestFrameAsync()
    {
        if (_disposed) return;
        bool subtitleChanged = ProcessLatestSubtitle();
        await PresentLatestVideoFrameAsync();
        VideoSurfaceLayout layout = CurrentVideoLayout;
        if (_disposed || (!subtitleChanged && layout == _notifiedLayout)) return;
        _notifiedLayout = layout;
        PresentationChanged?.Invoke();
    }

    private unsafe bool ProcessLatestSubtitle()
    {
        if (_bridge?.TryTakeLatestSubtitle(out DmaBufPluginBridge.Subtitle? subtitle) != true || subtitle is null)
            return false;
        VideoSubtitle? previous = _subtitle;
        _subtitle = null;
        if (subtitle.Pixels.Length != 0)
        {
            fixed (byte* pixels = subtitle.Pixels)
            {
                Bitmap image = new(PixelFormat.Rgba8888, AlphaFormat.Unpremul, (nint)pixels,
                    new PixelSize(subtitle.Bounds.Width, subtitle.Bounds.Height), new Vector(96, 96), subtitle.Bounds.Width * 4);
                _subtitle = new VideoSubtitle(image, subtitle.Bounds, subtitle.CanvasWidth, subtitle.CanvasHeight);
            }
        }
        previous?.Image.Dispose();
        return true;
    }

    private async Task PresentLatestVideoFrameAsync()
    {
        UpdateLayout();
        if (_bridge is not null)
        {
            VideoGeometry geometry = _bridge.GetVideoGeometry();
            if (!_geometry.Equals(geometry))
            {
                _geometry = geometry;
                UpdateLayout();
                _host.InvalidateVisual();
            }
        }

        if (_gpuEnabled && !_gpuUpdatePending &&
            _bridge?.TryTakeLatestDmaBuf(out DmaBufPluginBridge.DmaBufFrame? dmaBufFrame) == true &&
            dmaBufFrame is not null)
        {
            await PresentDmaBufFrameAsync(dmaBufFrame);
            return;
        }

        if (_bridge?.TryTakeLatest(out DmaBufPluginBridge.Frame? frame) != true || frame is null)
            return;
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        CopyFrameToBitmap(frame, _bitmap);
        _videoWidth = frame.Width;
        _videoHeight = frame.Height;
        _showingCpuFrame = true;
        UpdateLayout();
        if (!_reportedFirstCpuFrame)
        {
            _reportedFirstCpuFrame = true;
            ReportStatus("CPU BGRA fallback frame presented");
        }
        _host.InvalidateVisual();
    }

    private static unsafe void CopyFrameToBitmap(DmaBufPluginBridge.Frame frame, WriteableBitmap bitmap)
    {
        using ILockedFramebuffer locked = bitmap.Lock();
        int copyBytes = Math.Min(frame.RowBytes, locked.RowBytes);
        for (int row = 0; row < frame.Height; row++)
            frame.Pixels.AsSpan(row * frame.RowBytes, copyBytes)
                .CopyTo(new Span<byte>((void*)(locked.Address + row * locked.RowBytes), copyBytes));
    }

    private async Task PresentDmaBufFrameAsync(DmaBufPluginBridge.DmaBufFrame frame)
    {
        DmaBufPluginBridge? bridge = _bridge;
        if (_interop is null || _drawingSurface is null)
        {
            bridge?.Release(frame);
            return;
        }

        _gpuUpdatePending = true;
        try
        {
            ICompositionImportedGpuImage image = await ImportAsync(frame);
            if (_disposed || !_gpuEnabled || _drawingSurface is null)
                return;
            _videoWidth = frame.Width;
            _videoHeight = frame.Height;

            await _drawingSurface.UpdateAsync(image);
            if (_disposed || !_gpuEnabled) return;
            _showingCpuFrame = false;
            UpdateLayout();
            _gpuFramesPresented++;
            if (_targets.Count < 1024)
                _targets.Add((frame.Generation, frame.Slot));
            GpuFramePresented?.Invoke(frame.Generation, frame.Generation, "dmabuf:vaapi");
            if (!_reportedFirstGpuFrame)
            {
                _reportedFirstGpuFrame = true;
                ReportStatus("DMA-BUF frame presented by Avalonia compositor");
            }
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            DisableGpuForFallback($"presentation failed: {exception.Message}");
        }
        finally
        {
            bridge?.Release(frame);
            _gpuUpdatePending = false;
        }
    }

    private async Task<ICompositionImportedGpuImage> ImportAsync(DmaBufPluginBridge.DmaBufFrame frame)
    {
        if (frame.Generation != _importedGeneration)
        {
            foreach (ICompositionImportedGpuImage old in _imported.Values)
                await old.DisposeAsync();
            _imported.Clear();
            _importedGeneration = frame.Generation;
        }
        if (_imported.TryGetValue(frame.Slot, out ICompositionImportedGpuImage? hit))
        {
            if (!hit.IsLost)
                return hit;
            _imported.Remove(frame.Slot);
            await hit.DisposeAsync();
        }

        ICompositionImportedGpuImage image = _interop!.ImportImage(
            new PlatformHandle(frame.Fd, HandleType),
            new PlatformGraphicsExternalImageProperties
            {
                Width = frame.Width,
                Height = frame.Height,
                Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                MemorySize = frame.Size,
                MemoryOffset = frame.Offset,
                TopLeftOrigin = true,
                DmaBufProperties = new PlatformGraphicsExternalImageDmaBufProperties
                {
                    DrmFormat = frame.DrmFormat,
                    DrmModifier = frame.Modifier,
                    PlaneCount = 1,
                    PlaneStrides = [frame.Pitch],
                    PlaneOffsets = [frame.Offset],
                },
            });
        try
        {
            await image.ImportCompleted;
        }
        catch
        {
            await image.DisposeAsync();
            throw;
        }
        if (_disposed || !_gpuEnabled)
        {
            await image.DisposeAsync();
            throw new ObjectDisposedException(nameof(DmaBufVideoBackend));
        }
        _imported[frame.Slot] = image;
        return image;
    }

    private void DisableGpuForFallback(string reason)
    {
        if (!_gpuEnabled)
            return;
        _bridge?.DisableDmaBufFrames();
        ReleaseGpuResources();
        ReportStatus($"DMA-BUF {reason}; using CPU BGRA fallback");
        _host.InvalidateVisual();
    }

    private void ReleaseGpuResources()
    {
        _gpuEnabled = false;
        ElementComposition.SetElementChildVisual(_host, null);
        foreach (ICompositionImportedGpuImage image in _imported.Values)
            _ = image.DisposeAsync();
        _imported.Clear();
        _drawingSurface?.Dispose();
        _drawingSurface = null;
        _surfaceVisual = null;
        _videoClip = null;
        _interop = null;
    }

    public void UpdateLayout()
    {
        if (_surfaceVisual is null || _videoClip is null || _videoWidth <= 0 || _videoHeight <= 0)
            return;
        _videoClip.Opacity = _geometry.Active != 0 && !_showingCpuFrame ? 1 : 0;
        VideoSurfaceLayout layout = CurrentVideoLayout;
        _videoClip.Size = new Vector(layout.Destination.Width, layout.Destination.Height);
        _videoClip.Offset = new Vector3D(layout.Destination.X, layout.Destination.Y, 0);
        _surfaceVisual.Size = new Vector(layout.FullImage.Width, layout.FullImage.Height);
        _surfaceVisual.Offset = new Vector3D(layout.FullImage.X, layout.FullImage.Y, 0);
    }

    public void Render(DrawingContext context)
    {
        if ((_gpuEnabled && !_showingCpuFrame) || _bitmap is null || _geometry.Active == 0)
            return;
        VideoSurfaceLayout layout = CurrentVideoLayout;
        context.DrawImage(_bitmap, layout.Source, layout.Destination);
    }

    private void ReportStatus(string status)
    {
        if (!_disposed) StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridge?.DisableDmaBufFrames();
        ReleaseGpuResources();
        _bridge?.Dispose();
        _bridge = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _subtitle?.Image.Dispose();
        _subtitle = null;
    }
}

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
using Conic.Interop.MacOS;

namespace Conic.Video.MacOS;

/// <summary>
/// macOS VLC vout bridge: imports the IOSurface behind each VLC pixel buffer
/// into Avalonia's compositor, synchronized with Metal shared events.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class IOSurfaceVideoBackend : IVideoBackend
{
    // IOSurfaces imported so far. VLC cycles through a few pool buffers; the
    // cap bounds what stays imported if it keeps allocating new ones.
    private const int MaximumImportedSurfaces = 12;

    private readonly Control _host;
    private readonly VideoBackendOptions _options;
    private IOSurfacePluginBridge? _bridge;
    private bool _disposed;
    private WriteableBitmap? _bitmap;
    private ICompositionGpuInterop? _interop;
    private MetalSharedEvent? _ready;
    private MetalSharedEvent? _done;
    private ICompositionImportedGpuSemaphore? _readySemaphore;
    private ICompositionImportedGpuSemaphore? _doneSemaphore;
    private readonly List<ImportedSurface> _importedSurfaces = [];
    private readonly Queue<(IOSurfacePluginBridge.SurfaceFrame Frame, ulong Done)> _inFlight = new();
    private ulong _timeline;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _surfaceVisual;
    private CompositionContainerVisual? _videoClip;
    private VideoGeometry _geometry;
    private VideoSubtitle? _subtitle;
    private VideoSurfaceLayout _notifiedLayout;
    private int _videoWidth;
    private int _videoHeight;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _visibleX;
    private int _visibleY;
    private bool _gpuUpdatePending;
    private bool _gpuEnabled;
    private bool _reportedFirstGpuFrame;
    private bool _reportedFirstCpuFrame;
    private long _gpuFramesPresented;
    private readonly HashSet<uint> _surfaceIds = [];

    private sealed record ImportedSurface(nint Surface, uint SurfaceId, ICompositionImportedGpuImage Image);

    public IOSurfaceVideoBackend(Control host, VideoBackendOptions options)
    {
        _host = host;
        _options = options;
        _bridge = new IOSurfacePluginBridge();
    }

    public event Action<string>? StatusChanged;
    public event Action<ulong, ulong, string>? GpuFramePresented;
    public event Action? PresentationChanged;
    public VideoGeometry Geometry => _geometry;
    public VideoSubtitle? Subtitle => _subtitle;
    public Size VideoSize => new(_videoWidth, _videoHeight);
    public bool HasCpuFrame => _reportedFirstCpuFrame;
    public bool HasGpuFrame => _reportedFirstGpuFrame;
    internal VideoSurfaceLayout CurrentVideoLayout => VideoSurfaceLayout.Calculate(
        _host.Bounds.Size, VideoSize, _geometry, TopLevel.GetTopLevel(_host)?.RenderScaling ?? 1);

    /// <summary>Distinct IOSurfaces presented so far, for smoke diagnostics.</summary>
    internal int DistinctSurfaces => _surfaceIds.Count;
    internal long GpuFramesPresentedCount => _gpuFramesPresented;
    internal IOSurfacePluginBridge.PoolTelemetry? ReadPoolTelemetry() => _bridge?.ReadPoolTelemetry();

    public IMediaEngine CreatePlayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativePlaybackSupport.RequireAvailable();
        return new VlcCorePlayer();
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReportStatus("Managed vout ready (initializing IOSurface sharing)");
        try
        {
            CompositionVisual? elementVisual = ElementComposition.GetElementVisual(_host)
                ?? throw new InvalidOperationException("Avalonia did not expose a compositor visual.");
            ICompositionGpuInterop? interop = await elementVisual.Compositor.TryGetCompositionGpuInterop();
            if (_disposed)
                return;
            const string image = KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef;
            const string semaphore = KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent;
            if (interop is null || interop.IsLost ||
                !interop.SupportedImageHandleTypes.Contains(image) ||
                !interop.SupportedSemaphoreTypes.Contains(semaphore) ||
                (interop.GetSynchronizationCapabilities(image) &
                    CompositionGpuImportedImageSynchronizationCapabilities.TimelineSemaphores) == 0)
            {
                ReportStatus("IOSurface import unavailable; using CPU BGRA fallback");
                return;
            }
            if (_options.ForceUnavailableAdapter)
            {
                ReportStatus("IOSurface composition disabled for this run; using CPU BGRA fallback");
                return;
            }

            _ready = MetalSharedEvent.Create(interop.DeviceLuid);
            _done = MetalSharedEvent.Create(interop.DeviceLuid);
            _readySemaphore = interop.ImportSemaphore(new PlatformHandle(_ready.Handle, semaphore));
            _doneSemaphore = interop.ImportSemaphore(new PlatformHandle(_done.Handle, semaphore));
            await Task.WhenAll(_readySemaphore.ImportCompleted, _doneSemaphore.ImportCompleted);
            if (_disposed)
                return;

            _interop = interop;
            _drawingSurface = elementVisual.Compositor.CreateDrawingSurface();
            _surfaceVisual = elementVisual.Compositor.CreateSurfaceVisual();
            _surfaceVisual.Surface = _drawingSurface;
            _videoClip = elementVisual.Compositor.CreateContainerVisual();
            _videoClip.ClipToBounds = true;
            _videoClip.Children.Add(_surfaceVisual);
            ElementComposition.SetElementChildVisual(_host, _videoClip);
            _gpuEnabled = true;
            _bridge?.EnableSurfaceFrames();
            ReportStatus(
                "IOSurface composition active; " +
                $"metal-device={_ready.DeviceRegistryId:X16} compositor-device-match={_ready.MatchesCompositor.ToString().ToLowerInvariant()}");
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            _bridge?.DisableSurfaceFrames();
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
        if (_bridge?.TryTakeLatestSubtitle(out IOSurfacePluginBridge.Subtitle? subtitle) != true || subtitle is null)
            return false;
        VideoSubtitle? previous = _subtitle;
        _subtitle = null;
        if (subtitle.Pixels.Length != 0)
        {
            fixed (byte* pixels = subtitle.Pixels)
            {
                // The Bitmap copies the pixels.
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
        ReleaseCompletedFrames();
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
            _bridge?.TryTakeLatestSurface(out IOSurfacePluginBridge.SurfaceFrame? surfaceFrame) == true &&
            surfaceFrame is not null)
        {
            await PresentSurfaceFrameAsync(surfaceFrame);
            return;
        }

        if (_bridge?.TryTakeLatest(out IOSurfacePluginBridge.Frame? frame) != true || frame is null)
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
        if (!_reportedFirstCpuFrame)
        {
            _reportedFirstCpuFrame = true;
            ReportStatus("CPU BGRA fallback frame presented");
        }
        _host.InvalidateVisual();
    }

    private static unsafe void CopyFrameToBitmap(IOSurfacePluginBridge.Frame frame, WriteableBitmap bitmap)
    {
        using ILockedFramebuffer locked = bitmap.Lock();
        int copyBytes = Math.Min(frame.RowBytes, locked.RowBytes);
        for (int row = 0; row < frame.Height; row++)
            frame.Pixels.AsSpan(row * frame.RowBytes, copyBytes)
                .CopyTo(new Span<byte>((void*)(locked.Address + row * locked.RowBytes), copyBytes));
    }

    private async Task PresentSurfaceFrameAsync(IOSurfacePluginBridge.SurfaceFrame frame)
    {
        if (_interop is null || _drawingSurface is null || _ready is null || _readySemaphore is null || _doneSemaphore is null)
        {
            _bridge?.Release(frame);
            return;
        }

        _gpuUpdatePending = true;
        bool submitted = false;
        try
        {
            ICompositionImportedGpuImage image = await ImportAsync(frame);
            if (_disposed || !_gpuEnabled || _drawingSurface is null)
                return;
            _videoWidth = frame.Width;
            _videoHeight = frame.Height;
            _surfaceWidth = frame.SurfaceWidth;
            _surfaceHeight = frame.SurfaceHeight;
            _visibleX = frame.X;
            _visibleY = frame.Y;
            UpdateLayout();

            // The producer finished writing before lending the buffer, so the
            // wait value is signaled from the CPU; the compositor signals the
            // done value once it has copied the surface.
            ulong value = ++_timeline;
            _ready.SignaledValue = value;
            Task update = _drawingSurface.UpdateWithTimelineSemaphoresAsync(image, _readySemaphore, value, _doneSemaphore, value);
            _inFlight.Enqueue((frame, value));
            submitted = true;
            await update;
            if (_disposed || !_gpuEnabled) return;
            _gpuFramesPresented++;
            if (_surfaceIds.Count < 1024)
                _surfaceIds.Add(frame.SurfaceId);
            // Each display's pool is the IOSurface counterpart of a D3D11
            // device and shared texture: both change only when VLC reopens.
            GpuFramePresented?.Invoke(frame.PoolGeneration, frame.PoolGeneration, $"metal:{_ready.DeviceRegistryId:X16}");
            if (!_reportedFirstGpuFrame)
            {
                _reportedFirstGpuFrame = true;
                ReportStatus("IOSurface frame presented by Avalonia compositor");
            }
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            DisableGpuForFallback($"presentation failed: {exception.Message}");
        }
        finally
        {
            if (!submitted)
                _bridge?.Release(frame);
            _gpuUpdatePending = false;
        }
    }

    private async Task<ICompositionImportedGpuImage> ImportAsync(IOSurfacePluginBridge.SurfaceFrame frame)
    {
        int index = _importedSurfaces.FindIndex(entry => entry.Surface == frame.Surface && entry.SurfaceId == frame.SurfaceId);
        if (index >= 0)
        {
            ImportedSurface hit = _importedSurfaces[index];
            if (!hit.Image.IsLost)
            {
                // Most recently used last.
                _importedSurfaces.RemoveAt(index);
                _importedSurfaces.Add(hit);
                return hit.Image;
            }
            _importedSurfaces.RemoveAt(index);
            await hit.Image.DisposeAsync();
        }

        ICompositionImportedGpuImage image = _interop!.ImportImage(
            new PlatformHandle(frame.Surface, KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef),
            new PlatformGraphicsExternalImageProperties
            {
                Width = frame.SurfaceWidth,
                Height = frame.SurfaceHeight,
                Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                TopLeftOrigin = true,
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
        _importedSurfaces.Add(new ImportedSurface(frame.Surface, frame.SurfaceId, image));
        while (_importedSurfaces.Count > MaximumImportedSurfaces)
        {
            ImportedSurface oldest = _importedSurfaces[0];
            _importedSurfaces.RemoveAt(0);
            // An in-flight update may still read it: wait for the compositor first.
            if (_inFlight.Any(entry => entry.Frame.Surface == oldest.Surface))
                _ = DisposeAfterAsync(oldest.Image, _timeline);
            else
                await oldest.Image.DisposeAsync();
        }
        return image;
    }

    private async Task DisposeAfterAsync(ICompositionImportedGpuImage image, ulong doneValue)
    {
        while (!_disposed && _done is not null && _done.SignaledValue < doneValue)
            await Task.Delay(16);
        await image.DisposeAsync();
    }

    /// <summary>Returns the pixel buffers the compositor has finished reading.</summary>
    private void ReleaseCompletedFrames()
    {
        if (_done is null)
            return;
        ulong completed = _done.SignaledValue;
        while (_inFlight.TryPeek(out var entry) && entry.Done <= completed)
        {
            _inFlight.Dequeue();
            _bridge?.Release(entry.Frame);
        }
    }

    private void DisableGpuForFallback(string reason)
    {
        if (!_gpuEnabled)
            return;
        _bridge?.DisableSurfaceFrames();
        ReleaseGpuResources();
        ReportStatus($"IOSurface {reason}; using CPU BGRA fallback for the next video");
        _host.InvalidateVisual();
    }

    private void ReleaseGpuResources()
    {
        _gpuEnabled = false;
        ElementComposition.SetElementChildVisual(_host, null);
        foreach (ImportedSurface entry in _importedSurfaces)
            _ = entry.Image.DisposeAsync();
        _importedSurfaces.Clear();
        // The visuals are detached and no further updates run; frames still
        // queued may be read by an update already submitted, so release them
        // once the compositor has signaled them or the wait times out.
        var pending = _inFlight.ToArray();
        _inFlight.Clear();
        MetalSharedEvent? done = _done;
        IOSurfacePluginBridge? bridge = _bridge;
        if (pending.Length != 0 && bridge is not null)
            _ = ReleaseWhenDoneAsync(bridge, done, pending);
        _drawingSurface?.Dispose();
        _drawingSurface = null;
        _surfaceVisual = null;
        _videoClip = null;
        _ = _readySemaphore?.DisposeAsync();
        _ = _doneSemaphore?.DisposeAsync();
        _readySemaphore = _doneSemaphore = null;
        _interop = null;
        if (pending.Length == 0)
        {
            _ready?.Dispose();
            _done?.Dispose();
        }
        else
        {
            _ready?.Dispose();
        }
        _ready = _done = null;
    }

    private static async Task ReleaseWhenDoneAsync(
        IOSurfacePluginBridge bridge, MetalSharedEvent? done,
        (IOSurfacePluginBridge.SurfaceFrame Frame, ulong Done)[] pending)
    {
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (done is not null && done.SignaledValue < pending[^1].Done && DateTime.UtcNow < deadline)
                await Task.Delay(16);
        }
        finally
        {
            foreach (var entry in pending)
                bridge.Release(entry.Frame);
            done?.Dispose();
        }
    }

    public void UpdateLayout()
    {
        if (_surfaceVisual is null || _videoClip is null || _videoWidth <= 0 || _videoHeight <= 0)
            return;
        _videoClip.Opacity = _geometry.Active != 0 ? 1 : 0;
        VideoSurfaceLayout layout = CurrentVideoLayout;
        _videoClip.Size = new Vector(layout.Destination.Width, layout.Destination.Height);
        _videoClip.Offset = new Vector3D(layout.Destination.X, layout.Destination.Y, 0);
        // The imported surface can be larger than the visible picture; place
        // the picture over the full-image rectangle and let the clip hide the rest.
        double scaleX = layout.FullImage.Width / _videoWidth;
        double scaleY = layout.FullImage.Height / _videoHeight;
        int surfaceWidth = Math.Max(_surfaceWidth, _videoWidth);
        int surfaceHeight = Math.Max(_surfaceHeight, _videoHeight);
        _surfaceVisual.Size = new Vector(surfaceWidth * scaleX, surfaceHeight * scaleY);
        _surfaceVisual.Offset = new Vector3D(layout.FullImage.X - _visibleX * scaleX, layout.FullImage.Y - _visibleY * scaleY, 0);
    }

    public void Render(DrawingContext context)
    {
        if (_gpuEnabled || _bitmap is null || _geometry.Active == 0)
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
        // Stop new frames first, then hand back everything still held.
        _bridge?.DisableSurfaceFrames();
        ReleaseGpuResources();
        _bridge?.Dispose();
        _bridge = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _subtitle?.Image.Dispose();
        _subtitle = null;
    }
}

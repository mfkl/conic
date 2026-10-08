using VLCAvalonia.Interop.Windows;
using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Player.Contracts;
using VLCAvalonia.Controls;
using VLCAvalonia.Interop;

namespace VLCAvalonia.Video.Windows;

/// <summary>Windows VLC vout bridge, adapter selection and keyed-mutex presentation.</summary>
internal sealed class D3D11VideoBackend : IVideoBackend
{
    private readonly Control _host;
    private readonly VideoBackendOptions _options;
    private D3D11PluginBridge? _bridge;
    private bool _disposed;
    private WriteableBitmap? _bitmap;
    private ICompositionGpuInterop? _GPUInterop;
    private ICompositionImportedGpuImage? _importedImage;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _surfaceVisual;
    private CompositionContainerVisual? _videoClip;
    private VideoGeometry _geometry;
    private VideoSubtitle? _subtitle;
    private VideoSurfaceLayout _notifiedLayout;
    internal VideoSurfaceLayout CurrentVideoLayout => VideoSurfaceLayout.Calculate(_host.Bounds.Size, new Size(_videoWidth, _videoHeight), _geometry, TopLevel.GetTopLevel(_host)?.RenderScaling ?? 1);
    private ulong _importedGeneration;
    private D3D11PluginBridge.AdapterLuid? _compositorAdapter;
    private int _videoWidth;
    private int _videoHeight;
    private bool _GPUUpdatePending;
    private bool _GPUEnabled;
    private bool _reportedFirstGPUFrame;
    private bool _reportedFirstCPUFrame;

    public D3D11VideoBackend(Control host, VideoBackendOptions options)
    {
        _host = host;
        _options = options;
        _bridge = new D3D11PluginBridge();
        try { _bridge.ClearAdapterSelection(); }
        catch
        {
            _bridge.Dispose();
            throw;
        }
    }

    public event Action<string>? StatusChanged;
    public event Action<ulong, ulong, string>? GpuFramePresented;
    public event Action? PresentationChanged;
    public VideoGeometry Geometry => _geometry;
    public VideoSubtitle? Subtitle => _subtitle;
    public Size VideoSize => new(_videoWidth, _videoHeight);
    public bool HasCpuFrame => _reportedFirstCPUFrame;
    public bool HasGpuFrame => _reportedFirstGPUFrame;

    public IMediaEngine CreatePlayer(IReadOnlyList<string> audioStartupArguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativePlaybackSupport.IsAvailable)
            throw new PlatformNotSupportedException(NativePlaybackSupport.CurrentUnavailableReason);
        return new VlcCorePlayer(audioStartupArguments: audioStartupArguments);
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReportStatus("Managed vout ready (initializing D3D11 sharing)");
        try
        {
            CompositionVisual? elementVisual =
                ElementComposition.GetElementVisual(_host);
            if (elementVisual is null)
                throw new InvalidOperationException(
                    "Avalonia did not expose a compositor visual.");

            ICompositionGpuInterop? interop = await elementVisual.Compositor
                .TryGetCompositionGpuInterop();
            if (_disposed)
                return;
            if (interop is null || interop.IsLost ||
                !interop.SupportedImageHandleTypes.Contains(
                    KnownPlatformGraphicsExternalImageHandleTypes
                        .D3D11TextureGlobalSharedHandle))
            {
                ReportStatus(
                    "D3D11 shared images unavailable; using CPU BGRA fallback");
                return;
            }

            D3D11PluginBridge.AdapterLuid compositorAdapter =
                ParseAdapterLuid(interop.DeviceLuid);

            CompositionGpuImportedImageSynchronizationCapabilities sync =
                interop.GetSynchronizationCapabilities(
                    KnownPlatformGraphicsExternalImageHandleTypes
                        .D3D11TextureGlobalSharedHandle);
            if ((sync & CompositionGpuImportedImageSynchronizationCapabilities
                    .KeyedMutex) == 0)
            {
                ReportStatus(
                    "Keyed-mutex import unavailable; using CPU BGRA fallback");
                return;
            }

            _GPUInterop = interop;
            _compositorAdapter = compositorAdapter;
            _drawingSurface = elementVisual.Compositor.CreateDrawingSurface();
            _surfaceVisual = elementVisual.Compositor.CreateSurfaceVisual();
            _surfaceVisual.Surface = _drawingSurface;
            _surfaceVisual.ClipToBounds = true;
            _videoClip = elementVisual.Compositor.CreateContainerVisual();
            _videoClip.ClipToBounds = true;
            _videoClip.Children.Add(_surfaceVisual);
            ElementComposition.SetElementChildVisual(_host, _videoClip);
            D3D11PluginBridge.AdapterLuid requestedAdapter =
                _options.ForceUnavailableAdapter
                    ? new D3D11PluginBridge.AdapterLuid(
                        compositorAdapter.LowPart ^ uint.MaxValue,
                        compositorAdapter.HighPart ^ int.MinValue)
                    : compositorAdapter;
            _bridge?.ConfigureAdapter(requestedAdapter);
            _GPUEnabled = true;
            _bridge?.EnableGPUFrames();
            ReportStatus(
                "D3D11 shared-texture composition active; " +
                $"Avalonia adapter={compositorAdapter}");
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            _bridge?.ClearAdapterSelection();
            _bridge?.DisableGPUFrames();
            ReleaseGpuResources();
            ReportStatus($"GPU composition unavailable: {exception.Message}");
        }
    }

    private static D3D11PluginBridge.AdapterLuid ParseAdapterLuid(
        byte[]? bytes)
    {
        if (bytes is null || bytes.Length != 8)
        {
            throw new InvalidOperationException(
                "Avalonia compositor did not expose an 8-byte D3D adapter LUID.");
        }

        return new D3D11PluginBridge.AdapterLuid(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)));
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
        if (_bridge?.TryTakeLatestSubtitle(out D3D11PluginBridge.Subtitle? subtitle) != true || subtitle is null)
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
        ProcessLatestTelemetry();
        UpdateLayout();
        if (_bridge is not null)
        {
            var geometry = _bridge.GetVideoGeometry();
            if (!_geometry.Equals(geometry))
            {
                _geometry = geometry;
                UpdateLayout();
                _host.InvalidateVisual();
            }
        }

        if (_GPUEnabled && !_GPUUpdatePending &&
            _bridge?.TryTakeLatestGPU(
                out D3D11PluginBridge.GPUFrame? GPUFrame) == true &&
            GPUFrame is not null)
        {
            await PresentGPUFrameAsync(GPUFrame);
            return;
        }

        if (_bridge?.TryTakeLatest(out D3D11PluginBridge.Frame? frame)
            != true || frame is null)
        {
            return;
        }

        if (_bitmap is null ||
            _bitmap.PixelSize.Width != frame.Width ||
            _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        CopyFrameToBitmap(frame, _bitmap);
        _videoWidth = frame.Width;
        _videoHeight = frame.Height;

        if (!_reportedFirstCPUFrame)
        {
            _reportedFirstCPUFrame = true;
            ReportStatus("CPU BGRA fallback frame presented");
        }

        _host.InvalidateVisual();
    }

    private void ProcessLatestTelemetry()
    {
        if (_bridge?.TryTakeLatestTelemetry(
                out D3D11PluginBridge.DeviceTelemetry? telemetry) !=
            true || telemetry is null)
        {
            return;
        }

        bool adapterMatches =
            _compositorAdapter is { } compositorAdapter &&
            telemetry.ActualAdapter == compositorAdapter;
        Console.WriteLine(
            "[composition-telemetry] " +
            $"avalonia-adapter-luid={_compositorAdapter?.ToString() ?? "none"} " +
            $"producer-adapter-luid={telemetry.ActualAdapter?.ToString() ?? "none"} " +
            $"adapter-match={adapterMatches.ToString().ToLowerInvariant()}");

        if (telemetry.FallbackReason != "none" || !adapterMatches)
        {
            DisableGPUForFallback(
                telemetry.FallbackReason != "none"
                    ? telemetry.FallbackReason
                    : "adapter-mismatch");
        }
    }

    private void DisableGPUForFallback(string reason)
    {
        if (!_GPUEnabled)
            return;
        _GPUEnabled = false;
        _bridge?.DisableGPUFrames();
        ReleaseGpuResources();
        ReportStatus($"D3D11 producer fallback={reason}; using CPU BGRA fallback");
        _host.InvalidateVisual();
    }

    private void ReleaseGpuResources()
    {
        _GPUEnabled = false;
        ElementComposition.SetElementChildVisual(_host, null);
        if (_importedImage is not null)
        {
            _ = _importedImage.DisposeAsync();
            _importedImage = null;
        }
        _drawingSurface?.Dispose();
        _drawingSurface = null;
        _surfaceVisual = null;
        _videoClip = null;
        _GPUInterop = null;
        _importedGeneration = 0;
    }

    private static unsafe void CopyFrameToBitmap(
        D3D11PluginBridge.Frame frame,
        WriteableBitmap bitmap)
    {
        using ILockedFramebuffer locked = bitmap.Lock();
        int copyBytes = Math.Min(frame.RowBytes, locked.RowBytes);
        for (int row = 0; row < frame.Height; row++)
        {
            frame.Pixels.AsSpan(row * frame.RowBytes, copyBytes).CopyTo(
                new Span<byte>(
                    (void*)(locked.Address + row * locked.RowBytes),
                    copyBytes));
        }
    }

    private async Task PresentGPUFrameAsync(
        D3D11PluginBridge.GPUFrame frame)
    {
        if (_GPUInterop is null || _drawingSurface is null ||
            _surfaceVisual is null)
        {
            return;
        }

        if (_compositorAdapter is null ||
            frame.Adapter != _compositorAdapter.Value)
        {
            DisableGPUForFallback("adapter-mismatch");
            return;
        }

        _GPUUpdatePending = true;
        try
        {
            if (_options.ProbeSharedTexture && !_reportedFirstGPUFrame)
            {
                string probe =
                    D3D11SharedTextureProbe.OpenAndSynchronize(frame);
                Console.WriteLine(
                    $"[composition-ui-smoke] producer-probe={probe}");
            }

            if (_importedImage is null ||
                _importedGeneration != frame.ResourceGeneration)
            {
                ICompositionImportedGpuImage? oldImage = _importedImage;
                ICompositionImportedGpuImage importedImage = _GPUInterop.ImportImage(
                    new PlatformHandle(
                        frame.SharedHandle,
                        KnownPlatformGraphicsExternalImageHandleTypes
                            .D3D11TextureGlobalSharedHandle),
                    new PlatformGraphicsExternalImageProperties
                    {
                        Width = frame.Width,
                        Height = frame.Height,
                        Format = PlatformGraphicsExternalImageFormat
                            .B8G8R8A8UNorm,
                        TopLeftOrigin = true,
                    });
                _importedImage = importedImage;
                try
                {
                    await importedImage.ImportCompleted;
                    if (_disposed || !_GPUEnabled || !ReferenceEquals(_importedImage, importedImage))
                        return;
                    _importedGeneration = frame.ResourceGeneration;
                }
                finally
                {
                    if (oldImage is not null)
                        await oldImage.DisposeAsync();
                }
            }

            if (_disposed || !_GPUEnabled || _drawingSurface is null || _importedImage is null)
                return;
            _videoWidth = frame.Width;
            _videoHeight = frame.Height;
            UpdateLayout();
            await _drawingSurface.UpdateWithKeyedMutexAsync(
                _importedImage,
                1,
                0);
            if (_disposed || !_GPUEnabled) return;
            GpuFramePresented?.Invoke(
                frame.DeviceGeneration,
                frame.ResourceGeneration,
                frame.Adapter.ToString());
            if (!_reportedFirstGPUFrame)
            {
                _reportedFirstGPUFrame = true;
                ReportStatus("D3D11 frame presented by Avalonia compositor");
            }
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            DisableGPUForFallback("presentation-failed");
            ReportStatus($"D3D11 presentation failed: {exception.Message}");
        }
        finally
        {
            _GPUUpdatePending = false;
        }
    }

    public void UpdateLayout()
    {
        if (_surfaceVisual is null || _videoClip is null || _videoWidth <= 0 || _videoHeight <= 0)
            return;
        _videoClip.Opacity = _geometry.Active != 0 ? 1 : 0;
        var layout = CurrentVideoLayout;
        _videoClip.Size = new Vector(layout.Destination.Width, layout.Destination.Height);
        _videoClip.Offset = new Vector3D(layout.Destination.X, layout.Destination.Y, 0);
        _surfaceVisual.Size = new Vector(layout.FullImage.Width, layout.FullImage.Height);
        _surfaceVisual.Offset = new Vector3D(layout.FullImage.X, layout.FullImage.Y, 0);
    }

    public void Render(DrawingContext context)
    {
        if (_GPUEnabled || _bitmap is null || _geometry.Active == 0)
            return;

        var layout = CurrentVideoLayout;
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
        _bridge?.Dispose();
        _bridge = null;
        ReleaseGpuResources();
        _compositorAdapter = null;
        _reportedFirstGPUFrame = false;
        _reportedFirstCPUFrame = false;
        _bitmap?.Dispose();
        _bitmap = null;
        _subtitle?.Image.Dispose();
        _subtitle = null;
    }
}

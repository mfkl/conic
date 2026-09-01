using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Contracts;
using Conic.Interop;

namespace Conic.Controls;

public sealed class CompositionVideoSurface : Control, IDisposable
{
    public static readonly StyledProperty<string?> MediaPathProperty =
        AvaloniaProperty.Register<CompositionVideoSurface, string?>(
            nameof(MediaPath));

    private readonly DispatcherTimer _frameTimer;
    private CompositionPluginBridge? _bridge;
    private IMediaEngine? _player;
    private WriteableBitmap? _bitmap;
    private ICompositionGpuInterop? _GPUInterop;
    private ICompositionImportedGpuImage? _importedImage;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _surfaceVisual;
    private ulong _importedGeneration;
    private int _videoWidth;
    private int _videoHeight;
    private bool _GPUUpdatePending;
    private bool _GPUEnabled;
    private bool _reportedFirstGPUFrame;
    private bool _attached;

    public event Action<string>? StatusChanged;
    public event EventHandler<MediaEngineState>? EngineStateChanged;

    public string? MediaPath
    {
        get => GetValue(MediaPathProperty);
        set => SetValue(MediaPathProperty, value);
    }

    public CompositionVideoSurface()
    {
        ClipToBounds = true;
        MediaPathProperty.Changed.AddClassHandler<CompositionVideoSurface>(
            static (surface, args) =>
                surface.OnMediaPathChanged(
                    ((AvaloniaPropertyChangedEventArgs<string?>)args)
                    .NewValue.Value));

        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _frameTimer.Tick += async (_, _) =>
            await PresentLatestFrameAsync();
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        try
        {
            _bridge = new CompositionPluginBridge();
            _player = new VLCCorePlayer();
            _player.StateChanged += OnEngineStateChanged;
            _ = InitializeGPUCompositionAsync();
            _frameTimer.Start();
            if (!string.IsNullOrWhiteSpace(MediaPath))
                _player.Open(MediaPath);
            ReportStatus("Managed vout ready (initializing D3D11 sharing)");
        }
        catch (Exception exception)
        {
            ReportStatus(exception.Message);
        }
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameTimer.Stop();
        DisposePipeline();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMediaPathChanged(string? path)
    {
        if (_attached && _player is not null &&
            !string.IsNullOrWhiteSpace(path))
        {
            _player.Open(path);
        }
    }

    public void TogglePause() => _player?.TogglePause();

    public void Seek(double normalizedPosition) =>
        _player?.Seek(normalizedPosition);

    public void SetVolume(double normalizedVolume) =>
        _player?.SetVolume(normalizedVolume);

    public void Stop() => _player?.Stop();

    private void OnEngineStateChanged(
        object? sender,
        MediaEngineState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            EngineStateChanged?.Invoke(this, state);
            string status = state.Status switch
            {
                PlaybackStatus.Faulted => state.Error ?? "Playback failed",
                PlaybackStatus.Opening => "Opening media",
                PlaybackStatus.Playing => "Playing",
                PlaybackStatus.Paused => "Paused",
                PlaybackStatus.Stopped => "Stopped",
                _ => "Ready",
            };
            StatusChanged?.Invoke(status);
        });
    }

    private async Task InitializeGPUCompositionAsync()
    {
        try
        {
            CompositionVisual? elementVisual =
                ElementComposition.GetElementVisual(this);
            if (elementVisual is null)
                throw new InvalidOperationException(
                    "Avalonia did not expose a compositor visual.");

            ICompositionGpuInterop? interop = await elementVisual.Compositor
                .TryGetCompositionGpuInterop();
            if (!_attached)
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
            _drawingSurface = elementVisual.Compositor.CreateDrawingSurface();
            _surfaceVisual = elementVisual.Compositor.CreateSurfaceVisual();
            _surfaceVisual.Surface = _drawingSurface;
            _surfaceVisual.ClipToBounds = true;
            ElementComposition.SetElementChildVisual(this, _surfaceVisual);
            _GPUEnabled = true;
            _bridge?.EnableGPUFrames();
            ReportStatus("D3D11 shared-texture composition active");
        }
        catch (Exception exception)
        {
            ReportStatus($"GPU composition unavailable: {exception.Message}");
        }
    }

    private async Task PresentLatestFrameAsync()
    {
        if (_GPUEnabled && !_GPUUpdatePending &&
            _bridge?.TryTakeLatestGPU(
                out CompositionPluginBridge.GPUFrame? GPUFrame) == true &&
            GPUFrame is not null)
        {
            await PresentGPUFrameAsync(GPUFrame);
            return;
        }

        if (_bridge?.TryTakeLatest(out CompositionPluginBridge.Frame? frame)
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

        InvalidateVisual();
    }

    private static unsafe void CopyFrameToBitmap(
        CompositionPluginBridge.Frame frame,
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
        CompositionPluginBridge.GPUFrame frame)
    {
        if (_GPUInterop is null || _drawingSurface is null ||
            _surfaceVisual is null)
        {
            return;
        }

        _GPUUpdatePending = true;
        try
        {
            if (_importedImage is null ||
                _importedGeneration != frame.Generation)
            {
                ICompositionImportedGpuImage? oldImage = _importedImage;
                _importedImage = _GPUInterop.ImportImage(
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
                await _importedImage.ImportCompleted;
                _importedGeneration = frame.Generation;
                if (oldImage is not null)
                    await oldImage.DisposeAsync();
            }

            _videoWidth = frame.Width;
            _videoHeight = frame.Height;
            UpdateSurfaceLayout();
            await _drawingSurface.UpdateWithKeyedMutexAsync(
                _importedImage,
                1,
                0);
            if (!_reportedFirstGPUFrame)
            {
                _reportedFirstGPUFrame = true;
                ReportStatus("D3D11 frame presented by Avalonia compositor");
            }
        }
        catch (Exception exception)
        {
            _GPUEnabled = false;
            _bridge?.DisableGPUFrames();
            ReportStatus($"D3D11 presentation failed: {exception.Message}");
            InvalidateVisual();
        }
        finally
        {
            _GPUUpdatePending = false;
        }
    }

    private void UpdateSurfaceLayout()
    {
        if (_surfaceVisual is null || _videoWidth <= 0 || _videoHeight <= 0)
            return;

        double scale = Math.Min(
            Bounds.Width / _videoWidth,
            Bounds.Height / _videoHeight);
        double width = _videoWidth * scale;
        double height = _videoHeight * scale;
        _surfaceVisual.Size = new Vector(width, height);
        _surfaceVisual.Offset = new Vector3D(
            (Bounds.Width - width) / 2,
            (Bounds.Height - height) / 2,
            0);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, Bounds);
        if (_GPUEnabled || _bitmap is null)
            return;

        Rect source = new(_bitmap.Size);
        double scale = Math.Min(
            Bounds.Width / source.Width,
            Bounds.Height / source.Height);
        Size size = new(source.Width * scale, source.Height * scale);
        Rect destination = new(
            (Bounds.Width - size.Width) / 2,
            (Bounds.Height - size.Height) / 2,
            size.Width,
            size.Height);
        context.DrawImage(_bitmap, source, destination);
    }

    private void ReportStatus(string status)
    {
        Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(status));
    }

    private void DisposePipeline()
    {
        if (_player is not null)
        {
            _player.StateChanged -= OnEngineStateChanged;
            _player.Dispose();
            _player = null;
        }
        _bridge?.Dispose();
        _bridge = null;
        ElementComposition.SetElementChildVisual(this, null);
        if (_importedImage is not null)
        {
            _ = _importedImage.DisposeAsync();
            _importedImage = null;
        }
        _drawingSurface?.Dispose();
        _drawingSurface = null;
        _surfaceVisual = null;
        _GPUInterop = null;
        _GPUEnabled = false;
        _reportedFirstGPUFrame = false;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        _frameTimer.Stop();
        DisposePipeline();
        GC.SuppressFinalize(this);
    }
}

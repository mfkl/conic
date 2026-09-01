using System.Buffers.Binary;
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
    private CompositionPluginBridge.AdapterLuid? _compositorAdapter;
    private int _videoWidth;
    private int _videoHeight;
    private bool _GPUUpdatePending;
    private bool _GPUEnabled;
    private bool _reportedFirstGPUFrame;
    private bool _reportedFirstCPUFrame;
    private bool _attached;

    internal bool ForceUnavailableAdapterForSmoke { get; set; }

    internal bool ProbeSharedTextureForSmoke { get; set; }

    public event Action<string>? StatusChanged;
    public event EventHandler<MediaEngineState>? EngineStateChanged;
    internal event Action<ulong, ulong, string>? GPUFramePresentedForSmoke;

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
            _bridge.ClearAdapterSelection();
            _ = InitializeGPUCompositionAsync();
            _frameTimer.Start();
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

    public void SetMuted(bool muted) => _player?.SetMuted(muted);

    public void Skip(TimeSpan offset) => _player?.Skip(offset);

    public void SetPlaybackRate(double playbackRate) => _player?.SetPlaybackRate(playbackRate);

    public void Stop() => _player?.Stop();

    internal void ReopenForSmoke()
    {
        if (_player is null || string.IsNullOrWhiteSpace(MediaPath))
            throw new InvalidOperationException(
                "The smoke player has no media to reopen.");
        _player.Open(MediaPath);
    }

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

            CompositionPluginBridge.AdapterLuid compositorAdapter =
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
            ElementComposition.SetElementChildVisual(this, _surfaceVisual);
            CompositionPluginBridge.AdapterLuid requestedAdapter =
                ForceUnavailableAdapterForSmoke
                    ? new CompositionPluginBridge.AdapterLuid(
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
            _bridge?.ClearAdapterSelection();
            ReportStatus($"GPU composition unavailable: {exception.Message}");
        }
        finally
        {
            StartPlayerAfterAdapterSelection();
        }
    }

    private static CompositionPluginBridge.AdapterLuid ParseAdapterLuid(
        byte[]? bytes)
    {
        if (bytes is null || bytes.Length != 8)
        {
            throw new InvalidOperationException(
                "Avalonia compositor did not expose an 8-byte D3D adapter LUID.");
        }

        return new CompositionPluginBridge.AdapterLuid(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)));
    }

    private void StartPlayerAfterAdapterSelection()
    {
        if (!_attached || _player is not null)
            return;

        try
        {
            _player = new VLCCorePlayer();
            _player.StateChanged += OnEngineStateChanged;
            if (!string.IsNullOrWhiteSpace(MediaPath))
                _player.Open(MediaPath);
        }
        catch (Exception exception)
        {
            ReportStatus($"VLC startup failed: {exception.Message}");
        }
    }

    private async Task PresentLatestFrameAsync()
    {
        ProcessLatestTelemetry();

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

        if (!_reportedFirstCPUFrame)
        {
            _reportedFirstCPUFrame = true;
            ReportStatus("CPU BGRA fallback frame presented");
        }

        InvalidateVisual();
    }

    private void ProcessLatestTelemetry()
    {
        if (_bridge?.TryTakeLatestTelemetry(
                out CompositionPluginBridge.DeviceTelemetry? telemetry) !=
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
        _importedGeneration = 0;
        ReportStatus($"D3D11 producer fallback={reason}; using CPU BGRA fallback");
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

        if (_compositorAdapter is null ||
            frame.Adapter != _compositorAdapter.Value)
        {
            DisableGPUForFallback("adapter-mismatch");
            return;
        }

        _GPUUpdatePending = true;
        try
        {
            if (ProbeSharedTextureForSmoke && !_reportedFirstGPUFrame)
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
                _importedGeneration = frame.ResourceGeneration;
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
            GPUFramePresentedForSmoke?.Invoke(
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
        _compositorAdapter = null;
        _importedGeneration = 0;
        _GPUEnabled = false;
        _reportedFirstGPUFrame = false;
        _reportedFirstCPUFrame = false;
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

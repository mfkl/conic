using TerraFX.Interop;
using VLCLR.Native;
using static TerraFX.Interop.Windows;

namespace VLCAvalonia.Vout;

/// <summary>
/// Converts VLC-owned NV12/P010 texture-array slices to the shareable BGRA
/// surface without mapping either resource into CPU memory.
/// </summary>
internal sealed unsafe class D3D11VideoProcessorConverter : IDisposable
{
    private ID3D11Device* _device;
    private ID3D11VideoDevice* _videoDevice;
    private ID3D11VideoContext* _videoContext;
    private ID3D11VideoProcessorEnumerator* _enumerator;
    private ID3D11VideoProcessor* _processor;
    private ID3D11VideoProcessorOutputView* _outputView;
    private DXGI_FORMAT _inputFormat;
    private uint _inputWidth;
    private uint _inputHeight;
    private uint _outputWidth;
    private uint _outputHeight;
    private ulong _outputGeneration;

    public D3D11VideoProcessorConverter(
        ID3D11Device* device,
        ID3D11DeviceContext* context)
    {
        _device = device;
        _device->AddRef();
        try
        {
            ID3D11VideoDevice* videoDevice = null;
            Guid videoDeviceId = IID_ID3D11VideoDevice;
            Check(
                device->QueryInterface(
                    &videoDeviceId,
                    (void**)&videoDevice),
                "QueryInterface(ID3D11VideoDevice)");
            _videoDevice = videoDevice;

            ID3D11VideoContext* videoContext = null;
            Guid videoContextId = IID_ID3D11VideoContext;
            Check(
                context->QueryInterface(
                    &videoContextId,
                    (void**)&videoContext),
                "QueryInterface(ID3D11VideoContext)");
            _videoContext = videoContext;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Convert(
        VLCD3D11PictureSys* pictureSystem,
        in VLCVideoFormat format,
        D3D11SharedTexture destination,
        out uint outputWidth,
        out uint outputHeight)
    {
        outputWidth = format.VisibleWidth == 0
            ? format.Width
            : format.VisibleWidth;
        outputHeight = format.VisibleHeight == 0
            ? format.Height
            : format.VisibleHeight;
        if (IsQuarterTurn(format.Orientation))
            (outputWidth, outputHeight) = (outputHeight, outputWidth);
        if (outputWidth == 0 || outputHeight == 0)
            return false;
        if (pictureSystem is null || pictureSystem->Textures.Texture0 == 0)
            return false;

        DXGI_FORMAT inputFormat = (DXGI_FORMAT)pictureSystem->FormatTexture;
        if (inputFormat is not DXGI_FORMAT.DXGI_FORMAT_NV12 and
            not DXGI_FORMAT.DXGI_FORMAT_P010)
        {
            throw new D3D11AdapterException(
                D3D11AdapterFailure.VideoProcessorConversionFailed,
                unchecked((int)0x80070057),
                $"Unsupported opaque input format {inputFormat}");
        }

        // VLC 3 exposes mastering metadata but this legacy D3D11 processor
        // API cannot express HDR metadata or a deterministic SDR tone-map.
        if (format.Transfer is 6 or 8)
        {
            throw new D3D11AdapterException(
                D3D11AdapterFailure.VideoProcessorConversionFailed,
                unchecked((int)0x80070032),
                "HDR transfer (PQ/HLG) is not supported by the SDR BGRA path");
        }

        ID3D11Texture2D* source =
            (ID3D11Texture2D*)pictureSystem->Textures.Texture0;
        D3D11_TEXTURE2D_DESC sourceDescription;
        source->GetDesc(&sourceDescription);
        if (pictureSystem->SliceIndex >= sourceDescription.ArraySize)
            return false;

        destination.EnsureTexture(outputWidth, outputHeight);
        EnsureProcessor(
            sourceDescription.Width,
            sourceDescription.Height,
            inputFormat,
            outputWidth,
            outputHeight,
            destination);

        ID3D11VideoProcessorInputView* inputView = null;
        bool acquired = false;
        try
        {
            D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription = new()
            {
                FourCC = 0,
                ViewDimension = D3D11_VPIV_DIMENSION
                    .D3D11_VPIV_DIMENSION_TEXTURE2D,
            };
            inputDescription.Texture2D.MipSlice = 0;
            inputDescription.Texture2D.ArraySlice =
                pictureSystem->SliceIndex;
            Check(
                _videoDevice->CreateVideoProcessorInputView(
                    (ID3D11Resource*)source,
                    _enumerator,
                    &inputDescription,
                    &inputView),
                "ID3D11VideoDevice.CreateVideoProcessorInputView");

            RECT sourceRectangle = CreateSourceRectangle(
                in format,
                sourceDescription.Width,
                sourceDescription.Height);
            RECT destinationRectangle = new()
            {
                left = 0,
                top = 0,
                right = checked((int)outputWidth),
                bottom = checked((int)outputHeight),
            };
            _videoContext->VideoProcessorSetStreamFrameFormat(
                _processor,
                0,
                D3D11_VIDEO_FRAME_FORMAT
                    .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
            D3D11_VIDEO_PROCESSOR_COLOR_SPACE inputColorSpace =
                CreateInputColorSpace(in format);
            _videoContext->VideoProcessorSetStreamColorSpace(
                _processor,
                0,
                &inputColorSpace);
            D3D11_VIDEO_PROCESSOR_COLOR_SPACE outputColorSpace = default;
            outputColorSpace.RGB_Range = 0; // full-range RGB
            outputColorSpace.Nominal_Range = 2; // 0-255
            _videoContext->VideoProcessorSetOutputColorSpace(
                _processor,
                &outputColorSpace);
            _videoContext->VideoProcessorSetStreamSourceRect(
                _processor,
                0,
                1,
                &sourceRectangle);
            _videoContext->VideoProcessorSetStreamDestRect(
                _processor,
                0,
                1,
                &destinationRectangle);
            ApplyRotation(format.Orientation);

            if (!destination.TryBeginProducerWrite())
                return false;
            acquired = true;
            D3D11_VIDEO_PROCESSOR_STREAM stream = new()
            {
                Enable = 1,
                pInputSurface = inputView,
            };
            Check(
                _videoContext->VideoProcessorBlt(
                    _processor,
                    _outputView,
                    0,
                    1,
                    &stream),
                "ID3D11VideoContext.VideoProcessorBlt");
            return true;
        }
        finally
        {
            if (acquired)
                destination.EndProducerWrite();
            if (inputView is not null)
                inputView->Release();
        }
    }

    private void EnsureProcessor(
        uint inputWidth,
        uint inputHeight,
        DXGI_FORMAT inputFormat,
        uint outputWidth,
        uint outputHeight,
        D3D11SharedTexture destination)
    {
        bool processorMatches = _processor is not null &&
            _inputWidth == inputWidth &&
            _inputHeight == inputHeight &&
            _inputFormat == inputFormat &&
            _outputWidth == outputWidth &&
            _outputHeight == outputHeight;
        if (!processorMatches)
        {
            ReleaseProcessor();
            D3D11_VIDEO_PROCESSOR_CONTENT_DESC content = new()
            {
                InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT
                    .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
                InputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1,
                },
                InputWidth = inputWidth,
                InputHeight = inputHeight,
                OutputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1,
                },
                OutputWidth = outputWidth,
                OutputHeight = outputHeight,
                Usage = D3D11_VIDEO_USAGE.D3D11_VIDEO_USAGE_PLAYBACK_NORMAL,
            };
            ID3D11VideoProcessorEnumerator* enumerator = null;
            Check(
                _videoDevice->CreateVideoProcessorEnumerator(
                    &content,
                    &enumerator),
                "ID3D11VideoDevice.CreateVideoProcessorEnumerator");
            _enumerator = enumerator;

            uint inputSupport = 0;
            Check(
                _enumerator->CheckVideoProcessorFormat(
                    inputFormat,
                    &inputSupport),
                $"CheckVideoProcessorFormat({inputFormat})");
            if ((inputSupport & (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                    .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.VideoProcessorCreationFailed,
                    unchecked((int)0x80070032),
                    $"Video processor input support for {inputFormat}");
            }

            uint outputSupport = 0;
            Check(
                _enumerator->CheckVideoProcessorFormat(
                    DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    &outputSupport),
                "CheckVideoProcessorFormat(BGRA8)");
            if ((outputSupport & (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                    .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.VideoProcessorCreationFailed,
                    unchecked((int)0x80070032),
                    "Video processor output support for BGRA8");
            }

            ID3D11VideoProcessor* processor = null;
            Check(
                _videoDevice->CreateVideoProcessor(
                    _enumerator,
                    0,
                    &processor),
                "ID3D11VideoDevice.CreateVideoProcessor");
            _processor = processor;
            _inputWidth = inputWidth;
            _inputHeight = inputHeight;
            _inputFormat = inputFormat;
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
        }

        if (_outputView is not null &&
            _outputGeneration == destination.ResourceGeneration)
        {
            return;
        }

        ReleaseOutputView();
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputDescription = new()
        {
            ViewDimension = D3D11_VPOV_DIMENSION
                .D3D11_VPOV_DIMENSION_TEXTURE2D,
        };
        outputDescription.Texture2D.MipSlice = 0;
        ID3D11VideoProcessorOutputView* outputView = null;
        Check(
            _videoDevice->CreateVideoProcessorOutputView(
                (ID3D11Resource*)destination.Texture,
                _enumerator,
                &outputDescription,
                &outputView),
            "ID3D11VideoDevice.CreateVideoProcessorOutputView");
        _outputView = outputView;
        _outputGeneration = destination.ResourceGeneration;
    }

    private static RECT CreateSourceRectangle(
        in VLCVideoFormat format,
        uint textureWidth,
        uint textureHeight)
    {
        uint x = Math.Min(format.XOffset, textureWidth);
        uint y = Math.Min(format.YOffset, textureHeight);
        uint requestedWidth = format.VisibleWidth == 0
            ? textureWidth - x
            : format.VisibleWidth;
        uint requestedHeight = format.VisibleHeight == 0
            ? textureHeight - y
            : format.VisibleHeight;
        uint right = Math.Min(textureWidth, checked(x + requestedWidth));
        uint bottom = Math.Min(textureHeight, checked(y + requestedHeight));
        if (right <= x || bottom <= y)
            throw new InvalidOperationException("The VLC visible rectangle is empty.");
        return new RECT
        {
            left = checked((int)x),
            top = checked((int)y),
            right = checked((int)right),
            bottom = checked((int)bottom),
        };
    }

    private static D3D11_VIDEO_PROCESSOR_COLOR_SPACE CreateInputColorSpace(
        in VLCVideoFormat format)
    {
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE colorSpace = default;
        colorSpace.Usage = 0; // playback
        colorSpace.YCbCr_Matrix = format.Space is 2 or 3 ? 1u : 0u;
        colorSpace.YCbCr_xvYCC = 0;
        colorSpace.Nominal_Range = format.ColorRangeFull != 0 ? 2u : 1u;
        return colorSpace;
    }

    private void ApplyRotation(int orientation)
    {
        D3D11_VIDEO_PROCESSOR_ROTATION rotation = orientation switch
        {
            0 => D3D11_VIDEO_PROCESSOR_ROTATION
                .D3D11_VIDEO_PROCESSOR_ROTATION_IDENTITY,
            3 => D3D11_VIDEO_PROCESSOR_ROTATION
                .D3D11_VIDEO_PROCESSOR_ROTATION_180,
            5 => D3D11_VIDEO_PROCESSOR_ROTATION
                .D3D11_VIDEO_PROCESSOR_ROTATION_90,
            6 => D3D11_VIDEO_PROCESSOR_ROTATION
                .D3D11_VIDEO_PROCESSOR_ROTATION_270,
            _ => throw new D3D11AdapterException(
                D3D11AdapterFailure.VideoProcessorConversionFailed,
                unchecked((int)0x80070032),
                $"Mirrored VLC orientation {orientation}"),
        };
        _videoContext->VideoProcessorSetStreamRotation(
            _processor,
            0,
            rotation == D3D11_VIDEO_PROCESSOR_ROTATION
                .D3D11_VIDEO_PROCESSOR_ROTATION_IDENTITY ? 0 : 1,
            rotation);
    }

    private static bool IsQuarterTurn(int orientation) =>
        orientation is 5 or 6;

    private void Check(int result, string operation)
    {
        if (result >= 0)
            return;
        int removed = _device is null ? 0 : _device->GetDeviceRemovedReason();
        throw new D3D11AdapterException(
            removed < 0
                ? D3D11AdapterFailure.DeviceRemoved
                : operation.Contains("Create", StringComparison.Ordinal)
                    ? D3D11AdapterFailure.VideoProcessorCreationFailed
                    : D3D11AdapterFailure.VideoProcessorConversionFailed,
            removed < 0 ? removed : result,
            operation);
    }

    private void ReleaseOutputView()
    {
        if (_outputView is not null)
        {
            _outputView->Release();
            _outputView = null;
        }
        _outputGeneration = 0;
    }

    private void ReleaseProcessor()
    {
        ReleaseOutputView();
        if (_processor is not null)
        {
            _processor->Release();
            _processor = null;
        }
        if (_enumerator is not null)
        {
            _enumerator->Release();
            _enumerator = null;
        }
        _inputWidth = 0;
        _inputHeight = 0;
        _outputWidth = 0;
        _outputHeight = 0;
    }

    public void Dispose()
    {
        ReleaseProcessor();
        if (_videoContext is not null)
        {
            _videoContext->Release();
            _videoContext = null;
        }
        if (_videoDevice is not null)
        {
            _videoDevice->Release();
            _videoDevice = null;
        }
        if (_device is not null)
        {
            _device->Release();
            _device = null;
        }
    }
}

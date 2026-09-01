using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace VLCAvalonia.Vout;

internal enum D3D11AdapterFailure
{
    None = 0,
    AdapterNotConfigured = 1,
    RequestedAdapterUnavailable = 2,
    ProducerDeviceCreationFailed = 3,
    AdapterMismatch = 4,
    MultithreadProtectionUnavailable = 5,
    TextureUploadFailed = 6,
    OpaquePoolCreationFailed = 7,
}

internal sealed class D3D11AdapterException : InvalidOperationException
{
    public D3D11AdapterFailure Failure { get; }

    public int NativeResult { get; }

    public D3D11AdapterException(
        D3D11AdapterFailure failure,
        int nativeResult,
        string operation)
        : base($"{operation} failed with HRESULT 0x{nativeResult:X8}.")
    {
        Failure = failure;
        NativeResult = nativeResult;
    }
}

/// <summary>
/// Owns the producer side of a BGRA shared texture on the exact adapter
/// selected by Avalonia. VLC writes key 0 and publishes key 1; Avalonia
/// consumes key 1 and returns key 0.
/// </summary>
internal sealed unsafe class D3D11SharedTexture : IDisposable
{
    // AcquireSync returns the Win32 WAIT_TIMEOUT DWORD, not the similarly
    // named DXGI_ERROR_WAIT_TIMEOUT HRESULT.
    private const int WaitTimeout = 0x00000102;
    private static long s_nextDeviceGeneration;
    private static long s_nextResourceGeneration;

    private ID3D11Device* _device;
    private ID3D11DeviceContext* _context;
    private ID3D11Texture2D* _texture;
    private IDXGIKeyedMutex* _keyedMutex;
    private nint _sharedHandle;
    private uint _width;
    private uint _height;
    private ulong _resourceGeneration;

    public uint AdapterLowPart { get; }

    public int AdapterHighPart { get; }

    public ulong DeviceGeneration { get; }

    public nint SharedHandle => _sharedHandle;

    public ulong ResourceGeneration => _resourceGeneration;

    /// <summary>Borrowed for this producer object's lifetime.</summary>
    internal ID3D11Device* Device => _device;

    /// <summary>Borrowed for this producer object's lifetime.</summary>
    internal ID3D11DeviceContext* Context => _context;

    public D3D11SharedTexture(uint adapterLowPart, int adapterHighPart)
    {
        CreateDevice(adapterLowPart, adapterHighPart);
        AdapterLowPart = adapterLowPart;
        AdapterHighPart = adapterHighPart;
        DeviceGeneration = checked((ulong)Interlocked.Increment(
            ref s_nextDeviceGeneration));
    }

    public bool Upload(
        nint pixels,
        int pitch,
        uint width,
        uint height)
    {
        if (pixels == 0 || pitch <= 0 || width == 0 || height == 0)
            return false;

        EnsureTexture(width, height);
        if (_keyedMutex is null)
            return false;

        int acquireResult = _keyedMutex->AcquireSync(0, 0);
        if (acquireResult == WaitTimeout)
            return false;
        CheckHResult(
            acquireResult,
            D3D11AdapterFailure.TextureUploadFailed,
            "IDXGIKeyedMutex.AcquireSync(producer)");

        try
        {
            _context->UpdateSubresource(
                (ID3D11Resource*)_texture,
                0,
                null,
                (void*)pixels,
                checked((uint)pitch),
                0);
        }
        finally
        {
            CheckHResult(
                _keyedMutex->ReleaseSync(1),
                D3D11AdapterFailure.TextureUploadFailed,
                "IDXGIKeyedMutex.ReleaseSync(producer)");
        }

        return true;
    }

    private void EnsureTexture(uint width, uint height)
    {
        if (_texture is not null && _width == width && _height == height)
            return;

        ReleaseTexture();

        D3D11_TEXTURE2D_DESC description = new()
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(
                D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE |
                D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET),
            CPUAccessFlags = 0,
            MiscFlags = (uint)D3D11_RESOURCE_MISC_FLAG
                .D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX,
        };

        ID3D11Texture2D* texture = null;
        CheckHResult(
            _device->CreateTexture2D(&description, null, &texture),
            D3D11AdapterFailure.TextureUploadFailed,
            "ID3D11Device.CreateTexture2D(shared BGRA)");

        IDXGIKeyedMutex* keyedMutex = null;
        IDXGIResource* resource = null;
        try
        {
            Guid keyedMutexId = IID_IDXGIKeyedMutex;
            CheckHResult(
                texture->QueryInterface(
                    &keyedMutexId,
                    (void**)&keyedMutex),
                D3D11AdapterFailure.TextureUploadFailed,
                "QueryInterface(IDXGIKeyedMutex)");

            Guid resourceId = IID_IDXGIResource;
            CheckHResult(
                texture->QueryInterface(
                    &resourceId,
                    (void**)&resource),
                D3D11AdapterFailure.TextureUploadFailed,
                "QueryInterface(IDXGIResource)");

            nint sharedHandle = 0;
            CheckHResult(
                resource->GetSharedHandle(&sharedHandle),
                D3D11AdapterFailure.TextureUploadFailed,
                "IDXGIResource.GetSharedHandle");
            if (sharedHandle == 0)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.TextureUploadFailed,
                    unchecked((int)0x80004005),
                    "D3D11 shared texture handle creation");
            }

            _texture = texture;
            _keyedMutex = keyedMutex;
            _sharedHandle = sharedHandle;
            _width = width;
            _height = height;
            _resourceGeneration = checked((ulong)Interlocked.Increment(
                ref s_nextResourceGeneration));
            texture = null;
            keyedMutex = null;
        }
        finally
        {
            if (resource is not null)
                resource->Release();
            if (keyedMutex is not null)
                keyedMutex->Release();
            if (texture is not null)
                texture->Release();
        }
    }

    private void CreateDevice(uint adapterLowPart, int adapterHighPart)
    {
        IDXGIFactory4* factory = null;
        IDXGIAdapter* adapter = null;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Multithread* multithread = null;
        try
        {
            Guid factoryId = IID_IDXGIFactory4;
            int result = CreateDXGIFactory1(&factoryId, (void**)&factory);
            CheckHResult(
                result,
                D3D11AdapterFailure.RequestedAdapterUnavailable,
                "CreateDXGIFactory1");

            LUID requested = new()
            {
                LowPart = adapterLowPart,
                HighPart = adapterHighPart,
            };
            Guid adapterId = IID_IDXGIAdapter;
            result = factory->EnumAdapterByLuid(
                requested,
                &adapterId,
                (void**)&adapter);
            CheckHResult(
                result,
                D3D11AdapterFailure.RequestedAdapterUnavailable,
                "IDXGIFactory4.EnumAdapterByLuid");

            DXGI_ADAPTER_DESC adapterDescription;
            result = adapter->GetDesc(&adapterDescription);
            CheckHResult(
                result,
                D3D11AdapterFailure.RequestedAdapterUnavailable,
                "IDXGIAdapter.GetDesc");
            if (adapterDescription.AdapterLuid.LowPart != adapterLowPart ||
                adapterDescription.AdapterLuid.HighPart != adapterHighPart)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.AdapterMismatch,
                    unchecked((int)0x887A0002),
                    "DXGI adapter identity validation");
            }

            D3D_FEATURE_LEVEL* levels = stackalloc D3D_FEATURE_LEVEL[]
            {
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
            };
            D3D_FEATURE_LEVEL selectedLevel;
            result = D3D11CreateDevice(
                adapter,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                0,
                (uint)(
                    D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                    D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT),
                levels,
                4,
                D3D11_SDK_VERSION,
                &device,
                &selectedLevel,
                &context);
            CheckHResult(
                result,
                D3D11AdapterFailure.ProducerDeviceCreationFailed,
                "D3D11CreateDevice(selected Avalonia adapter)");

            Guid multithreadId = IID_ID3D11Multithread;
            result = context->QueryInterface(
                &multithreadId,
                (void**)&multithread);
            CheckHResult(
                result,
                D3D11AdapterFailure.MultithreadProtectionUnavailable,
                "QueryInterface(ID3D11Multithread)");
            multithread->SetMultithreadProtected(1);
            if (multithread->GetMultithreadProtected() == 0)
            {
                throw new D3D11AdapterException(
                    D3D11AdapterFailure.MultithreadProtectionUnavailable,
                    unchecked((int)0x80004005),
                    "ID3D11Multithread.SetMultithreadProtected");
            }

            _device = device;
            _context = context;
            device = null;
            context = null;
        }
        finally
        {
            if (multithread is not null)
                multithread->Release();
            if (context is not null)
                context->Release();
            if (device is not null)
                device->Release();
            if (adapter is not null)
                adapter->Release();
            if (factory is not null)
                factory->Release();
        }
    }

    private void ReleaseTexture()
    {
        _sharedHandle = 0;
        if (_keyedMutex is not null)
        {
            _keyedMutex->Release();
            _keyedMutex = null;
        }
        if (_texture is not null)
        {
            _texture->Release();
            _texture = null;
        }
        _width = 0;
        _height = 0;
    }

    public void Dispose()
    {
        ReleaseTexture();
        if (_context is not null)
        {
            _context->Release();
            _context = null;
        }
        if (_device is not null)
        {
            _device->Release();
            _device = null;
        }
    }

    private static void CheckHResult(
        int result,
        D3D11AdapterFailure failure,
        string operation)
    {
        if (result < 0)
            throw new D3D11AdapterException(failure, result, operation);
    }
}

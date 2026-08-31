using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace Conic.Vout;

/// <summary>
/// Owns the producer side of a BGRA shared texture. VLC writes key 0 and
/// publishes key 1; Avalonia consumes key 1 and returns key 0.
/// </summary>
internal sealed unsafe class D3D11SharedTexture : IDisposable
{
    private const int WaitTimeout = unchecked((int)0x887A0027);

    private ID3D11Device* _device;
    private ID3D11DeviceContext* _context;
    private ID3D11Texture2D* _texture;
    private IDXGIKeyedMutex* _keyedMutex;
    private nint _sharedHandle;
    private uint _width;
    private uint _height;
    private ulong _generation;

    public nint SharedHandle => _sharedHandle;

    public ulong Generation => _generation;

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
        CheckHResult(acquireResult, "IDXGIKeyedMutex.AcquireSync(producer)");

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
                "IDXGIKeyedMutex.ReleaseSync(producer)");
        }

        return true;
    }

    private void EnsureTexture(uint width, uint height)
    {
        if (_texture is not null && _width == width && _height == height)
            return;

        ReleaseTexture();
        EnsureDevice();

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
                "QueryInterface(IDXGIKeyedMutex)");

            Guid resourceId = IID_IDXGIResource;
            CheckHResult(
                texture->QueryInterface(
                    &resourceId,
                    (void**)&resource),
                "QueryInterface(IDXGIResource)");

            nint sharedHandle = 0;
            CheckHResult(
                resource->GetSharedHandle(&sharedHandle),
                "IDXGIResource.GetSharedHandle");
            if (sharedHandle == 0)
                throw new InvalidOperationException(
                    "D3D11 returned an empty shared texture handle.");

            _texture = texture;
            _keyedMutex = keyedMutex;
            _sharedHandle = sharedHandle;
            _width = width;
            _height = height;
            _generation++;
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

    private void EnsureDevice()
    {
        if (_device is not null)
            return;

        D3D_FEATURE_LEVEL* levels = stackalloc D3D_FEATURE_LEVEL[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
        };
        D3D_FEATURE_LEVEL selectedLevel;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        int result = D3D11CreateDevice(
            null,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            0,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            levels,
            4,
            D3D11_SDK_VERSION,
            &device,
            &selectedLevel,
            &context);
        CheckHResult(result, "D3D11CreateDevice(shared texture producer)");
        _device = device;
        _context = context;
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

    private static void CheckHResult(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{result:X8}.");
    }
}

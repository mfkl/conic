using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace VLCAvalonia.Interop;

internal static unsafe class D3D11SharedTextureProbe
{
    public static string OpenAndSynchronize(
        CompositionPluginBridge.GPUFrame frame)
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* texture = null;
        IDXGIKeyedMutex* keyedMutex = null;
        try
        {
            D3D_FEATURE_LEVEL* levels = stackalloc D3D_FEATURE_LEVEL[]
            {
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
            };
            D3D_FEATURE_LEVEL selectedLevel;
            CheckHResult(
                D3D11CreateDevice(
                    null,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                    0,
                    (uint)D3D11_CREATE_DEVICE_FLAG
                        .D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    levels,
                    4,
                    D3D11_SDK_VERSION,
                    &device,
                    &selectedLevel,
                    &context),
                "D3D11CreateDevice(smoke consumer)");

            Guid textureId = IID_ID3D11Texture2D;
            CheckHResult(
                device->OpenSharedResource(
                    frame.SharedHandle,
                    &textureId,
                    (void**)&texture),
                "ID3D11Device.OpenSharedResource");

            Guid mutexId = IID_IDXGIKeyedMutex;
            CheckHResult(
                texture->QueryInterface(
                    &mutexId,
                    (void**)&keyedMutex),
                "QueryInterface(IDXGIKeyedMutex)");
            CheckHResult(
                keyedMutex->AcquireSync(1, 5000),
                "IDXGIKeyedMutex.AcquireSync(smoke consumer)");

            D3D11_TEXTURE2D_DESC description;
            try
            {
                texture->GetDesc(&description);
            }
            finally
            {
                CheckHResult(
                    keyedMutex->ReleaseSync(0),
                    "IDXGIKeyedMutex.ReleaseSync(smoke consumer)");
            }

            if (description.Width != frame.Width ||
                description.Height != frame.Height ||
                description.Format !=
                    DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM)
            {
                throw new InvalidOperationException(
                    $"Unexpected shared texture: {description.Width}x" +
                    $"{description.Height} {description.Format}.");
            }

            return $"{description.Width}x{description.Height} " +
                $"{description.Format} generation={frame.Generation}";
        }
        finally
        {
            if (keyedMutex is not null)
                keyedMutex->Release();
            if (texture is not null)
                texture->Release();
            if (context is not null)
                context->Release();
            if (device is not null)
                device->Release();
        }
    }

    private static void CheckHResult(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{result:X8}.");
    }
}

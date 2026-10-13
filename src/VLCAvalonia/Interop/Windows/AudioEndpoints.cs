using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Player.Application;
using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace VLCAvalonia.Interop.Windows;

/// <summary>
/// Settings › Audio › Output device: the active playback endpoints, as
/// Windows' Core Audio lists them. VLC's MMDevice output names a device by
/// the same endpoint ID (--mmdevice-audio-device, aout_DeviceSet), so the
/// list needs no running VLC core.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class AudioEndpoints
{
    // PKEY_Device_FriendlyName (functiondiscoverykeys_devpkey.h).
    private static readonly PROPERTYKEY FriendlyName = new()
    {
        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        pid = 14,
    };

    /// <summary>The system default, then each active endpoint (ID, friendly name). Only the default when Core Audio fails.</summary>
    public static IReadOnlyList<SettingOption> List()
    {
        List<SettingOption> devices = [new(string.Empty, "System default")];
        IMMDeviceEnumerator* enumerator = null;
        IMMDeviceCollection* collection = null;
        try
        {
            Guid clsid = CLSID_MMDeviceEnumerator, iid = IID_IMMDeviceEnumerator;
            if (FAILED(CoCreateInstance(&clsid, null, (uint)CLSCTX_ALL, &iid, (void**)&enumerator))) return devices;
            if (FAILED(enumerator->EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, &collection))) return devices;
            uint count;
            if (FAILED(collection->GetCount(&count))) return devices;
            for (uint index = 0; index < count; index++)
            {
                IMMDevice* device = null;
                if (FAILED(collection->Item(index, &device))) continue;
                try
                {
                    ushort* id = null;
                    if (FAILED(device->GetId(&id))) continue;
                    string value = new((char*)id);
                    CoTaskMemFree(id);
                    devices.Add(new(value, Name(device) ?? value));
                }
                finally { device->Release(); }
            }
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException) { }
        finally
        {
            if (collection is not null) collection->Release();
            if (enumerator is not null) enumerator->Release();
        }
        return devices;
    }

    private static string? Name(IMMDevice* device)
    {
        IPropertyStore* store = null;
        if (FAILED(device->OpenPropertyStore(0 /* STGM_READ */, &store))) return null;
        PROPVARIANT value = default;
        try
        {
            PROPERTYKEY key = FriendlyName;
            if (FAILED(store->GetValue(&key, &value)) || value.vt != (ushort)VARENUM.VT_LPWSTR) return null;
            return new string((char*)value.pwszVal);
        }
        finally
        {
            PropVariantClear(&value);
            store->Release();
        }
    }
}

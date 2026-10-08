using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>
/// An <c>MTLSharedEvent</c>: the timeline semaphore Avalonia's compositor waits
/// on and signals around reading an imported IOSurface.
/// </summary>
/// <remarks>
/// The video output only lends pixel buffers VideoToolbox or VLC's converter
/// have finished writing, so the producer side signals from the CPU before
/// each update. The compositor signals the next value once its copy of the
/// surface is done, which is when the buffer can go back to VLC.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MetalSharedEvent : IDisposable
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string MetalLibrary = "/System/Library/Frameworks/Metal.framework/Metal";

    private static readonly nint MessageSend = NativeLibrary.GetExport(NativeLibrary.Load(ObjectiveCLibrary), "objc_msgSend");
    private static readonly nint SignaledValueSelector = sel_registerName("signaledValue");
    private static readonly nint SetSignaledValueSelector = sel_registerName("setSignaledValue:");
    private static readonly nint ReleaseSelector = sel_registerName("release");

    private nint _event;

    private MetalSharedEvent(nint sharedEvent, ulong deviceRegistryId, bool matchesCompositor)
    {
        _event = sharedEvent;
        DeviceRegistryId = deviceRegistryId;
        MatchesCompositor = matchesCompositor;
    }

    /// <summary>The retained <c>id&lt;MTLSharedEvent&gt;</c>.</summary>
    public nint Handle => _event != 0 ? _event : throw new ObjectDisposedException(nameof(MetalSharedEvent));

    /// <summary>The <c>registryID</c> of the device that created the event.</summary>
    public ulong DeviceRegistryId { get; }

    /// <summary>Whether that device is the one the compositor reported.</summary>
    public bool MatchesCompositor { get; }

    public ulong SignaledValue
    {
        get => ((delegate* unmanaged<nint, nint, ulong>)MessageSend)(Handle, SignaledValueSelector);
        set => ((delegate* unmanaged<nint, nint, ulong, void>)MessageSend)(Handle, SetSignaledValueSelector, value);
    }

    /// <summary>
    /// Creates the event on the Metal device Avalonia's compositor uses,
    /// identified by its 8-byte device LUID (a <c>registryID</c>), or on the
    /// system default device when no device matches.
    /// </summary>
    public static MetalSharedEvent Create(byte[]? compositorLuid)
    {
        nint device = 0;
        // The byte order Avalonia uses for the registry ID is not documented.
        foreach (ulong registryId in RegistryIdCandidates(compositorLuid))
            if ((device = FindDevice(registryId)) != 0)
                break;
        bool ownsDevice = device == 0;
        if (device == 0)
            device = MTLCreateSystemDefaultDevice();
        if (device == 0)
            throw new PlatformNotSupportedException("No Metal device is available.");
        try
        {
            ulong deviceId = ((delegate* unmanaged<nint, nint, ulong>)MessageSend)(device, sel_registerName("registryID"));
            nint sharedEvent = ((delegate* unmanaged<nint, nint, nint>)MessageSend)(device, sel_registerName("newSharedEvent"));
            if (sharedEvent == 0)
                throw new InvalidOperationException("The Metal device could not create a shared event.");
            return new MetalSharedEvent(sharedEvent, deviceId, !ownsDevice);
        }
        finally
        {
            if (ownsDevice)
                Release(device);
        }
    }

    private static ulong[] RegistryIdCandidates(byte[]? luid) => luid is { Length: 8 }
        ? [BinaryPrimitives.ReadUInt64BigEndian(luid), BinaryPrimitives.ReadUInt64LittleEndian(luid)]
        : [];

    // The returned device is borrowed from the array, which is released here;
    // devices live for the process, so the pointer stays valid.
    private static nint FindDevice(ulong registryId)
    {
        nint devices = MTLCopyAllDevices();
        if (devices == 0)
            return 0;
        try
        {
            var count = (delegate* unmanaged<nint, nint, nuint>)MessageSend;
            var objectAtIndex = (delegate* unmanaged<nint, nint, nuint, nint>)MessageSend;
            var getRegistryId = (delegate* unmanaged<nint, nint, ulong>)MessageSend;
            nint countSelector = sel_registerName("count");
            nint objectSelector = sel_registerName("objectAtIndex:");
            nint registrySelector = sel_registerName("registryID");
            for (nuint index = 0, total = count(devices, countSelector); index < total; index++)
            {
                nint device = objectAtIndex(devices, objectSelector, index);
                if (getRegistryId(device, registrySelector) == registryId)
                    return device;
            }
            return 0;
        }
        finally
        {
            Release(devices);
        }
    }

    private static void Release(nint value) =>
        ((delegate* unmanaged<nint, nint, void>)MessageSend)(value, ReleaseSelector);

    public void Dispose()
    {
        nint value = Interlocked.Exchange(ref _event, 0);
        if (value != 0)
            Release(value);
    }

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(MetalLibrary)]
    private static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(MetalLibrary)]
    private static partial nint MTLCopyAllDevices();
}

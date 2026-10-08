using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>
/// Sets the Dock icon. A bundled app gets it from <c>AppIcon.icns</c>; run
/// from source (<c>dotnet</c>), the Dock would otherwise show a generic
/// executable icon, and Avalonia's window icon does not reach the Dock.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacApplicationIcon
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly nint MessageSend = NativeLibrary.GetExport(NativeLibrary.Load(ObjectiveCLibrary), "objc_msgSend");

    /// <summary>Uses the PNG or ICNS <paramref name="image"/> as the Dock icon.</summary>
    public static void Set(byte[] image)
    {
        fixed (byte* bytes = image)
        {
            nint data = ((delegate* unmanaged<nint, nint, byte*, nuint, nint>)MessageSend)(
                objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), bytes, (nuint)image.Length);
            nint allocated = ((delegate* unmanaged<nint, nint, nint>)MessageSend)(objc_getClass("NSImage"), sel_registerName("alloc"));
            nint nsImage = ((delegate* unmanaged<nint, nint, nint, nint>)MessageSend)(allocated, sel_registerName("initWithData:"), data);
            if (nsImage == 0) return;
            nint application = ((delegate* unmanaged<nint, nint, nint>)MessageSend)(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            ((delegate* unmanaged<nint, nint, nint, void>)MessageSend)(application, sel_registerName("setApplicationIconImage:"), nsImage);
            ((delegate* unmanaged<nint, nint, void>)MessageSend)(nsImage, sel_registerName("release"));
        }
    }

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);
}

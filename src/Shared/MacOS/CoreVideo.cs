using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCAvalonia.Interop.MacOS;

/// <summary>
/// CoreFoundation, CoreVideo and IOSurface calls for sharing VLC's
/// <c>CVPixelBuffer</c> pictures. Shared by the video output and the app.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class CoreVideo
{
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreVideoLibrary = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string IOSurfaceLibrary = "/System/Library/Frameworks/IOSurface.framework/IOSurface";

    /// <summary><c>kCVPixelFormatType_32BGRA</c> ('BGRA').</summary>
    public const uint PixelFormat32BGRA = 0x42475241;
    /// <summary><c>kCVPixelBufferLock_ReadOnly</c>.</summary>
    public const ulong ReadOnlyLock = 1;
    /// <summary><c>kCVReturnSuccess</c>.</summary>
    public const int Success = 0;

    [LibraryImport(CoreFoundationLibrary)]
    public static partial nint CFRetain(nint value);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundationLibrary)]
    public static partial nint CFGetRetainCount(nint value);

    /// <summary>The IOSurface backing a pixel buffer, or zero. Not retained.</summary>
    [LibraryImport(CoreVideoLibrary)]
    public static partial nint CVPixelBufferGetIOSurface(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial uint CVPixelBufferGetPixelFormatType(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetWidth(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetHeight(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nuint CVPixelBufferGetBytesPerRow(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial nint CVPixelBufferGetBaseAddress(nint pixelBuffer);

    [LibraryImport(CoreVideoLibrary)]
    public static partial int CVPixelBufferLockBaseAddress(nint pixelBuffer, ulong flags);

    [LibraryImport(CoreVideoLibrary)]
    public static partial int CVPixelBufferUnlockBaseAddress(nint pixelBuffer, ulong flags);

    [LibraryImport(IOSurfaceLibrary)]
    public static partial uint IOSurfaceGetID(nint surface);

    [LibraryImport(IOSurfaceLibrary)]
    public static partial nuint IOSurfaceGetWidth(nint surface);

    [LibraryImport(IOSurfaceLibrary)]
    public static partial nuint IOSurfaceGetHeight(nint surface);

    [LibraryImport(IOSurfaceLibrary)]
    public static partial uint IOSurfaceGetPixelFormat(nint surface);
}

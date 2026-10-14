#if SMOKE
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.MacOS;

/// <summary>
/// Posts left clicks to the app's own event queue as <c>NSEvent</c>s, so they
/// go through AppKit's window and title bar routing like a real click. Unlike
/// <c>CGEventPost</c>, this needs no Accessibility permission.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacEventInjection
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly nint Library = NativeLibrary.Load(ObjectiveCLibrary);
    private static readonly nint MessageSend = NativeLibrary.GetExport(Library, "objc_msgSend");
    // A 32-byte CGRect comes back through a hidden pointer on x64, which
    // objc_msgSend_stret expects; arm64 returns it in registers.
    private static readonly nint MessageSendRect = RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? NativeLibrary.GetExport(Library, "objc_msgSend_stret")
        : MessageSend;

    // NSEventType values.
    private const nuint LeftMouseDown = 1;
    private const nuint LeftMouseUp = 2;
    private const nuint MouseMoved = 5;

    /// <summary>
    /// Clicks <paramref name="x"/>, <paramref name="y"/>, in points from the
    /// top left of the window's frame.
    /// </summary>
    /// <param name="handle">An <c>NSWindow</c> or one of its views.</param>
    public static void Click(nint handle, bool isView, double x, double y) =>
        Post(handle, isView, x, y, [LeftMouseDown, LeftMouseUp]);

    /// <summary>Presses the left button (see <see cref="Click"/>).</summary>
    public static void Press(nint handle, bool isView, double x, double y) =>
        Post(handle, isView, x, y, [LeftMouseDown]);

    /// <summary>Releases the left button (see <see cref="Click"/>).</summary>
    public static void Release(nint handle, bool isView, double x, double y) =>
        Post(handle, isView, x, y, [LeftMouseUp]);

    /// <summary>Moves the pointer to <paramref name="x"/>, <paramref name="y"/> (see <see cref="Click"/>).</summary>
    public static void Move(nint handle, bool isView, double x, double y) =>
        Post(handle, isView, x, y, [MouseMoved]);

    /// <summary>
    /// Moves the real pointer to <paramref name="x"/>, <paramref name="y"/>, in
    /// points from the top left of the main display. It posts no event.
    /// </summary>
    public static void Warp(double x, double y) =>
        CGWarpMouseCursorPosition(new CGPoint { X = x, Y = y });

    private static void Post(nint handle, bool isView, double x, double y, nuint[] types)
    {
        nint window = isView ? Send(handle, Selector("window")) : handle;
        if (window == 0) throw new InvalidOperationException("The view has no window.");
        nint windowNumber = Send(window, Selector("windowNumber"));
        double height = ((delegate* unmanaged<nint, nint, CGRect>)MessageSendRect)(window, Selector("frame")).Height;
        // Window coordinates start at the bottom left.
        CGPoint location = new() { X = x, Y = height - y };
        nint application = Send(objc_getClass("NSApplication"), Selector("sharedApplication"));
        double now = ((delegate* unmanaged<nint, nint, double>)MessageSend)(Send(objc_getClass("NSProcessInfo"), Selector("processInfo")), Selector("systemUptime"));
        foreach (nuint type in types)
        {
            nint mouseEvent = ((delegate* unmanaged<nint, nint, nuint, CGPoint, nuint, double, nint, nint, nint, nint, float, nint>)MessageSend)(
                objc_getClass("NSEvent"),
                Selector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
                type, location, 0, now, windowNumber, 0, 0, type == MouseMoved ? 0 : 1, type == LeftMouseDown ? 1f : 0f);
            ((delegate* unmanaged<nint, nint, nint, byte, void>)MessageSend)(application, Selector("postEvent:atStart:"), mouseEvent, 0);
        }
    }

    /// <summary>
    /// Presses and releases <paramref name="characters"/> (virtual key
    /// <paramref name="keyCode"/>) with Command held, so menu key
    /// equivalents see it as AppKit would.
    /// </summary>
    public static void CommandKey(nint handle, bool isView, string characters, ushort keyCode)
    {
        const nuint KeyDown = 10, KeyUp = 11, CommandFlag = 1 << 20;
        nint window = isView ? Send(handle, Selector("window")) : handle;
        nint windowNumber = Send(window, Selector("windowNumber"));
        nint text = ((delegate* unmanaged<nint, nint, byte*, nint>)MessageSend)(objc_getClass("NSString"), Selector("stringWithUTF8String:"), Utf8(characters));
        nint application = Send(objc_getClass("NSApplication"), Selector("sharedApplication"));
        double now = ((delegate* unmanaged<nint, nint, double>)MessageSend)(Send(objc_getClass("NSProcessInfo"), Selector("processInfo")), Selector("systemUptime"));
        foreach (nuint type in new[] { KeyDown, KeyUp })
        {
            nint keyEvent = ((delegate* unmanaged<nint, nint, nuint, CGPoint, nuint, double, nint, nint, nint, nint, byte, ushort, nint>)MessageSend)(
                objc_getClass("NSEvent"),
                Selector("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
                type, default, CommandFlag, now, windowNumber, 0, text, text, 0, keyCode);
            ((delegate* unmanaged<nint, nint, nint, byte, void>)MessageSend)(application, Selector("postEvent:atStart:"), keyEvent, 0);
        }
    }

    private static byte* Utf8(string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        nint memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);
        return (byte*)memory;
    }

    private static nint Send(nint receiver, nint selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MessageSend)(receiver, selector);

    private static nint Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjectiveCLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial int CGWarpMouseCursorPosition(CGPoint point);
}
#endif // SMOKE

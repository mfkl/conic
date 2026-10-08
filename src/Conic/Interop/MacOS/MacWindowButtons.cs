using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Conic.Interop.MacOS;

/// <summary>
/// Moves an <c>NSWindow</c>'s traffic lights (close, minimize, zoom) so they
/// are centered on the app's own caption row instead of AppKit's 28 pt title
/// bar. Avalonia 12 only uses its title bar height hint for its own views, so
/// the window's title bar container is resized here, as Electron's and
/// Tauri's <c>trafficLightPosition</c> do, but narrowed to the buttons.
/// </summary>
/// <remarks>
/// AppKit lays the buttons out again on resize, full screen and activation
/// changes, so callers apply the position again after each of them.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacWindowButtons
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly nint Library = NativeLibrary.Load(ObjectiveCLibrary);
    private static readonly nint MessageSend = NativeLibrary.GetExport(Library, "objc_msgSend");
    // A 32-byte CGRect comes back through a hidden pointer on x64, which
    // objc_msgSend_stret expects; arm64 returns it in registers.
    private static readonly nint MessageSendRect = RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? NativeLibrary.GetExport(Library, "objc_msgSend_stret")
        : MessageSend;

    private static readonly nint WindowSelector = sel_registerName("window");
    private static readonly nint StandardWindowButtonSelector = sel_registerName("standardWindowButton:");
    private static readonly nint SuperviewSelector = sel_registerName("superview");
    private static readonly nint FrameSelector = sel_registerName("frame");
    private static readonly nint SetFrameSelector = sel_registerName("setFrame:");
    private static readonly nint SetFrameOriginSelector = sel_registerName("setFrameOrigin:");
    private static readonly nint SetHiddenSelector = sel_registerName("setHidden:");

    // NSWindowButton values.
    private const nint CloseButton = 0;
    private const nint MinimizeButton = 1;
    private const nint ZoomButton = 2;

    /// <summary>
    /// Centers the buttons <paramref name="centerY"/> points below the top of
    /// the window, the first one <paramref name="left"/> points from its left edge.
    /// </summary>
    /// <param name="handle">An <c>NSWindow</c> or one of its views.</param>
    /// <param name="isView">Whether <paramref name="handle"/> is an <c>NSView</c>.</param>
    /// <returns>False when the window has no standard buttons (borderless).</returns>
    public static bool Apply(nint handle, bool isView, double left, double centerY)
    {
        nint window = isView ? Send(handle, WindowSelector) : handle;
        if (window == 0) return false;
        nint close = SendButton(window, CloseButton);
        nint minimize = SendButton(window, MinimizeButton);
        nint zoom = SendButton(window, ZoomButton);
        if (close == 0 || minimize == 0 || zoom == 0) return false;
        nint titleBar = Send(close, SuperviewSelector);
        nint container = titleBar == 0 ? 0 : Send(titleBar, SuperviewSelector);
        if (container == 0) return false;

        CGRect closeFrame = Frame(close);
        CGRect minimizeFrame = Frame(minimize);
        double spacing = minimizeFrame.X - closeFrame.X;
        double height = 2 * centerY;

        // The container sits in the window's unflipped frame view, so its
        // origin is measured from the bottom of the window. It only spans the
        // buttons: the window server treats the whole container as a title
        // bar it can drag the window from, so a full-width container would
        // swallow real clicks on the app's caption row.
        CGRect windowFrame = Frame(window);
        CGRect containerFrame = Frame(container);
        containerFrame.X = 0;
        containerFrame.Width = left + 2 * spacing + closeFrame.Width + left / 2;
        containerFrame.Height = height;
        containerFrame.Y = windowFrame.Height - height;
        ((delegate* unmanaged<nint, nint, CGRect, void>)MessageSend)(container, SetFrameSelector, containerFrame);
        HideDecoration(container);

        nint[] buttons = [close, minimize, zoom];
        for (int index = 0; index < buttons.Length; index++)
        {
            CGRect frame = Frame(buttons[index]);
            CGPoint origin = new(left + index * spacing, (height - frame.Height) / 2);
            ((delegate* unmanaged<nint, nint, CGPoint, void>)MessageSend)(buttons[index], SetFrameOriginSelector, origin);
        }
        return true;
    }

    /// <summary>Shows or hides the buttons, for example with the player's auto-hiding chrome.</summary>
    /// <param name="handle">An <c>NSWindow</c> or one of its views.</param>
    /// <param name="isView">Whether <paramref name="handle"/> is an <c>NSView</c>.</param>
    public static void SetVisible(nint handle, bool isView, bool visible)
    {
        nint window = isView ? Send(handle, WindowSelector) : handle;
        if (window == 0) return;
        foreach (nint kind in new[] { CloseButton, MinimizeButton, ZoomButton })
            if (SendButton(window, kind) is not 0 and nint button)
                ((delegate* unmanaged<nint, nint, byte, void>)MessageSend)(button, SetHiddenSelector, visible ? (byte)0 : (byte)1);
    }

    /// <summary>Whether the close button is shown (see <see cref="SetVisible"/>).</summary>
    public static bool IsVisible(nint handle, bool isView)
    {
        nint window = isView ? Send(handle, WindowSelector) : handle;
        nint close = window == 0 ? 0 : SendButton(window, CloseButton);
        return close != 0 && ((delegate* unmanaged<nint, nint, byte>)MessageSend)(close, sel_registerName("isHidden")) == 0;
    }

    /// <summary>
    /// Hides the container's <c>_NSTitlebarDecorationView</c>, which draws a
    /// rounded highlight along the top edge of the title bar. With the
    /// container narrowed to the buttons, that highlight would end in a
    /// visible notch; the app draws its own caption row instead.
    /// </summary>
    private static void HideDecoration(nint container)
    {
        nint subviews = Send(container, sel_registerName("subviews"));
        nuint count = ((delegate* unmanaged<nint, nint, nuint>)MessageSend)(subviews, sel_registerName("count"));
        for (nuint index = 0; index < count; index++)
        {
            nint view = ((delegate* unmanaged<nint, nint, nuint, nint>)MessageSend)(subviews, sel_registerName("objectAtIndex:"), index);
            // Key-value observing subclasses the view, so match on the name.
            if (Marshal.PtrToStringUTF8(class_getName(object_getClass(view)))?.Contains("TitlebarDecorationView", StringComparison.Ordinal) == true)
                ((delegate* unmanaged<nint, nint, byte, void>)MessageSend)(view, SetHiddenSelector, 1);
        }
    }

    [LibraryImport(ObjectiveCLibrary)]
    private static partial nint object_getClass(nint value);

    [LibraryImport(ObjectiveCLibrary)]
    private static partial nint class_getName(nint value);

    private static nint Send(nint receiver, nint selector) =>
        ((delegate* unmanaged<nint, nint, nint>)MessageSend)(receiver, selector);

    private static nint SendButton(nint window, nint button) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MessageSend)(window, StandardWindowButtonSelector, button);

    private static CGRect Frame(nint view) =>
        ((delegate* unmanaged<nint, nint, CGRect>)MessageSendRect)(view, FrameSelector);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint(double x, double y)
    {
        public double X = x;
        public double Y = y;
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
}

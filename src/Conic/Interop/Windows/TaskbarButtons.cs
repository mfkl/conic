using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Conic.Controls;
using Conic.Services;

namespace Conic.Interop.Windows;

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class TaskbarButtons : IMediaSessionBackend
{
    private static readonly Guid TaskbarListId = new("56fdf344-fd6d-11d0-958a-006097c9a090");
    private static readonly Guid TaskbarList3Id = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    private const uint Command = 0x0111, ThumbButtonClicked = 0x1800;
    private const uint IconMask = 0x2, TooltipMask = 0x4, FlagsMask = 0x8;
    private const uint Enabled = 0x0, Disabled = 0x1, Hidden = 0x8;
    private const int NoProgress = 0x0, Indeterminate = 0x1, Normal = 0x2, Paused = 0x8;
    private const uint PreviousButton = 1, PlayPauseButton = 2, NextButton = 3;
    private const int SmallIconWidth = 49;

    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly MediaSession _session;
    private readonly uint _buttonCreated;
    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private nint _playIcon, _pauseIcon, _previousIcon, _nextIcon;
    private void* _taskbar;
    private bool _buttonsAdded;
    private MediaSessionState _state = MediaSessionState.Closed;
    private (bool, bool, bool, bool)? _shownButtons;
    private (int, ulong, ulong)? _shownProgress;

    private TaskbarButtons(Window window, nint hwnd, MediaSession session)
    {
        _window = window;
        _hwnd = hwnd;
        _session = session;
        _buttonCreated = RegisterWindowMessageW("TaskbarButtonCreated");
        _hook = OnMessage;
        Win32Properties.AddWndProcHookCallback(window, _hook);
    }

    public static TaskbarButtons? Create(Window window, MediaSession session) =>
        window.TryGetPlatformHandle() is { Handle: not 0 } handle ? new TaskbarButtons(window, handle.Handle, session) : null;

    public void Update(MediaSessionState state)
    {
        _state = state;
        if (_taskbar == null) return;
        ShowProgress(state);
        ShowButtons(state);
    }

    public void Dispose()
    {
        Win32Properties.RemoveWndProcHookCallback(_window, _hook);
        if (_taskbar != null) ((delegate* unmanaged<void*, uint>)Vtbl(_taskbar)[2])(_taskbar);
        _taskbar = null;
        foreach (nint icon in new[] { _playIcon, _pauseIcon, _previousIcon, _nextIcon })
            if (icon != 0) DestroyIcon(icon);
        _playIcon = _pauseIcon = _previousIcon = _nextIcon = 0;
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == _buttonCreated && _buttonCreated != 0)
        {
            Connect();
            return 0;
        }
        if (message == Command && ((ulong)wParam >> 16 & 0xffff) == ThumbButtonClicked)
        {
            MediaSessionButton? pressed = ((ulong)wParam & 0xffff) switch
            {
                PreviousButton => MediaSessionButton.Previous,
                PlayPauseButton => MediaSessionButton.TogglePause,
                NextButton => MediaSessionButton.Next,
                _ => null,
            };
            if (pressed is { } button)
            {
                _session.Press(button);
                handled = true;
            }
        }
        return 0;
    }

    private void Connect()
    {
        if (_taskbar == null)
        {
            Guid clsid = TaskbarListId, iid = TaskbarList3Id;
            void* taskbar = null;
            if (CoCreateInstance(&clsid, null, 1, &iid, &taskbar) < 0 || taskbar == null) return;
            if (((delegate* unmanaged<void*, int>)Vtbl(taskbar)[3])(taskbar) < 0)
            {
                ((delegate* unmanaged<void*, uint>)Vtbl(taskbar)[2])(taskbar);
                return;
            }
            _taskbar = taskbar;
        }
        _buttonsAdded = false;
        _shownButtons = null;
        _shownProgress = null;
        Update(_state);
    }

    private void ShowProgress(MediaSessionState state)
    {
        ulong total = (ulong)Math.Max(0, state.Duration.TotalMilliseconds);
        ulong done = (ulong)Math.Clamp(state.Position.TotalMilliseconds, 0, total);
        int flags = state.Status switch
        {
            MediaSessionStatus.Opening => Indeterminate,
            MediaSessionStatus.Playing when total > 0 => Normal,
            MediaSessionStatus.Paused when total > 0 => Paused,
            _ => NoProgress,
        };
        if (flags is NoProgress or Indeterminate) done = total = 0;
        if (_shownProgress == (flags, done, total)) return;
        _shownProgress = (flags, done, total);
        ((delegate* unmanaged<void*, nint, int, int>)Vtbl(_taskbar)[10])(_taskbar, _hwnd, flags);
        if (total > 0) ((delegate* unmanaged<void*, nint, ulong, ulong, int>)Vtbl(_taskbar)[9])(_taskbar, _hwnd, done, total);
    }

    private void ShowButtons(MediaSessionState state)
    {
        bool playing = state.Status == MediaSessionStatus.Playing;
        var shown = (state.HasMedia, playing, state.CanPrevious, state.CanNext);
        if (_buttonsAdded && _shownButtons == shown) return;
        EnsureIcons();
        ThumbButton* buttons = stackalloc ThumbButton[3];
        Fill(&buttons[0], PreviousButton, _previousIcon, "Previous", !state.HasMedia ? Hidden : state.CanPrevious ? Enabled : Disabled);
        Fill(&buttons[1], PlayPauseButton, playing ? _pauseIcon : _playIcon, playing ? "Pause" : "Play", state.HasMedia ? Enabled : Hidden);
        Fill(&buttons[2], NextButton, _nextIcon, "Next", !state.HasMedia ? Hidden : state.CanNext ? Enabled : Disabled);
        int slot = _buttonsAdded ? 16 : 15;
        if (((delegate* unmanaged<void*, nint, uint, ThumbButton*, int>)Vtbl(_taskbar)[slot])(_taskbar, _hwnd, 3, buttons) < 0) return;
        _buttonsAdded = true;
        _shownButtons = shown;
    }

    private static void Fill(ThumbButton* button, uint id, nint icon, string tip, uint flags)
    {
        *button = default;
        button->Mask = IconMask | TooltipMask | FlagsMask;
        button->Id = id;
        button->Icon = icon;
        button->Flags = flags;
        int length = Math.Min(tip.Length, 259);
        for (int index = 0; index < length; index++) button->Tip[index] = tip[index];
    }

    private void EnsureIcons()
    {
        if (_playIcon != 0) return;
        int size = GetSystemMetricsForDpi(SmallIconWidth, GetDpiForWindow(_hwnd));
        if (size <= 0) size = 16;
        _playIcon = CreateIcon("Icon.Play", size);
        _pauseIcon = CreateIcon("Icon.Pause", size);
        _previousIcon = CreateIcon("Icon.Previous", size);
        _nextIcon = CreateIcon("Icon.Next", size);
    }

    private static nint CreateIcon(string key, int size)
    {
        if (Application.Current?.TryGetResource(key, null, out object? resource) != true || resource is not IconData icon) return 0;
        using RenderTargetBitmap bitmap = new(new PixelSize(size, size), new Vector(96, 96));
        double scale = size * 0.6 / 12;
        using (DrawingContext context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateTranslation(-12.25, -12) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(size / 2.0, size / 2.0)))
        {
            foreach (IconLayer layer in icon.Layers)
            {
                if (layer.Data is null) continue;
                IPen? pen = layer.StrokeThickness > 0 ? new Pen(Brushes.White, layer.StrokeThickness, lineCap: layer.LineCap, lineJoin: layer.LineJoin) : null;
                context.DrawGeometry(layer.Fill ? Brushes.White : null, pen, layer.Data);
            }
        }
        int stride = size * 4;
        byte[] pixels = new byte[stride * size];
        fixed (byte* source = pixels) bitmap.CopyPixels(new PixelRect(0, 0, size, size), (nint)source, pixels.Length, stride);
        BitmapInfoHeader header = new() { Size = (uint)sizeof(BitmapInfoHeader), Width = size, Height = -size, Planes = 1, BitCount = 32 };
        void* bits;
        nint color = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (color == 0) return 0;
        nint mask = CreateBitmap(size, size, 1, 1, null);
        try
        {
            byte* target = (byte*)bits;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                target[index] = target[index + 1] = target[index + 2] = 255;
                target[index + 3] = pixels[index + 3];
            }
            IconInfo info = new() { IsIcon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(&info);
        }
        finally
        {
            DeleteObject(color);
            if (mask != 0) DeleteObject(mask);
        }
    }

    private static void** Vtbl(void* instance) => *(void***)instance;

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbButton
    {
        public uint Mask;
        public uint Id;
        public uint Bitmap;
        public nint Icon;
        public fixed char Tip[260];
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** instance);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconIndirect(IconInfo* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Player.Application;
using VLCLR.Types;

namespace VLCAvalonia.Interop;

/// <summary>
/// VLC's log, for the Messages window: every core the app starts sends its
/// messages here (<c>vlc_LogSet</c>), from VLC's own threads. VLC passes all
/// of them, whatever the verbosity; <see cref="Messages"/> keeps the ones its
/// verbosity accepts. Setting a callback replaces VLC's console logger, so the
/// messages it printed (errors and information, everything with -vvv) still
/// go to stderr in its format, where the smoke scripts read them.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe class VlcLog
{
    private const int FormatBufferSize = 4096;
    private static int s_consoleVerbosity;

    public static EngineMessageLog Messages { get; } = new();

    /// <summary>Prints debug messages to stderr too, as VLC's -vvv did (smoke diagnostics).</summary>
    public static void EnableVerboseConsole() => Volatile.Write(ref s_consoleVerbosity, EngineMessageLog.MaximumVerbosity);

    /// <summary>Sends <paramref name="core"/>'s messages here; call after libvlc_InternalInit.</summary>
    public static void Attach(nint core) => VlcCoreApi.SetLogger(core, &OnMessage);

    /// <summary>A message of the app's own, listed with VLC's.</summary>
    public static void Write(EngineMessageType type, string text)
    {
        if (Messages.Accepts(type)) Messages.Add(type, "vlc-avalonia", "app", text);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMessage(nint data, int type, VLCLog* item, byte* format, nint arguments)
    {
        try
        {
            EngineMessageType kind = (EngineMessageType)Math.Clamp(type, 0, 3);
            bool keep = Messages.Accepts(kind);
            bool print = EngineMessageLog.IsShownAt(kind, Volatile.Read(ref s_consoleVerbosity));
            if (!keep && !print) return;

            string text = Format(format, arguments);
            string module = Utf8(item->ModulePtr);
            string objectType = Utf8(item->ObjectTypePtr);
            if (keep) Messages.Add(kind, module, objectType, text);
            if (print)
            {
                string header = item->HeaderPtr != 0 ? $"[{Utf8(item->HeaderPtr)}] " : "";
                string suffix = kind == EngineMessageType.Info ? "" : " " + EngineMessage.NameOf(kind);
                Console.Error.WriteLine($"[{item->ObjectId:x16}] {header}{module} {objectType}{suffix}: {text}");
            }
        }
        catch
        {
            // Never unwind into VLC.
        }
    }

    private static string Utf8(nint text) => text == 0 ? "" : Marshal.PtrToStringUTF8(text) ?? "";

    /// <summary>Formats a VLC message with the C library, which consumes <paramref name="arguments"/>.</summary>
    internal static string Format(byte* format, nint arguments)
    {
        byte* buffer = stackalloc byte[FormatBufferSize];
        int length = Formatter.Format(buffer, FormatBufferSize, format, arguments);
        if (length < 0) return Utf8((nint)format);
        bool truncated = length >= FormatBufferSize;
        string text = Encoding.UTF8.GetString(buffer, Math.Min(length, FormatBufferSize - 1));
        return truncated ? text + "…" : text;
    }

    private static class Formatter
    {
        // Windows: the Universal CRT's vsnprintf, which reads the C99 formats
        // (%zu, %lld) of VLC's MinGW build; msvcrt's _vsnprintf does not.
        private static readonly delegate* unmanaged[Cdecl]<ulong, byte*, nuint, byte*, nint, nint, int> s_windows =
            OperatingSystem.IsWindows()
                ? (delegate* unmanaged[Cdecl]<ulong, byte*, nuint, byte*, nint, nint, int>)NativeLibrary.GetExport(NativeLibrary.Load("ucrtbase.dll"), "__stdio_common_vsprintf")
                : null;
        private static readonly delegate* unmanaged[Cdecl]<byte*, nuint, byte*, nint, int> s_unix =
            OperatingSystem.IsWindows()
                ? null
                : (delegate* unmanaged[Cdecl]<byte*, nuint, byte*, nint, int>)NativeLibrary.GetExport(NativeLibrary.GetMainProgramHandle(), "vsnprintf");

        // _CRT_INTERNAL_PRINTF_STANDARD_SNPRINTF_BEHAVIOR: return the full length, as C99 does.
        private const ulong StandardSnprintfBehavior = 2;

        public static int Format(byte* buffer, int size, byte* format, nint arguments) => OperatingSystem.IsWindows()
            ? s_windows(StandardSnprintfBehavior, buffer, (nuint)size, format, 0, arguments)
            : s_unix(buffer, (nuint)size, format, arguments);
    }
}

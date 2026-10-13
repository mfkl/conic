using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace VLCAvalonia.Interop.Windows;

/// <summary>
/// Keeps the HTTP proxy password out of settings.json in clear text: it is
/// stored protected with DPAPI for the current Windows user (CryptProtectData,
/// no UI, an app-specific entropy), as base64. Only that user on that machine
/// can read it back; another account or a copied profile gets nothing and the
/// proxy is used without a password.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ProxyPasswordProtection
{
    private const int UiForbidden = 0x1;
    private static readonly byte[] Entropy = "VLC Avalonia HTTP proxy password"u8.ToArray();

    /// <summary>The protected form of <paramref name="password"/>; null for an empty password.</summary>
    public static string? Protect(string? password)
    {
        if (string.IsNullOrEmpty(password)) return null;
        byte[] clear = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(Transform(clear, protect: true)); }
        finally { Array.Clear(clear); }
    }

    /// <summary>The password, or null when there is none or it cannot be read back.</summary>
    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            byte[] clear = Transform(Convert.FromBase64String(protectedValue), protect: false);
            try { return Encoding.UTF8.GetString(clear); }
            finally { Array.Clear(clear); }
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException) { return null; }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        fixed (byte* inputBytes = input)
        fixed (byte* entropyBytes = Entropy)
        {
            DataBlob source = new() { Size = input.Length, Data = inputBytes };
            DataBlob entropy = new() { Size = Entropy.Length, Data = entropyBytes };
            DataBlob result = default;
            bool ok = protect
                ? CryptProtectData(&source, null, &entropy, 0, 0, UiForbidden, &result)
                : CryptUnprotectData(&source, 0, &entropy, 0, 0, UiForbidden, &result);
            if (!ok) throw new InvalidOperationException($"DPAPI failed ({Marshal.GetLastPInvokeError()}).");
            try { return new ReadOnlySpan<byte>(result.Data, result.Size).ToArray(); }
            finally
            {
                new Span<byte>(result.Data, result.Size).Clear();
                LocalFree((nint)result.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public byte* Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DataBlob* input, char* description, DataBlob* entropy, nint reserved, nint prompt, int flags, DataBlob* output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DataBlob* input, nint description, DataBlob* entropy, nint reserved, nint prompt, int flags, DataBlob* output);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}

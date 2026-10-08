using System.Diagnostics;

namespace VLCAvalonia.Services;

/// <summary>
/// Asks macOS Quick Look for a file's thumbnail through <c>qlmanage</c>, the
/// command-line front end every Mac ships, as the Windows reader asks the
/// shell. Quick Look covers the formats macOS itself understands (MP4, MOV,
/// M4V and others); formats without a Quick Look generator get none.
/// </summary>
internal static class QuickLookThumbnail
{
    private const string Tool = "/usr/bin/qlmanage";
    private const int Size = 512;
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<byte[]?> ReadAsync(string path)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(Tool) || !File.Exists(path)) return null;
        string output = Path.Combine(Path.GetTempPath(), "vlc-avalonia-quicklook-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(output);
            ProcessStartInfo start = new(Tool)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[] { "-t", "-s", Size.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", output, path })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("qlmanage did not start.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using (CancellationTokenSource timeout = new(Timeout))
            {
                try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    return null;
                }
            }
            await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
            // qlmanage names the image after the source file.
            FileInfo image = new(Path.Combine(output, Path.GetFileName(path) + ".png"));
            return image.Exists && image.Length is > 0 and <= MaximumBytes
                ? await File.ReadAllBytesAsync(image.FullName).ConfigureAwait(false)
                : null;
        }
        finally
        {
            try { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

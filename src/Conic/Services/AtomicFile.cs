using System.Threading;

namespace Conic.Services;

/// <summary>
/// Puts a finished temporary file in place of the real one. On Windows an
/// antivirus or the search indexer can hold the file for a moment just
/// after the last write, and File.Replace then fails ("Unable to remove
/// the file to be replaced"); the replacement is tried again for about a
/// second before the error stands.
/// </summary>
internal static class AtomicFile
{
    private static readonly int[] Delays = [25, 50, 100, 200, 300, 400];

    public static void Commit(string temporary, string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < Delays.Length)
            {
                Thread.Sleep(Delays[attempt]);
            }
        }
    }
}

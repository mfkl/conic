using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Conic.Services;

/// <summary>
/// Settings › Interface › Allow only one instance (VLC's one-instance): a
/// second start hands its media to the running Conic, which opens it (or
/// queues it, with Add to the queue) and comes to the front, then exits.
/// The running one listens on a pipe named for the user.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(string name) => _name = name;

    /// <summary>The pipe of this user's Conic.</summary>
    internal static string PipeName(string? user = null) => "conic-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes((user ?? Environment.UserName).ToLowerInvariant())))[..16];

    /// <summary>Hands <paramref name="sources"/> to the running Conic; false when none answers.</summary>
    internal static bool TrySend(IReadOnlyList<string> sources, string? pipeName = null, int timeoutMilliseconds = 300)
    {
        try
        {
            using NamedPipeClientStream client = new(".", pipeName ?? PipeName(), PipeDirection.Out);
            client.Connect(timeoutMilliseconds);
            byte[] message = JsonSerializer.SerializeToUtf8Bytes(sources.ToArray());
            client.Write(message);
            client.Flush();
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for later starts; <paramref name="received"/> runs on a pool thread with their media.</summary>
    internal static SingleInstance Listen(Action<IReadOnlyList<string>> received, string? pipeName = null)
    {
        SingleInstance listener = new(pipeName ?? PipeName());
        _ = Task.Run(() => listener.RunAsync(received));
        return listener;
    }

    private async Task RunAsync(Action<IReadOnlyList<string>> received)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream server = new(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token);
                using MemoryStream message = new();
                await server.CopyToAsync(message, _stop.Token);
                string[] sources = JsonSerializer.Deserialize<string[]>(message.ToArray()) ?? [];
                received(sources.Where(source => !string.IsNullOrWhiteSpace(source)).ToArray());
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // A broken message or a pipe another process holds: wait, then listen again.
                try { await Task.Delay(500, _stop.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}

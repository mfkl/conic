using System.Diagnostics;

namespace Conic.Interop;

internal static class CompositionSmoke
{
    public static int Run()
    {
        string mediaPath = RepositoryPaths.TestMediaPath;
        if (!File.Exists(mediaPath))
        {
            Console.Error.WriteLine($"Smoke media not found: {mediaPath}");
            return 2;
        }

        try
        {
            using CompositionPluginBridge bridge = new();
            bridge.EnableGPUFrames();
            using VLCCorePlayer player = new();
            player.StatusChanged += status =>
                Console.WriteLine($"[composition-smoke] {status}");
            player.Play(mediaPath);

            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (bridge.TryTakeLatestGPU(
                        out CompositionPluginBridge.GPUFrame? frame) &&
                    frame is not null)
                {
                    string texture =
                        D3D11SharedTextureProbe.OpenAndSynchronize(frame);
                    Console.WriteLine(
                        $"[composition-smoke] GPU shared texture={texture} " +
                        $"pts={frame.PresentationTimestamp}");
                    Thread.Sleep(500);
                    return 0;
                }

                Thread.Sleep(20);
            }

            Console.Error.WriteLine(
                "[composition-smoke] No frame arrived within 15 seconds.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[composition-smoke] {exception}");
            return 3;
        }
    }
}

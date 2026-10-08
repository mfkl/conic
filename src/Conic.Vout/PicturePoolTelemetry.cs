namespace Conic.Vout;

/// <summary>
/// Process-wide picture counters every video output publishes through
/// <c>vlc_avalonia_get_pool_telemetry</c>. "Opaque" pictures are GPU pictures:
/// D3D11 textures on Windows, CoreVideo pixel buffers on macOS. On macOS the
/// allocation and release counts track the pixel buffers lent to the app.
/// </summary>
internal readonly record struct PicturePoolTelemetrySnapshot(
    long OpaquePicturesReceived,
    long CpuPicturesReceived,
    long PoolGenerations,
    long OpaquePoolGenerations,
    long CpuPoolGenerations,
    long OpaquePicturesAllocated,
    long OpaquePicturesReleased,
    long CpuUploads)
{
    public long ReleaseBalance =>
        OpaquePicturesAllocated - OpaquePicturesReleased;
}

internal static class PicturePoolTelemetry
{
    private static long s_opaquePicturesReceived;
    private static long s_cpuPicturesReceived;
    private static long s_poolGenerations;
    private static long s_opaquePoolGenerations;
    private static long s_cpuPoolGenerations;
    private static long s_opaquePicturesAllocated;
    private static long s_opaquePicturesReleased;
    private static long s_cpuUploads;

    public static void OpaquePictureAllocated() =>
        Interlocked.Increment(ref s_opaquePicturesAllocated);

    public static void OpaquePictureReleased() =>
        Interlocked.Increment(ref s_opaquePicturesReleased);

    public static void OpaquePictureReceived() =>
        Interlocked.Increment(ref s_opaquePicturesReceived);

    public static void CpuPictureReceived() =>
        Interlocked.Increment(ref s_cpuPicturesReceived);

    public static void CpuUpload() =>
        Interlocked.Increment(ref s_cpuUploads);

    public static ulong OpaquePoolCreated()
    {
        Interlocked.Increment(ref s_opaquePoolGenerations);
        return checked((ulong)Interlocked.Increment(ref s_poolGenerations));
    }

    public static ulong CpuPoolCreated()
    {
        Interlocked.Increment(ref s_cpuPoolGenerations);
        return checked((ulong)Interlocked.Increment(ref s_poolGenerations));
    }

    public static PicturePoolTelemetrySnapshot Snapshot() => new(
        Interlocked.Read(ref s_opaquePicturesReceived),
        Interlocked.Read(ref s_cpuPicturesReceived),
        Interlocked.Read(ref s_poolGenerations),
        Interlocked.Read(ref s_opaquePoolGenerations),
        Interlocked.Read(ref s_cpuPoolGenerations),
        Interlocked.Read(ref s_opaquePicturesAllocated),
        Interlocked.Read(ref s_opaquePicturesReleased),
        Interlocked.Read(ref s_cpuUploads));
}

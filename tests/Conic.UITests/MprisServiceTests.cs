using Conic.Interop.Linux;
using Conic.Services;
using Conic.ViewModels;
using Player.Application;
using Player.Contracts;
using Tmds.DBus.Protocol;
using Xunit;

namespace Conic.UITests;

public sealed class SessionBusFactAttribute : FactAttribute
{
    public SessionBusFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))) Skip = "Needs a Linux session bus.";
    }
}

public sealed class MprisServiceTests
{
    private const string Destination = "org.mpris.MediaPlayer2.conic";
    private const string Player = "org.mpris.MediaPlayer2.Player";
    private const string ObjectPath = "/org/mpris/MediaPlayer2";

    [SessionBusFact]
    public async Task TheSessionBusSeesWhatPlaysAndControlsIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        Transport transport = new();
        PlayerPresentationViewModel vm = new(transport, new NoPicker(), () => { });
        using MediaSession session = new(vm);
        MprisService? mpris = await MprisService.StartAsync(session, () => { }, post: action => action());
        Assert.NotNull(mpris);
        session.Attach(mpris);
        vm.ApplyEngineState(new(1, PlaybackStatus.Playing, "sintel.mkv", 0.25, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), 0.5, true, null, Title: "Sintel"));

        using DBusConnection client = new(DBusAddress.Session!);
        await client.ConnectAsync();
        Dictionary<string, VariantValue> properties = await GetAllAsync(client, Player);
        Assert.Equal("Playing", properties["PlaybackStatus"].GetString());
        Assert.True(properties["CanSeek"].GetBool());
        Dictionary<string, VariantValue> metadata = properties["Metadata"].GetDictionary<string, VariantValue>();
        Assert.Equal("Sintel", metadata["xesam:title"].GetString());
        Assert.Equal(100_000_000L, metadata["mpris:length"].GetInt64());
        Assert.InRange(properties["Position"].GetInt64(), 25_000_000L, 30_000_000L);

        await CallAsync(client, "Pause");
        Assert.Equal(1, transport.Toggles);
        vm.ApplyEngineState(new(1, PlaybackStatus.Paused, "sintel.mkv", 0.25, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), 0.5, true, null, Title: "Sintel"));
        Assert.Equal("Paused", (await GetAllAsync(client, Player))["PlaybackStatus"].GetString());
        await CallAsync(client, "Seek", 25_000_000L);
        Assert.Equal([0.5], transport.Seeks);
    }

    [Fact]
    public async Task AnUnreachableBusLeavesConicWithoutMpris()
    {
        if (!OperatingSystem.IsLinux()) return;
        PlayerPresentationViewModel vm = new(new Transport(), new NoPicker(), () => { });
        using MediaSession session = new(vm);
        Task<MprisService?> start = MprisService.StartAsync(session, () => { }, address: "unix:path=/nonexistent/conic-test-bus");
        Assert.Same(start, await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Null(await start);
    }

    private static Task<Dictionary<string, VariantValue>> GetAllAsync(DBusConnection client, string name)
    {
        MessageBuffer message;
        using (MessageWriter writer = client.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(Destination, ObjectPath, "org.freedesktop.DBus.Properties", "GetAll", "s");
            writer.WriteString(name);
            message = writer.CreateMessage();
        }
        return client.CallMethodAsync(message, static (reply, _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue());
    }

    private static Task CallAsync(DBusConnection client, string member, long? offset = null)
    {
        MessageBuffer message;
        using (MessageWriter writer = client.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(Destination, ObjectPath, Player, member, offset is null ? null : "x");
            if (offset is { } value) writer.WriteInt64(value);
            message = writer.CreateMessage();
        }
        return client.CallMethodAsync(message);
    }

    private sealed class NoPicker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class Transport : IPlayerTransport
    {
        public int Toggles { get; private set; }
        public List<double> Seeks { get; } = [];
        public void Open(string source) { }
        public void TogglePause() => Toggles++;
        public void Stop() { }
        public void Seek(double normalizedPosition) => Seeks.Add(normalizedPosition);
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
    }
}

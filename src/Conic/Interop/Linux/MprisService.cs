using System.Runtime.Versioning;
using System.Text;
using Avalonia.Threading;
using Conic.Services;
using Tmds.DBus.Protocol;

namespace Conic.Interop.Linux;

[SupportedOSPlatform("linux")]
internal sealed class MprisService : IMediaSessionBackend, IPathMethodHandler
{
    public const string ObjectPath = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";
    private static readonly TimeSpan SeekedThreshold = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly ReadOnlyMemory<byte>[] Introspection =
    [
        Encoding.UTF8.GetBytes("""
            <interface name="org.mpris.MediaPlayer2">
              <method name="Raise"/>
              <method name="Quit"/>
              <property name="CanQuit" type="b" access="read"/>
              <property name="CanRaise" type="b" access="read"/>
              <property name="HasTrackList" type="b" access="read"/>
              <property name="Identity" type="s" access="read"/>
              <property name="SupportedUriSchemes" type="as" access="read"/>
              <property name="SupportedMimeTypes" type="as" access="read"/>
            </interface>
            """),
        Encoding.UTF8.GetBytes("""
            <interface name="org.mpris.MediaPlayer2.Player">
              <method name="Next"/>
              <method name="Previous"/>
              <method name="Pause"/>
              <method name="PlayPause"/>
              <method name="Stop"/>
              <method name="Play"/>
              <method name="Seek"><arg direction="in" type="x" name="Offset"/></method>
              <method name="SetPosition"><arg direction="in" type="o" name="TrackId"/><arg direction="in" type="x" name="Position"/></method>
              <method name="OpenUri"><arg direction="in" type="s" name="Uri"/></method>
              <signal name="Seeked"><arg type="x" name="Position"/></signal>
              <property name="PlaybackStatus" type="s" access="read"/>
              <property name="Rate" type="d" access="read"/>
              <property name="Metadata" type="a{sv}" access="read"/>
              <property name="Position" type="x" access="read"/>
              <property name="MinimumRate" type="d" access="read"/>
              <property name="MaximumRate" type="d" access="read"/>
              <property name="CanGoNext" type="b" access="read"/>
              <property name="CanGoPrevious" type="b" access="read"/>
              <property name="CanPlay" type="b" access="read"/>
              <property name="CanPause" type="b" access="read"/>
              <property name="CanSeek" type="b" access="read"/>
              <property name="CanControl" type="b" access="read"/>
            </interface>
            """),
    ];

    private readonly DBusConnection _connection;
    private readonly MediaSession _session;
    private readonly Action<Action> _post;
    private readonly Action _raise;
    private readonly object _gate = new();
    private MediaSessionState _state = MediaSessionState.Closed;
    private DateTime _stateAt = DateTime.UtcNow;
    private long _track;

    private MprisService(DBusConnection connection, MediaSession session, Action raise, Action<Action> post)
    {
        _connection = connection;
        _session = session;
        _raise = raise;
        _post = post;
    }

    public string Path => ObjectPath;

    public bool HandlesChildPaths => false;

    public static async Task<MprisService?> StartAsync(MediaSession session, Action raise, string? address = null, Action<Action>? post = null)
    {
        address ??= DBusAddress.Session;
        if (string.IsNullOrEmpty(address)) return null;
        DBusConnection connection = new(address);
        MprisService service = new(connection, session, raise, post ?? (action => Dispatcher.UIThread.Post(action)));
        Task<bool> start = service.ConnectAsync();
        try
        {
            if (await Task.WhenAny(start, Task.Delay(StartTimeout)) == start && await start) return service;
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or InvalidOperationException) { }
        connection.Dispose();
        return null;
    }

    private async Task<bool> ConnectAsync()
    {
        await _connection.ConnectAsync();
        _connection.AddMethodHandler(this);
        return await RequestNameAsync("org.mpris.MediaPlayer2.conic") ||
            await RequestNameAsync($"org.mpris.MediaPlayer2.conic.instance{Environment.ProcessId}");
    }

    public void Update(MediaSessionState state)
    {
        MediaSessionState previous;
        DateTime previousAt, now = DateTime.UtcNow;
        lock (_gate)
        {
            previous = _state;
            previousAt = _stateAt;
            if (state.Title != previous.Title || state.Duration != previous.Duration && previous.Duration == TimeSpan.Zero) _track++;
            _state = state;
            _stateAt = now;
        }
        Dictionary<string, VariantValue> changed = [];
        if (Status(state) != Status(previous)) changed["PlaybackStatus"] = Status(state);
        if (state.Rate != previous.Rate) changed["Rate"] = state.Rate;
        if (state.Title != previous.Title || state.Duration != previous.Duration || state.Subtitle != previous.Subtitle) changed["Metadata"] = Metadata(state).AsVariantValue();
        if (state.CanNext != previous.CanNext) changed["CanGoNext"] = state.CanNext;
        if (state.CanPrevious != previous.CanPrevious) changed["CanGoPrevious"] = state.CanPrevious;
        if (state.HasMedia != previous.HasMedia) { changed["CanPlay"] = state.HasMedia; changed["CanPause"] = state.HasMedia; }
        if (state.CanSeek != previous.CanSeek) changed["CanSeek"] = state.CanSeek;
        if (changed.Count != 0) EmitPropertiesChanged(changed);
        TimeSpan expected = previous.Position + (previous.Status == MediaSessionStatus.Playing ? (now - previousAt) * previous.Rate : TimeSpan.Zero);
        if (state.HasMedia && state.Title == previous.Title && (state.Position - expected).Duration() > SeekedThreshold) EmitSeeked(state.Position);
    }

    public void Dispose()
    {
        _connection.RemoveMethodHandler(ObjectPath);
        _connection.Dispose();
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(Introspection, Array.Empty<string>());
            return ValueTask.CompletedTask;
        }
        Message request = context.Request;
        string member = request.MemberAsString ?? string.Empty;
        switch (request.InterfaceAsString)
        {
            case PropertiesInterface:
                HandleProperties(context, member);
                break;
            case RootInterface:
                if (member == "Raise") _post(_raise);
                else if (member != "Quit") { context.ReplyUnknownMethodError(); return ValueTask.CompletedTask; }
                ReplyEmpty(context);
                break;
            case PlayerInterface:
                if (!HandlePlayer(context, member)) { context.ReplyUnknownMethodError(); return ValueTask.CompletedTask; }
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return ValueTask.CompletedTask;
    }

    private bool HandlePlayer(MethodContext context, string member)
    {
        switch (member)
        {
            case "Next": Press(MediaSessionButton.Next); return true;
            case "Previous": Press(MediaSessionButton.Previous); return true;
            case "Pause": Press(MediaSessionButton.Pause); return true;
            case "PlayPause": Press(MediaSessionButton.TogglePause); return true;
            case "Stop": Press(MediaSessionButton.Stop); return true;
            case "Play": Press(MediaSessionButton.Play); return true;
            case "OpenUri": return true;
            case "Seek":
            {
                long offset = context.Request.GetBodyReader().ReadInt64();
                TimeSpan target = CurrentPosition() + TimeSpan.FromTicks(offset * 10);
                _post(() => _session.SeekTo(target < TimeSpan.Zero ? TimeSpan.Zero : target));
                return true;
            }
            case "SetPosition":
            {
                Reader reader = context.Request.GetBodyReader();
                string track = reader.ReadObjectPathAsString();
                long position = reader.ReadInt64();
                if (track == TrackId() && position >= 0) _post(() => _session.SeekTo(TimeSpan.FromTicks(position * 10)));
                return true;
            }
            default:
                return false;
        }
    }

    private void HandleProperties(MethodContext context, string member)
    {
        Reader reader = context.Request.GetBodyReader();
        string name = reader.ReadString();
        if (member == "Get")
        {
            string property = reader.ReadString();
            if (Properties(name).TryGetValue(property, out VariantValue value))
            {
                using MessageWriter writer = context.CreateReplyWriter("v");
                writer.WriteVariant(value);
                context.Reply(writer.CreateMessage());
            }
            else context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property {property} on {name}.");
        }
        else if (member == "GetAll")
        {
            using MessageWriter writer = context.CreateReplyWriter("a{sv}");
            writer.WriteDictionary(Properties(name));
            context.Reply(writer.CreateMessage());
        }
        else if (member == "Set") context.ReplyError("org.freedesktop.DBus.Error.PropertyReadOnly", "Conic's MPRIS properties are read-only.");
        else context.ReplyUnknownMethodError();
    }

    private Dictionary<string, VariantValue> Properties(string name)
    {
        MediaSessionState state;
        lock (_gate) state = _state;
        if (name == RootInterface)
            return new()
            {
                ["CanQuit"] = false,
                ["CanRaise"] = true,
                ["HasTrackList"] = false,
                ["Identity"] = "Conic",
                ["SupportedUriSchemes"] = VariantValue.Array(new[] { "file", "http", "https", "rtsp", "rtp", "udp", "ftp", "smb" }),
                ["SupportedMimeTypes"] = VariantValue.Array(new[] { "video/mp4", "video/x-matroska", "video/webm", "audio/mpeg", "audio/flac", "audio/ogg" }),
            };
        if (name == PlayerInterface)
            return new()
            {
                ["PlaybackStatus"] = Status(state),
                ["Rate"] = state.Rate,
                ["Metadata"] = Metadata(state).AsVariantValue(),
                ["Position"] = CurrentPosition().Ticks / 10,
                ["MinimumRate"] = 0.25,
                ["MaximumRate"] = 4.0,
                ["CanGoNext"] = state.CanNext,
                ["CanGoPrevious"] = state.CanPrevious,
                ["CanPlay"] = state.HasMedia,
                ["CanPause"] = state.HasMedia,
                ["CanSeek"] = state.CanSeek,
                ["CanControl"] = true,
            };
        return [];
    }

    private Dict<string, VariantValue> Metadata(MediaSessionState state)
    {
        Dict<string, VariantValue> metadata = new() { ["mpris:trackid"] = new ObjectPath(TrackId()) };
        if (!state.HasMedia) return metadata;
        metadata["xesam:title"] = state.Title;
        if (state.Subtitle.Length != 0) metadata["xesam:album"] = state.Subtitle;
        if (state.Duration > TimeSpan.Zero) metadata["mpris:length"] = state.Duration.Ticks / 10;
        return metadata;
    }

    private string TrackId()
    {
        lock (_gate) return _state.HasMedia ? $"/org/mpris/MediaPlayer2/conic/track/{_track}" : NoTrack;
    }

    private TimeSpan CurrentPosition()
    {
        lock (_gate)
        {
            TimeSpan position = _state.Position;
            if (_state.Status == MediaSessionStatus.Playing) position += (DateTime.UtcNow - _stateAt) * _state.Rate;
            return _state.Duration > TimeSpan.Zero && position > _state.Duration ? _state.Duration : position;
        }
    }

    private static string Status(MediaSessionState state) => state.Status switch
    {
        MediaSessionStatus.Playing or MediaSessionStatus.Opening => "Playing",
        MediaSessionStatus.Paused => "Paused",
        _ => "Stopped",
    };

    private void Press(MediaSessionButton button) => _post(() => _session.Press(button));

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected) return;
        using MessageWriter writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }

    private void EmitPropertiesChanged(Dictionary<string, VariantValue> changed)
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
        writer.WriteString(PlayerInterface);
        writer.WriteDictionary(changed);
        writer.WriteArray(Array.Empty<string>());
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private void EmitSeeked(TimeSpan position)
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, PlayerInterface, "Seeked", "x");
        writer.WriteInt64(position.Ticks / 10);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private async Task<bool> RequestNameAsync(string name)
    {
        uint reply = await _connection.CallMethodAsync(RequestNameMessage(name), static (message, _) => message.GetBodyReader().ReadUInt32());
        return reply is 1 or 4;
    }

    private MessageBuffer RequestNameMessage(string name)
    {
        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su");
        writer.WriteString(name);
        writer.WriteUInt32(4);
        return writer.CreateMessage();
    }
}

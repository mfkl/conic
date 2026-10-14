using System.Globalization;
using System.Text;

namespace Player.Application;

/// <summary>VLC's message types, in VLC's order (VLC_MSG_INFO to VLC_MSG_DBG).</summary>
public enum EngineMessageType { Info = 0, Error = 1, Warning = 2, Debug = 3 }

/// <summary>One engine log message, as VLC's Messages window lists it.</summary>
public sealed record EngineMessage(long Sequence, DateTime Time, EngineMessageType Type, string Module, string ObjectType, string Text)
{
    public string TypeName => NameOf(Type);
    public bool IsInfo => Type == EngineMessageType.Info;
    public bool IsError => Type == EngineMessageType.Error;
    public bool IsWarning => Type == EngineMessageType.Warning;
    public bool IsDebug => Type == EngineMessageType.Debug;
    /// <summary>"module type", as the list shows the message's source.</summary>
    public string Source => $"{Module} {ObjectType}";

    public static string NameOf(EngineMessageType type) => type switch
    {
        EngineMessageType.Error => "error",
        EngineMessageType.Warning => "warning",
        EngineMessageType.Debug => "debug",
        _ => "info",
    };

    /// <summary>The line VLC's Messages window shows and saves: "module type kind: text".</summary>
    public string Line => Type == EngineMessageType.Info
        ? $"{Module} {ObjectType}: {Text}"
        : $"{Module} {ObjectType} {TypeName}: {Text}";

    public string TimedLine => $"{Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {Line}";

    public bool Matches(string? filter) =>
        string.IsNullOrWhiteSpace(filter) || Line.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The engine's recent log messages, kept while the app runs so the Messages
/// window shows what happened before it opened. VLC's verbosity levels: 0
/// keeps errors and information, 1 adds warnings, 2 adds debug messages.
/// Thread safe: VLC logs from its own threads.
/// </summary>
public sealed class EngineMessageLog(int capacity = EngineMessageLog.DefaultCapacity)
{
    public const int DefaultCapacity = 20_000;
    public const int MaximumVerbosity = 2;

    private readonly EngineMessage?[] _ring = new EngineMessage?[Math.Max(1, capacity)];
    private readonly object _gate = new();
    private int _start;
    private int _count;
    private long _sequence;
    private int _verbosity;

    public int Capacity => _ring.Length;

    public int Verbosity
    {
        get => Volatile.Read(ref _verbosity);
        set => Volatile.Write(ref _verbosity, Math.Clamp(value, 0, MaximumVerbosity));
    }

    /// <summary>Whether a message of this type is kept at the current verbosity (VLC's rule).</summary>
    public bool Accepts(EngineMessageType type) => IsShownAt(type, Verbosity);

    public static bool IsShownAt(EngineMessageType type, int verbosity) => (int)type - (int)EngineMessageType.Error <= verbosity;

    /// <summary>The sequence number of the last message added, or 0.</summary>
    public long LastSequence { get { lock (_gate) return _sequence; } }

    public EngineMessage Add(EngineMessageType type, string module, string objectType, string text)
    {
        lock (_gate)
        {
            EngineMessage message = new(++_sequence, DateTime.Now, type, module, objectType, text);
            if (_count < _ring.Length)
            {
                _ring[(_start + _count) % _ring.Length] = message;
                _count++;
            }
            else
            {
                _ring[_start] = message;
                _start = (_start + 1) % _ring.Length;
            }
            return message;
        }
    }

    /// <summary>The kept messages after <paramref name="afterSequence"/>, oldest first.</summary>
    public IReadOnlyList<EngineMessage> Since(long afterSequence)
    {
        lock (_gate)
        {
            List<EngineMessage> messages = [];
            for (int index = 0; index < _count; index++)
            {
                EngineMessage message = _ring[(_start + index) % _ring.Length]!;
                if (message.Sequence > afterSequence) messages.Add(message);
            }
            return messages;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _start = 0;
            _count = 0;
        }
    }

    /// <summary>The text VLC's Messages window saves: one timed line per message.</summary>
    public static string Format(IEnumerable<EngineMessage> messages)
    {
        StringBuilder text = new();
        foreach (EngineMessage message in messages) text.AppendLine(message.TimedLine);
        return text.ToString();
    }
}

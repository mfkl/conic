using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.Application;

namespace Conic.ViewModels;

public sealed record VerbosityChoice(int Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// VLC's Messages window: the engine's log at a chosen verbosity, filtered by
/// text. The verbosity applies to the log itself, so messages VLC sends from
/// then on are kept or dropped at once, as in VLC.
/// </summary>
internal sealed class MessagesViewModel : INotifyPropertyChanged
{
    private readonly EngineMessageLog _log;
    private readonly List<EngineMessage> _kept = [];
    private long _lastSequence;
    private string _filter = "";

    public MessagesViewModel(EngineMessageLog log)
    {
        _log = log;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VerbosityChoice> VerbosityChoices { get; } =
    [
        new(0, "Errors and information"),
        new(1, "Warnings"),
        new(2, "Debug"),
    ];

    public VerbosityChoice SelectedVerbosity
    {
        get => VerbosityChoices[_log.Verbosity];
        set
        {
            if (value is null || value.Level == _log.Verbosity) return;
            _log.Verbosity = value.Level;
            OnPropertyChanged();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter == (value ?? "")) return;
            _filter = value ?? "";
            Messages = new(_kept.Where(message => message.Matches(_filter)));
            OnPropertyChanged();
            OnPropertyChanged(nameof(Messages));
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>The kept messages that match <see cref="Filter"/>, oldest first.</summary>
    public ObservableCollection<EngineMessage> Messages { get; private set; } = [];

    public string Summary => _kept.Count == Messages.Count
        ? $"{Messages.Count:N0} {(Messages.Count == 1 ? "message" : "messages")}"
        : $"{Messages.Count:N0} of {_kept.Count:N0} messages";

    /// <summary>Takes the messages logged since the last call; returns how many matched the filter.</summary>
    public int Refresh()
    {
        IReadOnlyList<EngineMessage> added = _log.Since(_lastSequence);
        if (added.Count == 0) return 0;
        _lastSequence = added[^1].Sequence;
        _kept.AddRange(added);
        int matched = 0;
        foreach (EngineMessage message in added)
        {
            if (!message.Matches(_filter)) continue;
            Messages.Add(message);
            matched++;
        }
        // The log keeps its newest messages only; drop what it dropped.
        int overflow = _kept.Count - _log.Capacity;
        if (overflow > 0)
        {
            long oldest = _kept[overflow].Sequence;
            _kept.RemoveRange(0, overflow);
            while (Messages.Count > 0 && Messages[0].Sequence < oldest) Messages.RemoveAt(0);
        }
        OnPropertyChanged(nameof(Summary));
        return matched;
    }

    public void Clear()
    {
        _log.Clear();
        _kept.Clear();
        Messages.Clear();
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>The shown messages, as VLC saves them.</summary>
    public string Text(IEnumerable<EngineMessage>? selection = null) => EngineMessageLog.Format(selection ?? Messages);

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

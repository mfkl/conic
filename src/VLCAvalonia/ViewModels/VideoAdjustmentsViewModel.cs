using System.ComponentModel;
using Player.Application;

namespace VLCAvalonia.ViewModels;

internal sealed class VideoAdjustmentsViewModel : INotifyPropertyChanged
{
    private readonly IVideoAdjustmentTransport? _transport;
    private VideoAdjustmentState _state = VideoAdjustmentState.Unavailable;
    private bool _applying;
    public VideoAdjustmentsViewModel(IVideoAdjustmentTransport? transport)
    {
        _transport = transport;
        if (transport is not null)
        {
            Apply(transport.VideoAdjustments);
            transport.VideoAdjustmentsChanged += (_, state) => Apply(state);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Available => _state.Available;
    public IReadOnlyList<VideoChoice> Aspects => _state.Aspects;
    public IReadOnlyList<VideoChoice> Crops => _state.Crops;
    public IReadOnlyList<VideoChoice> Zooms => _state.Zooms;
    public VideoChoice? Aspect { get => Aspects.FirstOrDefault(x => x.Value == _state.Aspect); set => Select("aspect-ratio", value, Aspects); }
    public VideoChoice? Crop { get => Crops.FirstOrDefault(x => x.Value == _state.Crop); set => Select("crop", value, Crops); }
    public VideoChoice? Zoom { get => Zooms.FirstOrDefault(x => x.Value == _state.Zoom); set => Select("zoom", value, Zooms); }
    public void Reset()
    {
        if (!Available) return;
        Select("aspect-ratio", Aspects.FirstOrDefault(x => x.Value == ""), Aspects);
        Select("crop", Crops.FirstOrDefault(x => x.Value == ""), Crops);
        Select("zoom", Zooms.FirstOrDefault(x => x.Value == "fit"), Zooms);
    }
    private void Select(string setting, VideoChoice? choice, IReadOnlyList<VideoChoice> choices)
    {
        if (_applying) return;
        string current = setting == "aspect-ratio" ? _state.Aspect : setting == "crop" ? _state.Crop : _state.Zoom;
        if (Available && choice is not null && choice.Value != current && choices.Contains(choice)) _transport?.SetVideoAdjustment(_state, setting, choice.Value);
    }
    internal void Apply(VideoAdjustmentState state)
    {
        if (state.QueueGeneration < _state.QueueGeneration || (state.QueueGeneration == _state.QueueGeneration && state.Available && state.MediaGeneration < _state.MediaGeneration)) return;
        bool aspectsChanged = !_state.Aspects.SequenceEqual(state.Aspects);
        bool cropsChanged = !_state.Crops.SequenceEqual(state.Crops);
        bool zoomsChanged = !_state.Zooms.SequenceEqual(state.Zooms);
        _state = state with
        {
            Aspects = aspectsChanged ? state.Aspects : _state.Aspects,
            Crops = cropsChanged ? state.Crops : _state.Crops,
            Zooms = zoomsChanged ? state.Zooms : _state.Zooms,
        };
        _applying = true;
        try
        {
            if (aspectsChanged) PropertyChanged?.Invoke(this, new(nameof(Aspects)));
            if (cropsChanged) PropertyChanged?.Invoke(this, new(nameof(Crops)));
            if (zoomsChanged) PropertyChanged?.Invoke(this, new(nameof(Zooms)));
            foreach (string property in new[] { nameof(Available), nameof(Aspect), nameof(Crop), nameof(Zoom) })
                PropertyChanged?.Invoke(this, new(property));
        }
        finally { _applying = false; }
    }
}

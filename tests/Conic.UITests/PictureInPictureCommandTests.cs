using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

public sealed class PictureInPictureCommandTests
{
    [Fact]
    public void EntryRequiresUsablePlaybackButReturnIsAvailableAfterAnEngineFault()
    {
        PlayerPresentationViewModel vm = new(new Transport(), new Picker(), () => { });
        int requests = 0;
        vm.PictureInPictureRequested += (_, _) => requests++;
        Assert.False(vm.TogglePictureInPictureCommand.CanExecute(null));
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Opening });
        Assert.False(vm.TogglePictureInPictureCommand.CanExecute(null));
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Playing });
        vm.TogglePictureInPictureCommand.Execute(null);
        Assert.Equal(1, requests);
        vm.IsPictureInPicture = true;
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Faulted });
        Assert.True(vm.TogglePictureInPictureCommand.CanExecute(null));
        vm.TogglePictureInPictureCommand.Execute(null);
        Assert.Equal(2, requests);
    }

    [Fact]
    public void OpeningRequestsFullLayoutBeforePickerEvenWhenCancelled()
    {
        List<string> calls = [];
        PlayerPresentationViewModel vm = new(new Transport(), new Picker(() => calls.Add("picker")), () => { });
        vm.MainLayoutRequested += (_, _) => calls.Add("layout");
        vm.OpenMediaCommand.Execute(null);
        Assert.Equal(new[] { "layout", "picker" }, calls);
        Assert.True(vm.IsHomeOpen);
    }

    private sealed class Picker(Action? onPick = null) : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) { onPick?.Invoke(); return Task.FromResult<string?>(null); }
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class Transport : IPlayerTransport
    {
        public void Open(string source) => throw new InvalidOperationException("Window commands must not open media");
        public void TogglePause() => throw new InvalidOperationException();
        public void Stop() => throw new InvalidOperationException();
        public void Seek(double normalizedPosition) => throw new InvalidOperationException();
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
    }
}

using Player.Application;
using Player.Contracts;
using Conic.ViewModels;
using Xunit;

namespace Conic.UITests;

public sealed class HomeNavigationTests
{
    [Fact]
    public void BrowsingHomeDoesNotReopenOrStopPlaybackAndPollingPreservesRoute()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Create(transport);
        Assert.True(vm.IsHomeOpen);
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Opening });
        Assert.False(vm.IsHomeOpen);
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Playing });
        vm.ShowHomeCommand.Execute(null);
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Playing, Position = .5 });
        Assert.True(vm.IsHomeOpen);
        Assert.True(vm.ShowMiniPlayer);
        vm.ShowPlayerCommand.Execute(null);
        Assert.False(vm.IsHomeOpen);
        Assert.False(vm.ShowMiniPlayer);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void MissingSourceAndCancelledPickerKeepTheHomeRoute()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Create(transport, exists: false);
        vm.OpenSource(Path.GetFullPath("missing.mp4"), MediaSourceKind.File);
        Assert.True(vm.IsHomeOpen);
        Assert.True(vm.HasOpenError);
        vm.OpenMediaCommand.Execute(null);
        Assert.True(vm.IsHomeOpen);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void HomeCombinesExistingHistoryAndFiltersWithoutOpeningMedia()
    {
        MediaHistoryService history = new(new Storage());
        MediaSource first = new(Path.GetFullPath("mountains.mp4"), MediaSourceKind.File, "Mountains");
        MediaSource second = new(Path.GetFullPath("ocean.mp4"), MediaSourceKind.File, "Ocean");
        history.RecordOpened(first, true);
        history.RecordOpened(second, true);
        history.AddFavorite(first);
        Transport transport = new();
        PlayerPresentationViewModel vm = Create(transport, history);
        Assert.Equal(2, vm.HomeItems.Count);
        vm.HomeSearch = "MOUNTAIN";
        Assert.Equal("Mountains", Assert.Single(vm.HomeItems).Title);
        vm.HomeSearch = "missing";
        Assert.True(vm.ShowHomeEmpty);
        vm.HomeSearch = "";
        vm.SelectHomeSectionCommand.Execute("Favorites");
        HomeMediaItem favorite = Assert.Single(vm.HomeItems);
        vm.RemoveHomeItemCommand.Execute(favorite);
        Assert.Empty(history.Favorites);
        Assert.Equal(2, history.Recent.Count);
        Assert.Empty(vm.HomeItems);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void SuccessfulOpenLeavesHomeOnlyAfterValidation()
    {
        Transport transport = new();
        PlayerPresentationViewModel vm = Create(transport);
        vm.OpenSource(Path.GetFullPath("movie.mp4"), MediaSourceKind.File);
        Assert.False(vm.IsHomeOpen);
        Assert.Equal("open", Assert.Single(transport.Calls));
    }

    private static PlayerPresentationViewModel Create(Transport transport, MediaHistoryService? history = null, bool exists = true) =>
        new(transport, new Picker(), () => { }, sourceValidator: new(new Files(exists)), history: history);
    private sealed class Files(bool exists) : IMediaFileSystem
    {
        public bool FileExists(string path) => exists;
        public bool DirectoryExists(string path) => false;
        public string GetFullPath(string path) => Path.GetFullPath(path);
    }
    private sealed class Storage : IMediaHistoryStorage
    {
        public string? Read() => null;
        public void WriteAtomically(string content) { }
    }
    private sealed class Picker : IMediaSourcePicker
    {
        public Task<string?> PickMediaAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
    private sealed class Transport : IPlayerTransport
    {
        public List<string> Calls { get; } = [];
        public void Open(string source) => Calls.Add("open");
        public void TogglePause() => Calls.Add("pause");
        public void Stop() => Calls.Add("stop");
        public void Seek(double normalizedPosition) => Calls.Add("seek");
        public void SetVolume(double normalizedVolume) { }
        public void SetMuted(bool muted) { }
        public void Skip(TimeSpan offset) { }
        public void SetPlaybackRate(double playbackRate) { }
    }
}

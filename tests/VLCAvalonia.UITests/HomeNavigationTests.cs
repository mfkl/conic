using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Player.Application;
using Player.Contracts;
using VLCAvalonia.Controls;
using VLCAvalonia.ViewModels;
using Xunit;

namespace VLCAvalonia.UITests;

public sealed class HomeNavigationTests
{
    [Theory]
    [InlineData(PlaybackStatus.Stopped)]
    [InlineData(PlaybackStatus.Closed)]
    [InlineData(PlaybackStatus.Faulted)]
    public void FinishedPlaybackClearsTheMiniPlayerButKeepsHistory(PlaybackStatus terminal)
    {
        Transport transport = new();
        MediaHistoryService history = new(new Storage());
        var vm = Create(transport, history);
        vm.OpenSource(Path.GetFullPath("movie.mp4"), MediaSourceKind.File);
        var state = MediaEngineState.Initial with { Generation = 1, Source = Path.GetFullPath("movie.mp4") };
        vm.ApplyEngineState(state with { Status = PlaybackStatus.Playing });
        Assert.Equal(PlaybackStatus.Playing, vm.PlaybackStatus);
        vm.ShowHomeCommand.Execute(null);
        Assert.True(vm.ShowMiniPlayer);
        vm.ApplyEngineState(state with { Status = PlaybackStatus.Paused });
        Assert.True(vm.ShowMiniPlayer);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.ApplyEngineState(state with { Status = terminal });
        Assert.False(vm.ShowMiniPlayer);
        Assert.False(vm.CanStop);
        Assert.Contains(nameof(vm.ShowMiniPlayer), changed);
        Assert.Single(vm.HomeItems);
        Assert.Single(history.Recent);
    }

    [Fact]
    public void MiniPlayerStopUsesTheSharedTransportAndClearsOnReadback()
    {
        Transport transport = new();
        var vm = Create(transport);
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Playing });
        vm.ShowHomeCommand.Execute(null);
        vm.StopCommand.Execute(null);
        Assert.Equal("stop", Assert.Single(transport.Calls));
        vm.ApplyEngineState(MediaEngineState.Initial with { Generation = 1, Status = PlaybackStatus.Stopped });
        Assert.False(vm.ShowMiniPlayer);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

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
    public void HomeItemCountFollowsTheCardsShown()
    {
        MediaHistoryService history = new(new Storage());
        history.RecordOpened(new(Path.GetFullPath("mountains.mp4"), MediaSourceKind.File, "Mountains"), true);
        history.RecordOpened(new(Path.GetFullPath("ocean.mp4"), MediaSourceKind.File, "Ocean"), true);
        PlayerPresentationViewModel vm = Create(new Transport(), history);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("2 items", vm.HomeItemCountText);
        vm.HomeSearch = "ocean";
        Assert.Equal("1 item", vm.HomeItemCountText);
        Assert.Contains(nameof(vm.HomeItemCountText), changed);
        vm.HomeSearch = "missing";
        Assert.Equal("0 items", vm.HomeItemCountText);
    }

    [Fact]
    public void HomeSectionsSplitHistoryAndHideEmptyOnes()
    {
        MediaHistoryService history = new(new Storage());
        MediaSource[] sources = new[] { "a.mp4", "b.mp4", "c.mp4" }.Select(x => new MediaSource(Path.GetFullPath(x), MediaSourceKind.File, x)).ToArray();
        foreach (MediaSource source in sources.Reverse()) history.RecordOpened(source, true);
        history.Checkpoint(sources[1], TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120), true, 0, 0);
        PlayerPresentationViewModel vm = Create(new Transport(), history);

        Assert.True(vm.ShowHomeSections);
        Assert.False(vm.ShowHomeGrid);
        Assert.Empty(vm.HomeGridItems);
        HomeMediaItem resume = Assert.Single(vm.HomeContinueItems);
        Assert.Equal(("b.mp4", true, .25), (resume.Title, resume.HasProgress, resume.Progress));
        Assert.Equal(["a.mp4", "b.mp4", "c.mp4"], vm.HomeRecentItems.Select(x => x.Title));
        Assert.True(vm.HasHomeContinueItems && vm.HasHomeRecentItems);
        Assert.False(vm.HasHomeFavoriteItems);

        vm.AddFavoriteCommand.Execute(sources[2].CanonicalSource);
        Assert.True(vm.HasHomeFavoriteItems);
        Assert.True(Assert.Single(vm.HomeFavoriteItems).IsFavorite);
        Assert.True(vm.HomeRecentItems[2].IsFavorite);
        Assert.False(vm.HomeRecentItems[0].IsFavorite);

        vm.HomeSearch = "c.mp4";
        Assert.False(vm.ShowHomeSections);
        Assert.True(vm.ShowHomeGrid);
        Assert.Same(vm.HomeItems, vm.HomeGridItems);
        Assert.Empty(vm.HomeRecentItems);
        vm.HomeSearch = "";
        vm.SelectHomeSectionCommand.Execute("Recent");
        Assert.False(vm.ShowHomeSections);
        Assert.Equal(3, vm.HomeGridItems.Count);
        vm.SelectHomeSectionCommand.Execute("Home");
        Assert.True(vm.ShowHomeSections);
        Assert.Equal(3, vm.HomeRecentItems.Count);
    }

    [Fact]
    public void HomeSectionLimitTrimsAndRestoresCardsInPlace()
    {
        MediaHistoryService history = new(new Storage());
        foreach (string name in new[] { "e.mp4", "d.mp4", "c.mp4", "b.mp4", "a.mp4" })
            history.RecordOpened(new(Path.GetFullPath(name), MediaSourceKind.File, name), true);
        PlayerPresentationViewModel vm = Create(new Transport(), history);
        IReadOnlyList<HomeMediaItem> recent = vm.HomeRecentItems;
        HomeMediaItem first = recent[0], second = recent[1];
        List<NotifyCollectionChangedAction> changes = [];
        ((INotifyCollectionChanged)recent).CollectionChanged += (_, e) => changes.Add(e.Action);

        vm.HomeSectionCardLimit = 2;
        Assert.Equal(["a.mp4", "b.mp4"], recent.Select(x => x.Title));
        Assert.All(changes, x => Assert.Equal(NotifyCollectionChangedAction.Remove, x));
        changes.Clear();
        vm.HomeSectionCardLimit = 4;
        Assert.Equal(["a.mp4", "b.mp4", "c.mp4", "d.mp4"], recent.Select(x => x.Title));
        Assert.All(changes, x => Assert.Equal(NotifyCollectionChangedAction.Add, x));
        Assert.Same(recent, vm.HomeRecentItems);
        Assert.Same(first, recent[0]);
        Assert.Same(second, recent[1]);
        vm.HomeSectionCardLimit = 0;
        Assert.Single(recent);
    }

    [Fact]
    public void SyncInPlaceKeepsSurvivingCards()
    {
        HomeMediaItem Card(string source, string detail = "") => new(source, source, detail, 0, false, PlayerCommandId.RemoveRecent);
        HomeMediaItem a = Card("a"), b = Card("b"), c = Card("c");
        ObservableCollection<HomeMediaItem> target = [a, b, c];
        HomeMediaItem changedA = Card("a", "changed"), d = Card("d");

        PlayerPresentationViewModel.SyncInPlace(target, [c, changedA, d]);

        Assert.Equal([c, changedA, d], target);
        Assert.Same(c, target[0]);
        PlayerPresentationViewModel.SyncInPlace(target, []);
        Assert.Empty(target);
    }

    [Theory]
    [InlineData(1663, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, 5)] // the design: 5 x 316 at 1920
    [InlineData(810, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, 3)]
    [InlineData(400, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, 2)]
    [InlineData(300, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, 1)]
    [InlineData(0, MediaGridPanel.CardMinWidth, MediaGridPanel.CardMaxWidth, 1)]
    [InlineData(1663, MediaGridPanel.RowMinWidth, MediaGridPanel.RowMaxWidth, 4)]
    [InlineData(390, MediaGridPanel.RowMinWidth, MediaGridPanel.RowMaxWidth, 1)]
    public void MediaGridColumnsKeepCardsWithinTheirWidths(double width, double min, double max, int columns) =>
        Assert.Equal(columns, MediaGridPanel.ColumnCount(width, min, max, MediaGridPanel.DefaultSpacing));

    [Fact]
    public void MiniPlayerNamesTheFolderOfThePlayingFile()
    {
        string movie = Path.Combine(Path.GetTempPath(), "Shows", "S01", "movie.mp4");
        PlayerPresentationViewModel vm = Create(new Transport());
        Assert.Null(vm.NowPlayingSource);
        Assert.False(vm.HasNowPlayingFolder);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.OpenSource(movie, MediaSourceKind.File);
        Assert.Equal(movie, vm.NowPlayingSource);
        Assert.Equal("S01", vm.NowPlayingFolder);
        Assert.True(vm.HasNowPlayingFolder);
        Assert.Contains(nameof(vm.NowPlayingFolder), changed);
        Assert.Contains(nameof(vm.HasNowPlayingFolder), changed);
    }

    [Fact]
    public void MiniPlayerFolderNamesFilesUrlsAndStreams()
    {
        string file = Path.Combine(Path.GetTempPath(), "Music", "song.flac");
        Assert.Equal("Music", PlayerPresentationViewModel.FolderNameOf(file));
        Assert.Equal("Music", PlayerPresentationViewModel.FolderNameOf(new Uri(file).AbsoluteUri));
        Assert.Equal("example.com", PlayerPresentationViewModel.FolderNameOf("https://example.com/live/stream.m3u8"));
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root, PlayerPresentationViewModel.FolderNameOf(Path.Combine(root, "movie.mp4")));
        Assert.Equal("", PlayerPresentationViewModel.FolderNameOf(null));
        Assert.Equal("", PlayerPresentationViewModel.FolderNameOf("movie.mp4"));
    }

    [Fact]
    public void MiniPlayerFollowsTheQueuesCurrentItemAndOffersNavigationForSeveral()
    {
        string first = Path.Combine(Path.GetTempPath(), "A", "one.mp4"), second = Path.Combine(Path.GetTempPath(), "B", "two.mp4");
        PlayerPresentationViewModel vm = Create(new Transport());
        vm.OpenSource(first, MediaSourceKind.File);
        Assert.False(vm.HasQueueNavigation);
        List<string?> changed = [];
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.ApplyQueueState(new(1, 1, QueueLoadState.Ready, [new("1", "one.mp4", first, 0, false), new("2", "two.mp4", second, 1, true)], 1, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.True(vm.HasQueueNavigation);
        Assert.Contains(nameof(vm.HasQueueNavigation), changed);
        Assert.Equal(second, vm.NowPlayingSource);
        Assert.Equal("B", vm.NowPlayingFolder);
        vm.ApplyQueueState(new(1, 2, QueueLoadState.Ready, [new("1", "one.mp4", first, 0, true)], 0, QueueRepeatMode.None, QueueOrder.Normal));
        Assert.False(vm.HasQueueNavigation);
        Assert.Equal("A", vm.NowPlayingFolder);
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

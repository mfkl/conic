using System.Collections.ObjectModel;
using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed record HomeMediaItem(string Source, string Title, string Detail,
    double Progress, bool HasProgress, PlayerCommandId RemoveCommand, bool IsFavorite = false);

internal sealed partial class PlayerPresentationViewModel
{
    private bool _isHomeOpen = true;
    private bool _homeListLayout;
    private string _homeSection = "Home";
    private string _homeSearch = string.Empty;
    private long _homeMediaGeneration = -1;
    private readonly ObservableCollection<HomeMediaItem> _homeItems = [];
    private readonly ObservableCollection<HomeMediaItem> _homeContinueItems = [];
    private readonly ObservableCollection<HomeMediaItem> _homeRecentItems = [];
    private readonly ObservableCollection<HomeMediaItem> _homeFavoriteItems = [];
    private int _homeSectionCardLimit = 5;
    public ICommand ShowHomeCommand { get; private set; } = null!;
    public ICommand ShowPlayerCommand { get; private set; } = null!;
    public ICommand SelectHomeSectionCommand { get; private set; } = null!;
    public ICommand RemoveHomeItemCommand { get; private set; } = null!;
    public ICommand EnqueueHomeItemCommand { get; private set; } = null!;
    public ICommand ToggleHomeLayoutCommand { get; private set; } = null!;

    private void InitializeHomeCommands(PlayerCommandRegistry registry)
    {
        registry.Register(new(PlayerCommandId.ShowHome, "Home", "Navigation", "Alt+Home"), _ => IsHomeOpen = true);
        registry.Register(new(PlayerCommandId.ShowPlayer, "Return to player", "Navigation"), _ => IsHomeOpen = false, _ => HasPlayableMedia);
        registry.Register(new(PlayerCommandId.SelectHomeSection, "Browse library section", "Navigation"), p =>
        {
            if (p is string section && section is "Home" or "Recent" or "Favorites" or "Continue watching")
            { HomeSection = section; IsHomeOpen = true; }
        });
        registry.Register(new(PlayerCommandId.RemoveHomeItem, "Remove library item", "Library"), p =>
        {
            if (p is HomeMediaItem item) registry.Execute(item.RemoveCommand, item.Source);
        }, p => p is HomeMediaItem);
        registry.Register(new(PlayerCommandId.Enqueue, "Add to queue", "Library"), p =>
        {
            MediaOpenResult result = _sourceValidator.Validate(p as string, MediaSourceKind.Recent);
            if (!result.Succeeded || result.Source is null) { OpenError = result.UserMessage ?? "Media could not be queued."; return; }
            try { _queue?.Enqueue([result.Source.CanonicalSource]); OpenError = string.Empty; }
            catch (Exception e) { _diagnostic?.Invoke(e.ToString()); OpenError = "Media could not be queued."; }
        }, p => _queue is not null && p is string);
        registry.Register(new(PlayerCommandId.ToggleHomeLayout, "Switch grid or list", "Library"), _ => HomeListLayout = !HomeListLayout);
        ShowHomeCommand = new RegistryCommand(registry, PlayerCommandId.ShowHome);
        ShowPlayerCommand = new RegistryCommand(registry, PlayerCommandId.ShowPlayer);
        SelectHomeSectionCommand = new RegistryCommand(registry, PlayerCommandId.SelectHomeSection);
        RemoveHomeItemCommand = new RegistryCommand(registry, PlayerCommandId.RemoveHomeItem);
        EnqueueHomeItemCommand = new RegistryCommand(registry, PlayerCommandId.Enqueue);
        ToggleHomeLayoutCommand = new RegistryCommand(registry, PlayerCommandId.ToggleHomeLayout);
        _homeItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HomeItemCountText));
        SyncHomeItems();
        SyncHomeSections();
    }

    public bool IsHomeOpen
    {
        get => _isHomeOpen;
        set
        {
            if (!Set(ref _isHomeOpen, value)) return;
            if (value) { IsQueueOpen = false; IsLibraryOpen = false; }
            OnPropertyChanged(nameof(ShowMiniPlayer));
        }
    }
    public bool ShowMiniPlayer => IsHomeOpen && CanStop;
    public bool HomeListLayout { get => _homeListLayout; set { if (Set(ref _homeListLayout, value)) OnPropertyChanged(nameof(HomeLayoutLabel)); } }
    public string HomeLayoutLabel => HomeListLayout ? "Grid view" : "List view";
    public string HomeSection { get => _homeSection; set { if (Set(ref _homeSection, value)) NotifyHome(); } }
    public bool IsHomeSection => HomeSection == "Home";
    public bool IsRecentSection => HomeSection == "Recent";
    public bool IsFavoritesSection => HomeSection == "Favorites";
    public bool IsContinueSection => HomeSection == "Continue watching";
    public string HomeSearch { get => _homeSearch; set { if (Set(ref _homeSearch, value)) NotifyHome(); } }
    public bool HomeHistoryPrivate => !(_settings?.Current.RememberRecentMedia ?? true) || !(_settings?.Current.RememberResumeHistory ?? true);
    public string HomeEmptyMessage => !string.IsNullOrWhiteSpace(HomeSearch) ? "No media matches your search." : HomeSection switch
    {
        "Favorites" => "Keep your favorites here. Add one from a media card or the library.",
        "Continue watching" => "Saved playback positions will appear here.",
        "Recent" => "Media you open will appear here.",
        _ => "Open a file or drop media anywhere to get started."
    };
    public bool HomeHasItems => HomeItems.Count != 0;
    /// <summary>The Home route shows its sections (Continue watching, Recently played, Favorites) unless a search is active.</summary>
    public bool ShowHomeSections => IsHomeSection && string.IsNullOrWhiteSpace(HomeSearch) && HomeHasItems;
    /// <summary>Section routes and search results show every card in one grid or list.</summary>
    public bool ShowHomeGrid => HomeHasItems && !ShowHomeSections;
    /// <summary>The grid's cards; empty while the sections are shown, so the hidden grid holds no thumbnails.</summary>
    public IReadOnlyList<HomeMediaItem> HomeGridItems => ShowHomeSections ? [] : _homeItems;
    /// <summary>The first cards of each Home section, as many as fit (set by the view); empty while the sections are hidden. Updated in place like <see cref="HomeItems"/>.</summary>
    public IReadOnlyList<HomeMediaItem> HomeContinueItems => _homeContinueItems;
    public IReadOnlyList<HomeMediaItem> HomeRecentItems => _homeRecentItems;
    public IReadOnlyList<HomeMediaItem> HomeFavoriteItems => _homeFavoriteItems;
    public bool HasHomeContinueItems => _homeContinueItems.Count != 0;
    public bool HasHomeRecentItems => _homeRecentItems.Count != 0;
    public bool HasHomeFavoriteItems => _homeFavoriteItems.Count != 0;
    /// <summary>How many cards each Home section shows; the view sets it from the width it has.</summary>
    public int HomeSectionCardLimit
    {
        get => _homeSectionCardLimit;
        set { if (Set(ref _homeSectionCardLimit, Math.Max(1, value))) SyncHomeSections(); }
    }
    /// <summary>The content header's count of the cards shown (after search).</summary>
    public string HomeItemCountText => HomeItems.Count == 1 ? "1 item" : $"{HomeItems.Count} items";
    public bool ShowHomeWelcome => !HasLibraryItems && IsHomeSection && string.IsNullOrWhiteSpace(HomeSearch);
    public bool ShowHomeEmpty => !HomeHasItems && !ShowHomeWelcome;
    /// <summary>Updated in place, so a playback checkpoint replaces only the cards that changed instead of recreating every card and thumbnail.</summary>
    public IReadOnlyList<HomeMediaItem> HomeItems => _homeItems;
    private void SyncHomeItems()
    {
        IEnumerable<HomeMediaItem> items = HomeSection switch
        {
            "Favorites" => FavoriteItems.Select(FavoriteCard),
            "Continue watching" => ResumeItems.Select(ResumeCard),
            "Recent" => RecentItems.Select(RecentCard),
            _ => ResumeItems.Select(ResumeCard).Concat(RecentItems.Select(RecentCard))
                .Concat(FavoriteItems.Select(FavoriteCard))
                .DistinctBy(x => MediaHistoryService.KeyFromCanonicalSource(x.Source))
        };
        string query = HomeSearch.Trim();
        SyncInPlace(_homeItems, items.Where(x => query.Length == 0 || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
    }
    private void SyncHomeSections()
    {
        // Off the Home route (or while searching) the sections are hidden; empty them so they hold no thumbnails.
        int limit = ShowHomeSections ? HomeSectionCardLimit : 0;
        SyncInPlace(_homeContinueItems, ResumeItems.Take(limit).Select(ResumeCard).ToArray());
        SyncInPlace(_homeRecentItems, RecentItems.Take(limit).Select(RecentCard).ToArray());
        SyncInPlace(_homeFavoriteItems, FavoriteItems.Take(limit).Select(FavoriteCard).ToArray());
        OnPropertyChanged(nameof(HasHomeContinueItems)); OnPropertyChanged(nameof(HasHomeRecentItems)); OnPropertyChanged(nameof(HasHomeFavoriteItems));
    }
    /// <summary>
    /// Makes <paramref name="target"/> equal <paramref name="next"/> with few changes: cards that stay keep their
    /// instance (and their loaded thumbnail) unless their record changed, so a checkpoint, a narrowing search or a
    /// reorder does not recreate every card.
    /// </summary>
    internal static void SyncInPlace(ObservableCollection<HomeMediaItem> target, IReadOnlyList<HomeMediaItem> next)
    {
        HashSet<string> keep = next.Select(x => x.Source).ToHashSet(StringComparer.Ordinal);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(target[i].Source)) target.RemoveAt(i);
        for (int i = 0; i < next.Count; i++)
        {
            int existing = -1;
            for (int j = i; j < target.Count; j++)
                if (string.Equals(target[j].Source, next[i].Source, StringComparison.Ordinal)) { existing = j; break; }
            if (existing < 0) { target.Insert(i, next[i]); continue; }
            if (existing != i) target.Move(existing, i);
            if (target[i] != next[i]) target[i] = next[i];
        }
        while (target.Count > next.Count) target.RemoveAt(target.Count - 1);
    }
    private bool IsFavoriteSource(string source) => _history?.IsFavorite(source) ?? false;
    private HomeMediaItem RecentCard(RecentMediaEntry entry) => new(entry.Source, entry.DisplayName, "Recently played", 0, false, PlayerCommandId.RemoveRecent, IsFavoriteSource(entry.Source));
    private static HomeMediaItem FavoriteCard(FavoriteMediaEntry entry) => new(entry.Source, entry.DisplayName, "Favorite", 0, false, PlayerCommandId.RemoveFavorite, true);
    private HomeMediaItem ResumeCard(ResumeEntry entry)
    {
        string title = RecentItems.FirstOrDefault(x => x.Source == entry.Source)?.DisplayName
            ?? FavoriteItems.FirstOrDefault(x => x.Source == entry.Source)?.DisplayName
            ?? (Uri.TryCreate(entry.Source, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? uri.Host : Path.GetFileName(entry.Source));
        return new(entry.Source, title, $"Continue from {FormatTime(entry.Position)}", entry.Duration > TimeSpan.Zero ? Math.Clamp(entry.Position / entry.Duration, 0, 1) : 0, true, PlayerCommandId.RemoveResume, IsFavoriteSource(entry.Source));
    }
    private void NotifyHome()
    {
        SyncHomeItems();
        SyncHomeSections();
        foreach (string property in new[] { nameof(HomeHasItems), nameof(HomeEmptyMessage), nameof(ShowHomeWelcome), nameof(ShowHomeEmpty), nameof(ShowHomeSections), nameof(ShowHomeGrid), nameof(HomeGridItems), nameof(HomeHistoryPrivate), nameof(IsHomeSection), nameof(IsRecentSection), nameof(IsFavoritesSection), nameof(IsContinueSection) }) OnPropertyChanged(property);
    }
}

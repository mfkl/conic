using System.Windows.Input;
using Player.Application;
using VLCAvalonia.Commands;

namespace VLCAvalonia.ViewModels;

internal sealed record HomeMediaItem(string Source, string Title, string Detail,
    double Progress, bool HasProgress, PlayerCommandId RemoveCommand);

internal sealed partial class PlayerPresentationViewModel
{
    private bool _isHomeOpen = true;
    private bool _homeListLayout;
    private string _homeSection = "Home";
    private string _homeSearch = string.Empty;
    private long _homeMediaGeneration = -1;
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
    public bool ShowHomeWelcome => !HasLibraryItems && IsHomeSection && string.IsNullOrWhiteSpace(HomeSearch);
    public bool ShowHomeEmpty => !HomeHasItems && !ShowHomeWelcome;
    public IReadOnlyList<HomeMediaItem> HomeItems
    {
        get
        {
            IEnumerable<HomeMediaItem> items = HomeSection switch
            {
                "Favorites" => FavoriteItems.Select(x => new HomeMediaItem(x.Source, x.DisplayName, "Favorite", 0, false, PlayerCommandId.RemoveFavorite)),
                "Continue watching" => ResumeItems.Select(ResumeCard),
                "Recent" => RecentItems.Select(RecentCard),
                _ => ResumeItems.Select(ResumeCard).Concat(RecentItems.Select(RecentCard))
                    .Concat(FavoriteItems.Select(x => new HomeMediaItem(x.Source, x.DisplayName, "Favorite", 0, false, PlayerCommandId.RemoveFavorite)))
                    .DistinctBy(x => MediaHistoryService.KeyFromCanonicalSource(x.Source))
            };
            string query = HomeSearch.Trim();
            return items.Where(x => query.Length == 0 || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
    private static HomeMediaItem RecentCard(RecentMediaEntry entry) => new(entry.Source, entry.DisplayName, "Recently played", 0, false, PlayerCommandId.RemoveRecent);
    private HomeMediaItem ResumeCard(ResumeEntry entry)
    {
        string title = RecentItems.FirstOrDefault(x => x.Source == entry.Source)?.DisplayName
            ?? FavoriteItems.FirstOrDefault(x => x.Source == entry.Source)?.DisplayName
            ?? (Uri.TryCreate(entry.Source, UriKind.Absolute, out Uri? uri) && !uri.IsFile ? uri.Host : Path.GetFileName(entry.Source));
        return new(entry.Source, title, $"Continue from {FormatTime(entry.Position)}", entry.Duration > TimeSpan.Zero ? Math.Clamp(entry.Position / entry.Duration, 0, 1) : 0, true, PlayerCommandId.RemoveResume);
    }
    private void NotifyHome()
    {
        foreach (string property in new[] { nameof(HomeItems), nameof(HomeHasItems), nameof(HomeEmptyMessage), nameof(ShowHomeWelcome), nameof(ShowHomeEmpty), nameof(HomeHistoryPrivate), nameof(IsHomeSection), nameof(IsRecentSection), nameof(IsFavoritesSection), nameof(IsContinueSection) }) OnPropertyChanged(property);
    }
}

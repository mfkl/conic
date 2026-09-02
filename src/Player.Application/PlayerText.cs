namespace Player.Application;

/// <summary>
/// Stable keys for new presentation text. Hosts may replace this catalog with a
/// localized resource provider without changing command identifiers or state.
/// </summary>
public static class PlayerText
{
    public const string LibraryTitle = "Library";
    public const string LibraryDescription = "Your recent items, explicit favorites, and saved positions. No folders are scanned.";
    public const string LibraryRecent = "Recent media";
    public const string LibraryFavorites = "Favorites";
    public const string LibraryResume = "Resume history";
    public const string LibraryNoRecent = "No recent media is stored.";
    public const string LibraryNoFavorites = "Favorites are only added when you choose Favorite current.";
    public const string LibraryNoResume = "No resume positions are stored.";
    public const string LibraryPrivacy = "Recent and resume history obey their privacy switches in Settings. Clear library removes recent items, favorites, and all resume positions.";
}

namespace Player.Application;

/// <summary>
/// Stable keys for new presentation text. Hosts may replace this catalog with a
/// localized resource provider without changing command identifiers or state.
/// </summary>
public static class PlayerText
{
    public const string LibraryMenu = "Media library";
    public const string LibraryButton = "Library";
    public const string LibraryButtonToolTip = "Media library (Ctrl+Shift+L)";
    public const string LibraryButtonAutomationName = "Open media library";
    public const string LibraryTitle = "Library";
    public const string LibraryDescription = "Your recent items, explicit favorites, and saved positions. No folders are scanned.";
    public const string LibraryRecent = "Recent media";
    public const string LibraryFavorites = "Favorites";
    public const string LibraryResume = "Resume history";
    public const string LibraryNoRecent = "No recent media is stored.";
    public const string LibraryNoFavorites = "Favorites are only added when you choose Favorite current.";
    public const string LibraryNoResume = "No resume positions are stored.";
    public const string LibraryPrivacy = "Recent and resume history obey their privacy switches in Settings. Clear library removes recent items, favorites, and all resume positions.";
    public const string LibraryCloseToolTip = "Close media library";
    public const string LibraryCloseAutomationName = "Close media library";
    public const string LibraryRecentTab = "Recent";
    public const string LibraryFavoritesTab = "Favorites";
    public const string LibraryResumeTab = "Resume";
    public const string LibraryRecentTabAutomationName = "Show recent library items";
    public const string LibraryFavoritesTabAutomationName = "Show favorite library items";
    public const string LibraryResumeTabAutomationName = "Show resume library items";
    public const string LibraryFavoriteCurrent = "Favorite current";
    public const string LibraryFavoriteCurrentToolTip = "Add the current media to favorites";
    public const string LibraryFavoriteCurrentAutomationName = "Add current media to favorites";
    public const string LibraryOpenRecentToolTip = "Open recent media";
    public const string LibraryOpenRecentAutomationName = "Open recent library item";
    public const string LibraryRemoveRecentAutomationName = "Remove recent library item";
    public const string LibraryOpenFavoriteToolTip = "Open favorite media";
    public const string LibraryOpenFavoriteAutomationName = "Open favorite library item";
    public const string LibraryRemoveFavoriteAutomationName = "Remove favorite library item";
    public const string LibraryOpenResumeToolTip = "Open media with saved position";
    public const string LibraryOpenResumeAutomationName = "Open resume library item";
    public const string LibraryRemoveResumeAutomationName = "Remove resume library item";
    public const string LibraryClearPrompt = "Clear all library data?";
    public const string LibraryCancel = "Cancel";
    public const string LibraryClearAll = "Clear all";
    public const string LibraryClearAllAutomationName = "Confirm clear media library";
    public const string LibraryClear = "Clear library…";
    public const string LibraryClearAutomationName = "Clear media library";
    public const string CommandAddFavorite = "Favorite current media";
    public const string CommandRemoveFavorite = "Remove favorite";
    public const string CommandClearFavorites = "Clear favorites";
    public const string CommandClearLibrary = "Clear media library";
    public const string CommandRemoveResume = "Remove saved position";
}

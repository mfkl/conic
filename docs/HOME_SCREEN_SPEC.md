# Home and navigation specification

Status: implementation authorized by the user's instruction to continue the
plan and commit validated steps (2026-09-10).

## Reference and purpose

Use local `home.PNG` and VLC master `1a853315`:
`maininterface/qml/NoMedialibHome.qml`, `SideNavigationPane.qml`, and
`medialibrary/qml/HomePage.qml`. Match the black shell, charcoal content panel,
cone/open empty state, compact navigation and orange selection. The screenshot
is visual evidence; source-only behavior is an inference.

Home is the startup route without an explicit media request. It presents the
existing recent, favorite and saved-position history. No media scanning, second
database, inferred catalog, discovery services or unavailable disc action.

## Layout and components

- Desktop: 52-DIP header, 176-DIP navigation pane, charcoal content with an
  8-DIP top-left radius, 24-DIP content spacing. Header provides return to active
  playback, cone/brand, open file, queue and More. Preferences sits at the bottom
  of the sidebar. Routes: Home, Recent, Favorites, Continue watching.
- Below 720 DIPs: sidebar becomes a 56-DIP icon rail; navigation keeps tooltips,
  automation names and selected state. Header labels reduce before actions clip.
- Empty Home: centered cone, Open file and Open folder, and a quiet drop hint.
- Populated view: section title/count, search, wrapping media cards or list rows.
  On Windows, cards load local video thumbnails in the background and cache them
  across visits. Unsupported, missing and network sources retain vector placeholders; media
  title ellipsizes, saved positions show progress, and item actions offer play,
  enqueue, favorite and remove as applicable. Local history remains the source
  of truth; search does not perform filesystem I/O.
- The existing Library management surface remains available for individual and
  section clear actions, with confirmations. A visible Manage library action
  provides access during the transition to the new Home surface.
- While playing/paused/stopped with a usable source, a bottom now-playing strip
  shows title, position, play/pause and return to Playback. It shares the existing
  commands and transport; it does not own another video surface.

## State and behavior

Opening validated media enters Playback. A cancelled picker or invalid/missing
source stays on Home and shows the existing actionable error. Back/Home changes
the route without reopening, pausing or detaching the active video surface.
Playback polling must not force the route back to Playback after the user goes
Home. A new media generation may enter Playback. Settings and overlay actions
preserve the return route and restore focus on close. Escape closes the active
overlay first; return-to-player remains a visible command when media exists.

Search has a distinct no-results state. Empty Recent/Favorites/Continue sections
explain their purpose. Privacy-disabled history explains that new recents/resume
positions are not saved. Missing paths use existing validation; persistence
errors keep useful in-memory content and surface the existing history diagnostic.
Clear operations retain the existing confirmation and privacy semantics.

Use semantic playback resources, deterministic Inter/system font fallback,
12/14/20-DIP text roles, 40-DIP action targets, visible keyboard focus and names
on icon-only buttons. No new nonessential animations; reduced motion remains
respected. Grid/list and navigation must work at 480x360 without horizontal
scrolling. All platform behavior uses Avalonia APIs; Windows is qualified first.

## Acceptance

Managed tests: initial route, search/section projections, open failure/cancel,
history/privacy changes, navigation without transport calls, and late engine
polling. UI smoke: empty/populated/search/compact snapshots, command routing,
overlay access, focus and containment. Native smoke: start real media, go Home,
return to Playback and prove the same engine, generation and advancing position;
retest compositor fullscreen/reopen where affected. Record results before commit.

PiP follows Home as a separate validated step. It must preserve this same session.

Implemented and validated on Windows: see [Home review](HOME_REVIEW_2026-09-10.md).
User visual acceptance remains pending; continued implementation and commits
are authorized by the updated working agreement.

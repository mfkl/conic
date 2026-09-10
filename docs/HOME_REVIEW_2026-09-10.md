# Home implementation — 2026-09-10

Home is implemented and validated on Windows with the pinned VLCLR/VLC 3
combination documented in the Playback review. The user authorized continuing
and committing validated steps; visual acceptance of Home is still pending.

The startup screen now has the VLC-style header, navigation pane, empty/open
state, Recent, Favorites and Continue sections, search, media cards and list
rows. The sidebar becomes an icon rail below 720 DIPs. Home uses the existing
history and queue commands; it introduces no media scanning or second database.
Manage library retains the existing clear confirmations. A now-playing strip
and return action preserve the attached video surface and current session.

`Alt+Home` returns to Home. Opening validated media enters Playback; a cancelled
picker or invalid source stays on Home. Closing overlays restores Home focus.
The existing Media information action and `Ctrl+I` remain available.

Validation on this machine:

- Release build: no warnings or errors; all 160 managed tests passed.
- `--home-smoke`: empty Home, grid/list and 480x360 containment passed.
- `--home-session-smoke`: populated search/grid/list, compact More/library
  access and focus restoration passed. Real VLC 3 playback kept the same engine
  reference and media generation with an advancing position through Home and
  back to Playback. Hardware telemetry reported matching adapters and DX11.
- Existing `--playback-controls-smoke` and `--layout-smoke` passed, including
  the 900/899 breakpoint and compact settings, media drawer and opening controls.

Local captures are under `artifacts/home-review/`: `home-wide.png`,
`home-list.png`, `home-compact.png`, `home-search-empty.png` and
`home-now-playing.png`. Smoke logs are under `artifacts/playback-validation/`.
Captures exercise an isolated `CONIC_PROFILE` directory with test history;
the default profile remains the normal local application-data directory.

Follow-up: the missing-thumbnail report is addressed in
`HOME_THUMBNAILS_REVIEW_2026-09-10.md`. Local video cards now use cached Windows
thumbnails, with vector fallbacks when unavailable.

Remaining limits: the library contains
explicit local history only, and the first list implementation uses generous
rows with inline item actions. Windows captures use this machine's display
scale; additional physical scale, macOS and Linux qualification remains open.
PiP is the next independently validated step, before Settings.

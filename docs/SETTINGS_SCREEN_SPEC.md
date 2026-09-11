# Settings screen — VLC-style shell

Continue after Home and PiP under the user's 2026-09-10 instruction to implement,
validate and commit each step. VLC preferences supply the category/behavior
inventory; this page uses the shared Avalonia Home and Playback visual language.

Settings becomes an opaque full page over the same attached video surface.
The 52-DIP black header has a back action, cone and Preferences title. Desktop
uses a 176-DIP category pane and charcoal content with 24-DIP padding. Below
720 DIPs a category selector replaces the sidebar and content padding is 16 DIPs.
Search stays above the scrollable content; back remains reachable at 480x360.
Changing category returns the content to its top. Escape/back restores the
previous Home or Playback route; playback continues underneath.

Eight categories remain:

| Category | Contents and changes |
|---|---|
| General | Resume, remembered volume, recent/resume privacy, existing clear confirmations and resume thresholds. Preserve immediate privacy clearing and history errors. |
| Appearance | Live density, control timeout and reduced motion. Explain the current dark appearance; remove the ineffective System/Dark selector from the UI while preserving stored compatibility. |
| Playback | Default speed for next open; live skip intervals. |
| Audio | Output module, normalization and passthrough, grouped under one restart explanation. Link to current audio tracks/delay. Device selection is unavailable. |
| Video | Explain unavailable video adjustments and snapshot capability in user language; omit the disabled snapshot button. |
| Subtitles | Link to subtitle tracks and timing; retain the rendering qualification limitation in plain language. |
| Hotkeys | Searchable named commands, editable gestures, Save/Reset, existing conflict validation and per-page reset. Show a no-results state. |
| Advanced | Media information/diagnostics entry point. No raw startup-argument editor or ABI jargon in the normal flow. |

Use clear headings, grouped setting rows, wrapped labels and 40-DIP controls.
Keep Live/Next open/Restart meaning near the relevant groups without repeating
implementation details. Save failures and shortcut conflicts remain inline.
No settings persistence schema changes. Reset page is shown only for editable
categories. Reset all retains explicit confirmation, with text wrapping at
compact sizes; cancellation/closing clears transient prompts.

Search filters categories and selects a match. No matches shows an explicit
empty state and hides stale category content. Blank search restores navigation.
All icon controls have names and tooltips; interactive fields have names.

Extract SettingsView as the owner of page layout and history confirmation UI.
Keep window routing in MainWindow and settings/persistence in existing services.
Managed checks cover search/no results, reset, persistence and shortcut behavior.
UI smoke captures every category at desktop and compact sizes, checks editor
containment, search, reset confirmation, focus and the return route. Recheck
Home/PiP session preservation and final Debug/Release/native regressions.

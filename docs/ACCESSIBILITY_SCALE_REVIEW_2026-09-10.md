# Accessibility and scaling checks, 2026-09-10

The timeline's inherited automation provider changed its slider value without
raising the player's seek-start/commit events. A range-value write could therefore
move the displayed thumb without seeking VLC. `PlayerTimeline` now supplies a
provider that runs the same seek lifecycle as pointer/keyboard interaction.
Invalid and disabled writes are rejected before starting a seek. This applies
to both the main player and PiP timeline.

Home card actions and queue move/remove actions now include the media title in
their automation names. This distinguishes repeated actions in the accessibility
tree. The drawer tabs now wrap when larger text exceeds the available width,
preserving their existing keyboard navigation and focus behavior.

Validation on Windows with the pinned VLCLR/VLC 3 runtime:

- Release build succeeded without warnings/errors; all 184 product tests passed.
- `--accessibility-smoke` drove the real range-value automation providers.
  Playback seek, PiP seek and volume changes were confirmed by VLC readback.
  The main seek raised exactly one start and commit; NaN and disabled writes
  were rejected. Home and queue action names include their actual media item.
- `--scale-smoke` checked equivalent logical viewports for a 1280×720 pixel
  display at 100%, 125%, 150% and 200%, with inherited text at 14 and 20 DIP.
  Captures render at corresponding bitmap DPI. Home, Playback, the drawer and
  all eight Settings pages passed horizontal-control containment checks.
  2,181 control-name observations across the matrix had nonempty automation names.
- The larger-text run initially exposed overflowing drawer tabs; the wrapping
  layout passed the rerun. Inspected larger-text drawer and 200% Hotkeys captures.
- Drawer keyboard/focus/scrolling and Playback pointer/keyboard/slider regression
  smokes passed after the changes.

Evidence: `artifacts/accessibility-native.log`, `artifacts/scale-a11y.log`,
`artifacts/accessibility-tests.log`, `artifacts/drawer-a11y.log`,
`artifacts/playback-controls-a11y.log`, and `artifacts/accessibility-review/*.png`.
Native reproduction uses `--accessibility-smoke`, the configured runtime and a
seekable local `VLC_AVALONIA_TEST_MEDIA`; use an isolated `VLC_AVALONIA_PROFILE`.

Limits: these are automation-provider, DIP-layout and scaled-raster checks.
They do not simulate Windows `WM_DPICHANGED`, physical monitor transitions,
system text-size settings, or a complete Narrator/screen-reader walkthrough.
Those manual qualifications, and macOS/Linux native checks, remain open.

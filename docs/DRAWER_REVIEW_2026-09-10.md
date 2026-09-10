# Queue and track drawers: Windows validation, 2026-09-10

This I-phase pass addresses long content and keyboard operation while retaining
the shared VLCLR/VLC 3 engine and commands.

- Queue labels now have a constrained text column, ellipsis and a full-name
  tooltip/accessibility name. The repeat/shuffle/save/clear toolbar wraps.
- Audio, subtitle and chapter lists scroll vertically. Long labels ellipsize;
  delay controls and external-track actions remain reachable by scrolling.
  Delay buttons can wrap at narrow widths. Notices occupy a separate bounded
  area instead of overlapping the drawer content.
- VLC's `IsDisabled` flag means the selectable "Disable" entry (`id == -1`).
  The UI previously disabled that button and rejected its command. Both audio
  and subtitle Disable choices now dispatch normally when capabilities are
  ready; selection still waits for native readback.
- Opening the drawer focuses its close button after layout, avoiding the race
  between the view-model notification and visibility binding. Tab cycles within
  the drawer. Left/Right/Home/End move focus and select drawer tabs. Escape
  closes the drawer and restores route focus. Queue/track rows have visible
  keyboard focus borders.

Validation:

- Release solution build/test passed; all 184 product tests passed. Added cases
  verify both Disable commands, rejection of unknown/unready choices and no
  optimistic selection before readback.
- `--drawer-smoke` passed with 24 entries per list and long media/language
  labels, at 1280×720 and 480×360. It checks viewport bounds, vertical scrolling,
  no horizontal overflow, reachable delay controls, initial focus, tab arrows,
  a full Tab cycle and Escape restoration.
- `--drawer-native-smoke` disabled and restored real audio and subtitle tracks
  through the UI commands, checking VLC readback after each operation.
- Existing layout, media-information and Stop/Play native regressions passed.
  The Stop smoke still covers Stop → Play, pause/resume and EOF → Play.
- Inspected wide queue and compact audio captures. Evidence lives in
  `artifacts/home-review/drawer-*.png`, `artifacts/drawer-tests.log`,
  `artifacts/drawer-check.log` and `artifacts/drawer-native-check.log`.

Use the pinned `VLCLR_ROOT` and `VLC_DIRECTORY`. Native smoke requires a finite
`VLC_AVALONIA_TEST_MEDIA` fixture with audio and subtitle tracks. Review fixtures
use an isolated `VLC_AVALONIA_PROFILE`. This pass does not qualify subtitle visual
rendering or physical display scaling; those limits remain documented separately.

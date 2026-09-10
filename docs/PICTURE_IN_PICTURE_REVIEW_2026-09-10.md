# Picture in picture implementation — 2026-09-10

Implemented against [the PiP specification](PICTURE_IN_PICTURE_SPEC.md), using
the pinned VLCLR/VLC 3 engine. The user authorized implementation and commits;
visual acceptance remains pending.

PiP is available from More, the video context menu, Home's now-playing strip
and `Ctrl+Shift+P`. The existing window becomes compact and always on top.
Return/Escape restores its placement, fullscreen/maximized state, decorations,
topmost preference and Home/Playback route. The video surface stays attached.
Controls include play/pause, seek, a draggable header, return and app close.
Initial keyboard focus lets Space pause playback immediately.

Windows validation:

- Release build passed. A clean dependency rebuild reports the four existing
  VLCLR CA1420 warnings; the final incremental build has no warnings/errors.
- All 162 managed tests passed, including entry/return capability gating and
  restoring the main layout before a cancelled file picker.
- `--pip-smoke` passed with 50 GPU frame notifications, unchanged engine
  reference and media generation, actual Space/pause and seek readback,
  320x180 control containment, normal placement, maximized/fullscreen and both
  topmost preferences restored. Home, Settings, URL and invalid-file flows
  restored the main layout. Closing PiP exercised normal application shutdown.
- Existing compositor fullscreen/reopen, forced-adapter CPU fallback,
  Home/session and Playback interaction/layout smokes passed.
- Native window capture confirmed visible video and controls, using the owned
  generated test pattern. `artifacts/pip-review/pip-native-window.png` is the
  actual 480x270-DIP PiP window at this machine's 150% display scale. Managed
  control captures `pip-playing.png` and `pip-minimum.png` intentionally omit
  the compositor's external GPU texture. Logs are in
  `artifacts/playback-validation/pip-*.log`.

`--pip-review` opens the configured test media and enters PiP for an interactive
review. The local `artifacts/pip-review/launch-review.ps1` uses an owned 120-second
test pattern. Smoke/review history uses `VLC_AVALONIA_PROFILE` isolation.

Limits: this is one compact application window; the main shell is temporarily
replaced. Simultaneous main and PiP windows remain deferred. macOS/Linux and
additional physical display scales have not been qualified. Detailed codec
stream information remains the separate M1 binding work in the capability matrix.

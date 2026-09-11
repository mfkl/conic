# Picture in picture — desktop v1

Added at the user's request on 2026-09-10, after Home and before Settings.
Reference: VLC master `1a853315`, `modules/gui/qt/player/qml/PIPPlayer.qml`.
Follow its compact video, central play/pause, upper return/close controls and
hover behavior. Keep the existing Avalonia timeline and keyboard seeking.

Entry points: Playback More, video context menu, Home's now-playing strip,
and `Ctrl+Shift+P`. Enable entry for usable playback. Return remains available
if the engine stops or faults while PiP is active.

The existing window becomes a resizable, border-only, always-on-top 480x270-DIP
video window, initially near the current display's bottom-right working area.
Minimum client size is 320x180. Video remains aspect-correct through the
existing attached CompositionVideoSurface; no second engine, Open call or
surface reparenting is allowed. This first version temporarily replaces the
main shell. Simultaneously open main and floating windows are deferred.

Controls use the shared white/orange palette over translucent black. A header
contains the return action, ellipsized title and close action. Drag the header
to move the window. Central play/pause and a bottom position timeline share
the existing commands. Inactivity hides controls during playback; pointer,
keyboard focus, pause and reduced-motion preferences keep them discoverable.
Icon controls have 40-DIP targets, tooltips and automation names.

Return via the upper-left button, Escape, the shortcut or double-click restores
normal placement, window state (including fullscreen/maximized), decorations,
topmost preference and previous Home/Playback route. Explicit Home, Settings,
queue/library and media-opening actions restore the full layout first. A
cancelled picker leaves that full layout available. Close exits the app through
normal lifetime shutdown because this is the single application window.

Validate registry gating and picker ordering with managed tests. Native tests
must prove compositor frames, unchanged engine reference/media generation,
pause/seek, minimum size, placement/state/topmost restoration, route changes,
and actual close shutdown. Run existing composition/fallback and Home/Playback
regressions. Capture both the UI and actual native video window; a managed
RenderTargetBitmap alone does not capture the external GPU texture. Windows
is qualified here; macOS/Linux remain unqualified.

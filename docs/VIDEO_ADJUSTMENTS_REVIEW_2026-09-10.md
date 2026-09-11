# V1: aspect ratio, crop and zoom

Implemented on `feature/modern-player-ui`, retaining VLCLR and VLC 3.0.24-beta1.
Framework prerequisite: `50d849200c3dfb2b047e20a174ae66b56a19adcd`, isolated branch
`feature/vlc3-video-adjustments`. The sibling working checkout was not switched.

## Behavior and UI specification

More → **Video adjustments** and the playback drawer's **Video** tab open a
scrollable surface with Aspect ratio, Crop, Zoom and Reset video adjustments.
It uses the existing drawer styles, wrapping tabs and focus handling. Selectors
have explicit accessibility names and native choice labels. Controls appear only
with one active video output; otherwise the drawer prompts users to play a video.
Stop and audio-only media clear the choices and hide the previous video image.

VLC 4 reference: `modules/gui/qt/menus/menus.cpp` at
`1a8533156079724db7b63dc6cb73675c76458b14` exposes exclusive Zoom, Aspect Ratio,
Crop and Fit lists. Our drawer follows those groups using VLC 3 choices, with
Fit included in Zoom to keep the compact drawer coherent.

- Aspect changes displayed proportions; Default restores source pixel aspect.
- Crop changes the centered visible source region; Default restores the frame.
- Fit keeps the picture inside the viewport. Quarter, Half, Original and Double
  use discovered VLC zoom values at physical-pixel size, corrected for display
  scale. Oversized pictures clip; the app window retains its existing dimensions.
- Changes work while playing or paused. PiP and resizing preserve the current
  input's settings. Reset restores Default aspect/crop and Fit. New/replayed
  media uses fresh output defaults. No persisted video preferences are added.

## Native and rendering implementation

The earlier missing-held-output assumption was corrected by reviewing VLC 3's
SDK, source and runtime exports. `input_Control(INPUT_GET_VOUTS = 43)` returns
held outputs in an owned array. VLCLR releases all references and the array,
discovers typed choices, and rejects old input/output identities and values
outside those choices. Mutations run on the existing VLC owner dispatcher;
dispatch is followed by fresh native readback.

The custom vout previously acknowledged these changes without applying them.
It now copies source crop/sample aspect and display zoom/autoscale through
`vlc_avalonia_get_video_geometry_v1`. The configuration fields at offsets 40,
44 and 48 follow pinned Windows x64 VLC 3 `vout_display_cfg_t`; actual callbacks
were exercised natively. CPU drawing and GPU clipping share a tested layout
calculation. GPU cropping adds no CPU readback or second playback session.
Layout updates continue while paused.

Choice collections remain stable during selection updates. This fixes a visual
readback defect found during capture where rebuilding the lists could leave the
dropdown displaying Default after a native change.

## Validation

- Release solution build and 193 product tests passed: 56 application, 8 engine
  contracts, 129 UI. Nine added cases cover pixel aspect, crop coordinates,
  physical-pixel zoom, oversize clipping, stale selection and binding updates.
- Framework Release solution build, including VideoOverlay and SubtitleRenderer,
  and all 416 existing tests passed. Four existing CA1420 framework warnings remain.
- Native AOT vout publish, deployment, cache regeneration and VLC loading passed.
- `--video-smoke` and `--video-cpu-smoke` passed: first-frame presentation,
  discovered choices, invalid/stale rejection, paused aspect/crop/zoom readback
  and display geometry, visible dropdown selection, resize, PiP, reset,
  Stop → Play and video → audio-only.
- H.264 runs on D3D11VA/opaque DX11 on Intel Iris Xe. Unavailable-adapter
  injection exercises CPU BGRA fallback; expected decoder allocation errors can
  precede CPU recovery. The check requires a presented CPU frame before proceeding.
- Drawer, playback-controls and PiP native regressions passed. Player capability
  discovery passed with deinterlace and snapshots still explicitly skipped.
- Native GPU window/desktop and CPU render captures show the changed picture.
  PrintWindow can omit unchanged overlay regions; desktop capture cross-checked
  actual GPU cropping. The drawer was also captured at 640×360 logical size with
  20-DIP text, with content scrolling inside its bounds.

Artifacts: `artifacts/video-review/` and `artifacts/video-*.log`.
User visual acceptance is pending. Qualification covers one output on Windows
x64. Rotated/360-degree media, multiple outputs, physical monitor-DPI transitions,
Narrator, macOS and Linux remain separate work. Deinterlace (V2) and snapshots
(V3) remain the next capability packages.

# Playback screen specification

Status: **implemented; VLC 3 native qualification refreshed; Gate P2 accepted 2026-09-10**

September 10 execution: the compatible baseline is restored and the Playback
fidelity pass is implemented. Debug/Release tests, native composition/lifecycle
checks and review captures are recorded in
[PLAYBACK_REVIEW_2026-09-10.md](PLAYBACK_REVIEW_2026-09-10.md). This includes the
native-caption difference and outstanding visual coverage. After the live
demonstration, the user said “ok great commit it”; this records acceptance of
the current Playback result, with the documented qualification limits preserved.

September 10 direction update: the user explicitly wants VLC 4 UI fidelity on
the existing **VLCLR / VLC 3 engine**. This supersedes the earlier request to use
VLC 4-targeting VLCLR `main`. Restore an isolated compatible framework/runtime
baseline from `eng/versions.json` for the pending native checks. The unsuccessful
main experiment is retained in [VLCLR_MAIN_MIGRATION.md](VLCLR_MAIN_MIGRATION.md)
as historical evidence. The active source map, visual foundation and delivery
order are in [VLC4_SCREEN_BY_SCREEN_PLAN.md](VLC4_SCREEN_BY_SCREEN_PLAN.md).

Playback review corrections requested on 2026-09-08 supersede the corresponding
layout/component details below:

- Align the media title vertically with the cone in the same top row.
- Anchor More below its top-right button and dismiss it on an outside click.
- Mouse focus left by clicking a control must not prevent inactivity hiding;
  preserve controls during keyboard navigation, dragging, and open menus.
- Show painted seek/volume tracks and thumbs, and display the volume percentage.
  Keep the volume slider visible in compact mode; use the compact layout below
  900 DIPs to avoid overlapping the centered transport.
- Label the left action “Audio / subtitles,” use recognizable speaker/fullscreen
  glyphs, and remove the bookmark/favorite action from the playback bar.

The targeted `--playback-controls-smoke` exercises styled controls, routed seek
and volume input, menu dismissal, inactivity, keyboard focus, and compact layout.
It uses a simulated engine snapshot and does not qualify native playback.

Specification baseline: `feature/modern-player-ui` at
`12d89183c98f57796c4cc2540cbe2f4f50bab5b7`

Product-code baseline: `6a5fc39` (included in the phase baseline
`c6e1f70e7a3c25a4041757b05d319283479d63b3`)

Primary visual reference: local-only `playback on hover.PNG`, 1920x1140,
supplied by the user on 2026-09-02. It contains a third-party media frame and
must not be committed.

Reference runtime/source: VLC `4.0.0-dev-38799-g5dd4aebdab` /
`5dd4aebdabf4a625822036bda08a24a91295158c`.

Supplemental source reference inspected September 10: local VLC `master` at
`1a8533156079724db7b63dc6cb73675c76458b14`. This does not change the screenshot
baseline or override user corrections. New token/layout adjustments from that
source must be included in the Playback comparison and result review.

## 1. Decision and product outcome

Build a focused, VLC 4-inspired playback screen around the existing VLC 3
Avalonia player contracts. Match the reference's composition and interaction
hierarchy without copying unsupported controls or Windows-only decoration:

- video remains the dominant edge-to-edge surface;
- sparse top chrome carries navigation, identity, title, queue, and overflow;
- a transient bottom gradient carries a full-width timeline and optically
  centered transport controls;
- orange is a restrained active accent, while the shell, inactive icons, and
  remaining tracks stay black, white, or neutral gray;
- controls are usable by pointer and keyboard, remain contained at every target
  size, and never cover or clip their own slider thumbs; and
- all selected actions are real. There are no decorative placeholders.

This is a screen redesign, not a playback-engine rewrite. The implementation
must preserve the current generation-safe state, queue, media-capability,
history, settings, and native composition contracts.

## 2. Scope and non-goals

### In scope

- no-media, opening, playing, paused, stopped/ended, seeking, unseekable, and
  failure presentation;
- visible and hidden playback chrome in windowed and fullscreen modes;
- media title, elapsed time, duration, play/pause, short skip, previous/next,
  seek, mute, volume, fullscreen, queue, favorite, tracks/synchronization,
  speed, stop, repeat/shuffle, media information, open, always-on-top, and
  Settings access through an appropriate primary or secondary surface;
- the queue/tracks/chapters/information drawer required by playback;
- the minimum application shell needed to host Playback;
- wide, medium, compact, fullscreen, DPI-scale, keyboard, focus, error, and
  unavailable states; and
- Windows qualification now plus a platform-neutral presentation contract for
  later macOS/Linux qualification.

### Explicit non-goals

- Home/Media Library content or navigation design;
- the Settings redesign;
- optical-disc opening;
- casting/renderers, picture-in-picture, or a mini-player;
- snapshot capture;
- aspect ratio, crop, zoom, or deinterlace controls;
- thumbnail seek previews or cover-art retrieval;
- codec-stream details;
- subtitle/subpicture composition or any new subtitle-rendering claim;
- custom-drawn Windows caption buttons;
- media scanning/indexing;
- transcoding, broadcasting, capture authoring, plugin administration, or an
  expert VLC variable tree; and
- new sibling-repository native bindings.

Unsupported items are absent. They are not shown disabled merely to resemble
VLC 4.

## 3. Evidence and interpretation

The supplied screenshot is authoritative for the shown visual state:

- dark edge-to-edge player shell;
- centered video with honest pillarboxing;
- top-left navigation/brand and a separate left-aligned media title;
- top-right queue and overflow actions;
- bottom gradient overlay;
- timeline above the command row with endpoint time labels;
- centered five-part transport group;
- secondary commands at the left and audio/display commands at the right; and
- orange progress, volume, and primary-control accents.

The screenshot's main button contains a Play glyph. This specification records
the screenshot as **paused with controls visible**, even though its filename
mentions hover. It does not copy that glyph into the playing state. Effective
engine state controls the glyph: Pause while playing, Play otherwise.

The screenshot does not prove auto-hide timing, opening/failure presentation,
keyboard behavior, compact layout, or cross-platform chrome. Those behaviors
are product decisions in this document rather than inferred VLC facts.

No icon or logo is extracted from the screenshot. A VLC cone may be used only
from an approved source asset with recorded provenance; otherwise use the
product's neutral player mark. All command icons are product-owned vector paths
or an approved redistributable icon set.

## 4. Current implementation audit

Release baseline on 2026-09-02:

- `Player.Application.Tests`: 53 passed;
- `Player.Engine.ContractTests`: 8 passed; and
- `Conic.UITests`: 86 passed.

The first parallel test attempt produced a shared compiler-output lock. Both
affected projects passed when rerun sequentially; it was not a product failure.

### What is already usable

| Area | Current contract |
|---|---|
| Engine state | `MediaEngineState` exposes generation, status, position, elapsed, duration, volume, seekability, error, mute, rate, title, and source. |
| Play/pause | Effective `PlaybackStatus` already drives `IsPlaying`; XAML already has separate play and pause paths. |
| Seek | `PlayerTimeline` brackets a protected drag; the view model previews elapsed time and commits one normalized seek. |
| Audio | Volume, mute, effective readback, and playback rate are live. The current product clamp is 0--125%; the existing `PlaylistVolumeSet/Get` binding accepts a raw float and does not impose that ceiling. |
| Queue | Immutable queue state supports previous/next, select, reorder, remove, clear, repeat, and shuffle with capability gating. |
| Media controls | Audio/subtitle tracks, titles, chapters, delays, external next-open tracks, and media information have copied generation-safe state. |
| Window | Fullscreen, always-on-top for the current window, focus-aware chrome policy, and responsive layout policy exist. |
| Opening/history | File, URL, folder, playlist, drag/drop, recent, resume, and favorite contracts exist. |
| Accessibility base | Commands are shared by buttons, menus, context menus, and editable shortcuts; several controls already expose automation names. |

### Gaps this implementation must close

| Current behavior | Required correction |
|---|---|
| A 1080-DIP floating dock card is visually separate from the media. | Replace it with an edge overlay and gradient that follows the reference hierarchy. |
| Windowed playback chrome stays visible; auto-hide is fullscreen-only. | Auto-hide while actively playing in windowed and fullscreen modes, subject to the interaction rules below. |
| Timeline and volume inherit most Fluent slider geometry. | Give each a dedicated template with explicit track/thumb geometry, hit target, clipping, and endpoint mapping. |
| The UI volume range ends at 100 and the current product/engine-state clamp ends at 125%. | Extend the product and engine-state clamps to 200%, then prove native set/readback at 150% and 200%. Mark 100% as normal and values above it as amplification. |
| Stop and speed occupy the primary transport row. | Move Stop and speed into overflow/context surfaces; keep the reference's centered five-part group. |
| Top chrome is text-heavy: Open, Queue, Library, Settings, Always on top. | Replace it with sparse icon actions and an overflow menu; defer Library content. |
| Play/pause, Stop, Seek, Volume, Mute, and Rate are not all command-state gated consistently. | Derive command availability from engine and queue state and apply it to button, menu, context, and shortcut paths. |
| Customer startup can auto-open the repository test fixture. | Remove automatic test-media opening from normal startup; keep fixtures behind explicit smoke/diagnostic entry points. |
| Error/opening states do not form a coherent player state surface. | Add the state presentation defined below without exposing engine diagnostics. |
| The Windows D3D surface is directly hosted by the window. | Keep it behind a video-surface boundary so presentation logic remains platform-neutral. |

There is no customer Playing/Paused label and no customer GPU-composition badge
to preserve. Native composition telemetry remains diagnostic-only.

## 5. Capability mapping and inclusion decisions

| Visible/product action | Current status | Playback-screen decision |
|---|---|---|
| Open local file | Working | Show as the primary no-media action and in overflow. |
| Open URL/folder/playlist | Working | Put in the Open submenu/overflow, not the primary transport. |
| Open disc | Missing | Omit. |
| Back | No screen-navigation contract yet | Show only when a navigation host reports `CanNavigateBack`; never show a fake disabled action. Screen 2 will provide the normal destination. |
| Media title/time | Working | Show title in top overlay and elapsed/duration at timeline ends. Never add a Playing/Paused label. |
| Play/pause | Working | Primary 64-DIP circular control; dynamic glyph and name from effective state. |
| Stop | Working | Retain in overflow and context menu. |
| Seek | Working when `CanSeek` | Full-width interactive timeline; disabled honestly for unseekable media. |
| Short skip | Working/configurable | Primary seek-back and seek-forward controls use configured seconds. |
| Previous/next | Working when queue neighbor exists | Keep in primary transport, disabled when no valid neighbor. |
| Volume/mute | Working to the current 125% product clamp; existing native binding has no wrapper-side ceiling | Aligned right-side group with a 0--200% track, a normal-volume marker at 100%, a visually distinct amplified segment above 100%, and effective mute glyph. |
| Playback speed | Working | Overflow/context menu plus compact popover; do not occupy primary row. |
| Fullscreen | Working | Right-side primary action and `F11`. |
| Queue | Working | Top-right action opens the playback drawer. |
| Favorite current media | Working | Map the reference bookmark-shaped action to favorite/unfavorite, with a selected state. |
| Tracks/synchronization | Working with caveats | Left secondary action opens Audio/Subtitles/Chapters. Preserve the subtitle-rendering qualification warning. |
| Repeat/shuffle | Working | Queue drawer and overflow, with observed effective state. |
| Media information | Working, codec streams unavailable | Include in drawer/overflow; retain redacted diagnostics copy. |
| Always on top | Working for current window | Overflow toggle; do not imply persistence. |
| Settings | Working existing surface | Overflow entry only; preserve access without redesigning Settings. |
| Cast/renderer | Missing | Omit. |
| Snapshot | Missing | Omit from customer Playback UI and context menu. |
| Aspect/crop/zoom/deinterlace | Missing | Omit. |
| Subtitle track selection | Working | Include in drawer, while explicitly stating rendering is unqualified. |
| Subtitle/subpicture composition | Background prerequisite | No claim and no Playback implementation work. |

No `vlclr` API change is required for the approved control set: the reviewed
volume binding already accepts and returns a float. Playback implementation must
still verify VLC 3 accepts and reads back `2.0` on the qualified runtime. If it
does not, the implementation stops and reports the native limit rather than
claiming 200% or silently displaying a value VLC rejected. Any new native API
would then be planned independently in the sibling repository.

## 6. Layout and z-order

All dimensions are Avalonia device-independent pixels (DIPs). Breakpoints use
logical client width, so 125% and 150% scaling do not change the design contract.

### 6.1 Layer stack

1. Player background, `#050607`.
2. Video surface, centered with aspect preserved; unused area remains black.
3. Top readability scrim and bottom gradient.
4. Top and bottom playback chrome.
5. transient status, seek value, tooltip, and resume/error surfaces.
6. queue/tracks/information drawer.
7. overflow/context menus.
8. platform-owned window caption controls and system dialogs.

Avalonia overlays must remain above both CPU and imported-GPU video paths. They
must not be placed inside a native child window.

### 6.2 Wide layout, at least 1000 DIPs

```text
┌ native/platform caption safe area ───────────────────────────────────────┐
│ [Back*] [Mark]                              [Queue] [More] [OS caption] │
│    Media title, one line and ellipsized                                │
│                                                                        │
│                         VIDEO SURFACE                                  │
│                                                                        │
│ ╲                         bottom gradient                            ╱ │
│  00:05                                                         01:42  │
│  ────────────────●───────────────────────────────────────────────────  │
│ [Tracks][Favorite][Sync]  [−10][Previous][ Play/Pause ][Next][+10]   │
│                                      [Mute][──●── volume][Fullscreen] │
└────────────────────────────────────────────────────────────────────────┘
```

`Back*` is absent when no prior route exists. The remaining items reflow; they
do not reserve an unexplained blank hit target.

Measurements:

- top action row: 56 high, 20 horizontal inset, 8 gap;
- media title: 18 semibold, left aligned 24 below the action row, maximum 70%
  client width, one line;
- top scrim: transparent to `#B8050607`, approximately 116 high;
- bottom gradient: transparent to at least `#F2050607`, approximately 176 high;
- time row and interactive timeline: 20 horizontal inset; the thumb center can
  reach normalized 0 and 1 while its full circle remains inside the client;
- visible track: 4 high resting, 6 high on hover/drag; minimum hit lane 36 high;
- command row: minimum 72 high and 16 bottom safe inset;
- secondary button hit targets: at least 40x40;
- primary play/pause: 64x64, white/dark fill by state, 2-DIP orange ring;
- secondary transport buttons: 44x44; and
- volume group: 40x40 icon plus a 120-wide track, sharing one vertical center.

Primary transport is centered on the window, not on remaining space between the
left and right groups. Edge groups may never push it off center.

### 6.3 Medium layout, 720--999 DIPs

- keep the full timeline and primary five-part transport;
- reduce title maximum width and volume track to 96;
- keep Tracks and Favorite visible; move Sync and low-frequency commands into
  overflow;
- use icon-only Queue and More actions;
- allow edge groups to occupy a second command sub-row if required; and
- never overlap the timeline, primary transport, or native caption safe area.

### 6.4 Compact layout, 480--719 DIPs

```text
┌──────────────────────────────────────────┐
│ [Back*]  Title…           [Queue] [More]│
│                                          │
│                VIDEO                     │
│                                          │
│ 00:05                              01:42 │
│ ─────────────●────────────────────────── │
│       [−10] [ Play/Pause ] [+10]        │
│ [More controls]       [Mute] [Fullscreen]│
└──────────────────────────────────────────┘
```

- hide the brand mark before hiding the media title;
- move Previous/Next into the queue surface when width cannot contain them;
- collapse the volume track into a keyboard-accessible popover opened from Mute/
  Volume; the popover still uses the aligned 0--200% slider;
- move Tracks, Favorite, Sync, speed, Stop, and always-on-top to More controls;
- use a bottom sheet, maximum 72% client height, for Queue/Tracks/Information;
- preserve 40-DIP targets and visible focus; and
- the minimum supported client is 480x360. Below it, normal window constraints
  apply rather than allowing controls to clip.

### 6.5 Window chrome

Caption buttons remain platform-owned. The app may extend the dark header into
the title-bar region only through Avalonia's platform abstraction and must
respect Windows caption insets, macOS traffic lights, Linux decoration policy,
and draggable/non-draggable regions. It must never paint fake Windows minimize,
maximize, or close glyphs on every platform.

Fullscreen removes system decoration. The player overlays then use the display
safe area and the same logical-width breakpoints.

## 7. Component specification

### 7.1 Top overlay

- **Back:** conditional `CanNavigateBack`; automation name `Back`; Escape does
  not invoke it directly.
- **Product mark:** decorative unless it has an approved command; not focusable
  when decorative.
- **Title:** effective media title with filename fallback; no raw URL user info,
  status suffix, or file path. Tooltip exposes the full sanitized title.
- **Queue:** opens Queue tab; selected/open state uses orange plus shape/fill,
  not color alone.
- **More:** opens the overflow menu and restores focus when it closes.

### 7.2 Timeline

- values are normalized 0--1 internally and formatted from engine duration;
- pointer press anywhere in the lane starts a protected seek and updates the
  preview value; release commits exactly once;
- capture loss commits the latest valid value once, not twice;
- endpoints map to 0 and 1 without clipping the thumb or requiring pointer
  coordinates outside the window;
- while dragging, engine refresh cannot pull the thumb away;
- a small text-only preview above the thumb shows target time; there is no image
  thumbnail;
- Home/End seek to start/end; Left/Right adjust by five seconds when the timeline
  owns focus; configured short-skip commands remain `Alt+Left/Right` globally;
- when seek is unavailable, the track and times remain readable, the thumb is
  absent or stationary, input is disabled, and help text says `Seeking is not
  available for this media`; and
- elapsed/duration use tabular numerals and expand to `h:mm:ss` only when needed.

### 7.3 Primary transport

Order on wide/medium:

1. configured seek backward;
2. previous queue item;
3. play/pause;
4. next queue item; and
5. configured seek forward.

The play/pause glyph, tooltip, automation name, and command are all derived from
the same effective state. While playing they say/show Pause; while paused or
stopped with a playable item they say/show Play. During Opening, Closed without
media, or Faulted without a playable item, the action is disabled.

Previous and Next remain visible but disabled in wide/medium layouts when a
neighbor is unavailable. Disabled icons meet contrast requirements and expose
the reason through help text. Compact may move both to Queue.

Stop is available from More and the context menu while Opening, Playing, or
Paused. It is not a sixth primary button.

### 7.4 Left secondary actions

- **Tracks:** opens Audio/Subtitles/Chapters in the existing playback drawer.
- **Favorite:** adds/removes the current source from explicit favorites. Selected
  state fills the bookmark shape and uses an automation name such as `Remove
  from favorites`.
- **Synchronization/adjustments:** opens audio/subtitle delay controls and media
  information only. It must not imply crop, zoom, deinterlace, or color controls.

Actions without a current source or relevant capability are omitted or disabled
according to layout stability. No empty icon invokes an unavailable feature.

### 7.5 Audio and fullscreen group

- mute icon, slider track, and thumb share one optical vertical center;
- the mute button is 40x40 and the track is hosted in its own fixed-height lane,
  not aligned by the Fluent Slider's outer bounds;
- orange fills the effective volume amount through 100%; a subtle tick marks
  normal volume and the 101--200% amplified segment uses a lighter warm-orange
  fill so amplification is visible without adding a warning badge;
- the 16-DIP thumb is always fully visible, including 0 and 200;
- pointer drag updates volume live; arrow keys change 2 percentage points;
  PageUp/PageDown change 10; Home sets 0; End sets 200; crossing 100 has a small
  pointer detent but remains continuous and fully keyboard accessible;
- M toggles mute without destroying the remembered effective volume;
- muting changes the glyph and automation name but leaves the track's stored
  position visible;
- a value tooltip shows `Volume N%` through 100 and
  `Volume N% · Amplified` above 100 during focus, hover, or drag; and
- fullscreen follows the same optical row, shows enter/exit glyphs from effective
  window state, and uses `F11`.

### 7.6 Overflow and context menu

Wide overflow order:

1. Open file;
2. Open URL, folder, or playlist submenu;
3. Stop, when available;
4. Playback speed submenu with effective value;
5. Repeat and shuffle with effective state;
6. Media information;
7. Always on top;
8. Settings; and
9. Exit fullscreen when applicable.

The right-click context menu uses the same command registry and state; it is not
a separate behavior implementation. Snapshot and unsupported video controls are
removed from customer menus.

### 7.7 Playback drawer

- one mutually exclusive surface hosts Queue, Audio, Subtitles, Chapters, and
  Information;
- wide/medium: right drawer, 400 target width and no more than 42% client width;
- compact: bottom sheet, no more than 72% client height;
- opening the drawer pins player chrome visible;
- Escape closes the current nested popover first, then the drawer, and restores
  focus to its launcher;
- Queue preserves loading, ready, empty, partial-failure, repeat, shuffle,
  reorder, remove, clear, select, and save behavior;
- Subtitles visibly states that track selection is available while presentation
  remains unqualified on the custom video path; and
- Information keeps URI/credentials redaction and the codec-details limitation.

## 8. State model

| State | Video/background | Top chrome | Bottom chrome | Primary action | Status/error surface |
|---|---|---|---|---|---|
| No media / Closed | Black player background and approved neutral mark | Persistent, Open available | Persistent minimal controls; transport disabled | Disabled | Centered `Open media` action and drag/drop hint; no Open Disc |
| Opening | Black or last safe frame | Persistent | Persistent, seek disabled | Disabled; Stop in overflow | Small indeterminate progress with sanitized title; no fake percentage |
| Playing, active input | Current video | Revealed on input, then hidden | Revealed on input, then hidden | Pause | No status label |
| Playing, controls visible | Current video plus scrims/gradient | Visible | Visible | Pause | Tooltips/previews only |
| Paused | Current frame | Persistent | Persistent | Play | No `Paused` text |
| Seeking | Current frame | Pinned | Pinned | Effective play/pause state | Target time preview above thumb |
| Unseekable | Current video/frame | Normal visibility policy | Timeline readable but disabled | Effective play/pause state | Seeking help only on focus/tooltip |
| Stopped/ended with playable queue item | Last safe frame or black | Persistent | Persistent | Play | Optional short `Playback ended` announcement for assistive tech, not a badge |
| Faulted | Black or last safe frame under dark scrim | Persistent | Persistent safe actions | Play only if retry is valid | Inline sanitized error with Retry/Open another file; details remain diagnostics-only |
| Non-Windows/unavailable video surface | Black fallback | Persistent | Controls reflect actual transport availability | Capability-gated | Honest `Video playback is not available on this platform build` message; app remains operable |

The view model must reject stale generation updates exactly as it does today.
Opening a replacement source must not flash title, time, icon, or queue state from
the previous generation.

## 9. Chrome visibility and motion

- chrome is always visible for No media, Opening, Paused, Stopped, Faulted,
  reduced-motion, or any state without a confirmed Playing status;
- while Playing, chrome auto-hides after the configured 1--15 second timeout in
  both windowed and fullscreen modes;
- pointer movement, pointer press, Tab, any recognized player shortcut, window
  activation, or a playback-state change wakes chrome;
- chrome cannot hide while pointer is over it, keyboard focus is within it, a
  menu/drawer/dialog is open, a seek/volume drag is active, or a tooltip/popup
  requires its anchor;
- hiding chrome never moves keyboard focus to an invisible element. If focus is
  already in chrome, it remains visible;
- while fullscreen chrome is hidden, the pointer cursor may hide after the same
  timeout and returns on input; windowed mode keeps the normal pointer;
- standard motion: 140 ms opacity fade plus at most 6 DIP vertical translation;
  no spring or scale animation on transport; and
- reduced motion: no translation or scale. Chrome remains visible, matching the
  existing setting contract.

## 10. Pointer, keyboard, and focus behavior

### Pointer

- one click/tap on bare video reveals chrome; it does not toggle playback;
- double-click on bare video toggles fullscreen;
- right-click opens the playback context menu;
- drag/drop retains the validated local-file behavior; multiple files use the
  existing queue contract;
- timeline and volume interactions capture the pointer and pin chrome; and
- no global wheel-volume behavior is added. Wheel changes volume only when the
  volume control owns pointer/focus according to Avalonia control conventions.

### Keyboard

Existing defaults remain unless the later Settings spec changes them:

| Gesture | Action |
|---|---|
| `Space` | Play/pause when focus is not on a control that owns Space |
| `Alt+Left` / `Alt+Right` | Configured short skip |
| `M` | Mute/unmute |
| `F11` | Enter/exit fullscreen |
| `Ctrl+J` | Queue |
| `Ctrl+O` | Open file |
| `Ctrl+L` | Open URL |
| `Ctrl+I` | Media information |
| `Ctrl+,` | Settings |

On macOS, displayed and dispatched primary-menu modifiers use Command through a
platform gesture mapper. The engine-neutral command registry does not hard-code
Windows display strings into controls.

Escape priority:

1. close nested menu/popover;
2. close playback drawer;
3. dismiss non-destructive resume/error prompt as specified by that prompt;
4. exit fullscreen; and
5. otherwise leave playback unchanged.

Focus order begins with visible top actions, then timeline, left secondary
actions, primary transport, audio/fullscreen actions, and the open drawer. When
responsive layout moves an item into overflow, it appears once in the focus
order, never twice.

## 11. Visual tokens

Playback remains dark regardless of light Settings theme because it is a media
view. System high-contrast mode may replace these tokens.

| Token | Value / rule |
|---|---|
| Player background | `#050607` |
| Strong overlay | `#F2050607` maximum; use gradients, not one opaque card |
| Primary foreground | `#F5F6F8` |
| Secondary foreground | `#AEB3BD` |
| Disabled foreground | at least 3:1 against the immediate surface |
| Accent | `#FF8A3D` |
| Accent hover | `#FFA465` |
| Accent pressed | `#E8752C` |
| Remaining track | `#4DFFFFFF` resting; stronger on hover |
| Focus ring | 2 DIP orange plus a contrasting inner/outer edge where needed |
| Error | accessible warm red on a dark neutral surface; never orange-only |
| Typography | Inter when bundled, then platform sans-serif fallback |
| Title | 18/24 semibold, one line |
| Time/value | 12/16 with tabular numerals |
| Corners | circular transport; 10--14 for menus/drawer items; no large floating dock radius |
| Elevation | gradients and restrained menu/drawer shadows only |

Blue is not used for playback selection, filled tracks, focus, or active icons.
Orange is limited to active progress, volume fill, selected actions, the primary
button ring, and focus. Large orange surfaces are reserved for the no-media Open
action.

## 12. Accessibility

- every interactive target is at least 40x40 DIPs;
- visible focus is required for keyboard operation and cannot rely on color
  alone;
- Play/Pause, Mute/Unmute, Favorite/Unfavorite, Fullscreen/Exit fullscreen, Queue
  open state, repeat, and shuffle expose dynamic names from effective state;
- timeline exposes minimum, maximum, current elapsed, duration, and seekability;
- volume exposes 0--200%, the 100% normal-volume point, amplified state above
  100%, effective mute state, and keyboard help;
- loading, open failure, and platform-unavailable messages use a polite live
  region; time ticks and ordinary state refreshes are not announced repeatedly;
- title and menu text meet WCAG AA contrast; disabled controls remain legible;
- tooltips supplement but never replace an automation name;
- the drawer traps neither focus nor Escape and restores focus to its launcher;
- at 200% text scaling, controls may reflow to the next responsive tier rather
  than truncate essential command names in menus;
- animations honor reduced motion; and
- the video frame is decorative unless meaningful media description becomes
  available through a later product contract.

## 13. Cross-platform architecture contract

Presentation and interaction logic must compile without Windows-native types:

- state mapping, command availability, chrome visibility, responsive policy,
  formatted values, and drawer state remain in `Player.Application` or a
  platform-neutral view model;
- Avalonia XAML and styles do not reference HWND, D3D11, COM, registry paths,
  drive letters, or Windows caption geometry;
- file opening continues through `StorageProvider` abstractions;
- window caption insets, modifier labels, fullscreen, cursor hiding, and
  always-on-top use injected/Avalonia platform services;
- the existing Windows `CompositionVideoSurface` remains one implementation of a
  video-surface host. New screen logic consumes its engine-neutral state and
  cannot reach into D3D telemetry;
- a non-Windows or unavailable-surface implementation must let the application
  launch and show the honest fallback state rather than crash during XAML load;
- filenames, URIs, and tests make no case-insensitive or drive-letter assumption;
  and
- fonts/icons have deterministic fallbacks and no screenshot-ripped assets.

Windows is the only physically qualified playback platform for Gate P2. macOS
and Linux remain unqualified until real runners, VLC runtimes, packaging, and a
native video-surface choice are available. Later checks include build/test,
launch, file picker, menu/shortcut mapping, scaling, window/fullscreen behavior,
fallback presentation, reopen, and shutdown.

## 14. Implementation boundaries after approval

The approved implementation may:

- extract a dedicated Playback view/control from the monolithic window;
- add platform-neutral playback presentation/chrome/layout policies;
- replace the current timeline and volume templates;
- reorganize existing commands into the specified top, bottom, drawer, overflow,
  and context surfaces;
- add a conditional navigation-shell interface without building Home content;
- remove normal-startup test-media auto-open behavior;
- add focused application, headless UI, layout, accessibility, and Windows smoke
  coverage; and
- preserve access to the existing Settings surface until Screen 3.

It may not:

- redesign Home/Library or Settings;
- add a new native control by calling VLC privately from the app;
- change sibling `vlclr`;
- claim macOS/Linux playback, subtitle composition, casting, snapshot, or video
  adjustments;
- push, merge, rebase, or amend; or
- start another screen before Gate P2 approval.

After Gate P1, one fresh `gpt-5.6-terra` implementation subagent receives this
exact committed specification. The orchestrator reviews and tests its diff,
launches the Release application, and presents Playback for Gate P2.

## 15. Acceptance scenarios

### Managed and headless tests

1. Effective Playing state exposes Pause glyph/name; Paused and playable Stopped
   expose Play; Closed/Opening/Faulted without a playable item disable it.
2. Seek begins only when seekable, ignores engine thumb updates while dragging,
   previews target time, and commits exactly once on release or capture loss.
3. Timeline endpoints commit exactly 0 and 1 while the full thumb remains inside
   the window at 480, 720, 1000, 1280, and 1920 logical widths.
4. Volume accepts 0, 100, 150, and 200, clamps values outside 0--200, distinguishes
   the amplified segment above 100, keeps its thumb visible, and keeps icon/track
   optical centers within 1 DIP.
5. Muting changes glyph/name without overwriting the stored volume.
6. Previous/Next enablement follows current queue neighbors and repeat mode.
7. Unsupported Cast, Snapshot, Open Disc, crop/zoom, and GPU status controls are
   absent from the visual tree and menus.
8. Playing chrome hides after timeout in windowed and fullscreen modes; it cannot
   hide during pointer hover, focus, seek/volume drag, menu, drawer, prompt, or
   reduced motion.
9. Paused, Opening, Faulted, No media, and platform-unavailable states keep chrome
   visible.
10. responsive layout contains every visible control at wide, medium, and 480-DIP
    compact widths at 100%, 125%, and 150% scale.
11. Primary transport remains centered independently of unequal edge groups.
12. compact overflow contains each moved action once and restores focus.
13. Escape follows the defined close order and exits fullscreen only after nested
    surfaces close.
14. shortcut dispatch remains suppressed for sliders, editors, combo boxes,
    buttons, menus, and controls that own arrow/Space input.
15. stale engine, queue, media-capability, and information generations cannot
    replace current title, time, icon, or drawer state.
16. all dynamic actions expose correct automation names and a visible focus ring.
17. a non-Windows/unavailable surface can construct the screen and show the
    fallback state without loading a Windows native library in headless tests.
18. normal application startup does not auto-open the repository test fixture.

### Windows Release runtime checks

1. Open the approved local fixture through the customer picker.
2. Verify video is centered with correct pillarbox/letterbox behavior on hardware
   opaque, software upload, and CPU fallback paths.
3. While Playing, confirm Pause is visible; click it and confirm playback pauses
   and the glyph changes to Play; click again and confirm resume/Pause.
4. Click, drag, and keyboard-seek to start, middle, and end. Confirm one commit,
   accurate position, no clipped thumb, and no controls hidden under chrome.
5. Exercise configured backward/forward skip and valid/invalid queue neighbors.
6. Set volume to 0, 50, 100, 150, and 200; mute/unmute; confirm aligned geometry,
   visible thumb, effective native readback, amplified styling, and audio
   behavior. Specifically verify `PlaylistVolumeSet(2.0)` succeeds and a fresh
   `PlaylistVolumeGet` reports 2.0 within the existing smoke tolerance.
7. Open/close Queue, Tracks, Chapters, and Information; verify mutual exclusion,
   focus restore, loading/empty/error states, and subtitle-rendering caveat.
8. Test windowed and fullscreen auto-hide/wake, pointer cursor behavior,
   double-click fullscreen, F11, menus, focus pinning, and reduced motion.
9. Test 1920x1080, 1280x720, 900-DIP boundary, 720-DIP boundary, and 480-DIP
   compact containment with Windows display scaling at 100%, 125%, and 150% where
   available.
10. Open replacement media, stop/reopen, trigger a controlled open failure, and
    close the app; confirm generation safety and clean shutdown.
11. Run existing engine, queue, media-capability, player-capability, composition,
    forced-adapter, and lifecycle smokes.
12. Produce product-owned captures for no-media, opening, playing hidden, playing
    visible, paused, seeking, queue open, failure, fullscreen, and compact states.

### Gate P2 presentation

Present to the user:

- the running Release Playback screen;
- a VLC-reference/Avalonia contact sheet using only distributable or sanitized
  product-owned frames;
- wide, compact, paused, playing, fullscreen, and drawer states;
- exact build/test/smoke commands and results;
- known differences from the supplied VLC 4 screenshot; and
- macOS/Linux items still unqualified.

Gate P2 repeats after every visible correction until the user explicitly accepts
Playback.

## 16. Gate P1 approval bundle

Approval of this specification accepts these deliberate design decisions:

1. use the VLC 4 edge-overlay hierarchy, but keep caption buttons platform-owned;
2. auto-hide controls while Playing in both windowed and fullscreen modes;
3. extend volume to an aligned 0--200% control, with 100% marked as normal and
   the range above 100% presented as amplification;
4. map the bookmark-shaped action to the existing explicit Favorite capability;
5. keep Stop and speed in overflow rather than the centered transport row;
6. show Back only when a later navigation host has a real previous route;
7. omit Open Disc, Cast, Snapshot, crop/zoom, and other unsupported actions;
8. retain subtitle track selection with an honest rendering caveat and no
   subtitle-composition claim; and
9. defer all Home/Library and Settings visual work.

**Gate P1:** implementation may begin only after the user explicitly approves
this document or approves it with listed corrections.

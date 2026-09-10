# VLC 4 UI fidelity plan for Avalonia / VLCLR / VLC 3

Status: active UI roadmap, updated 2026-09-10. Playback fidelity changes and
fresh VLC 3 native qualification are complete for this pass; the user accepted
Playback on 2026-09-10 after the live review. See
[the review report](PLAYBACK_REVIEW_2026-09-10.md). Home/Library is implemented
and validated, along with PiP and Settings; Windows integration qualification
follows. The engine target is **VLCLR + VLC 3**;
the earlier proposed move to VLC 4-targeting VLCLR main is superseded.

Product baseline: `feature/modern-player-ui` at
`c6e1f70e7a3c25a4041757b05d319283479d63b3`

Reference source: local VLC `master` at
`1a8533156079724db7b63dc6cb73675c76458b14` (2026-09-07), inspected on
2026-09-10 through `git show master:<path>` in `../vlc`. Local `origin/master`
resolves to the same commit; remote freshness was not checked. The checkout
has unrelated work on `fix/win32-stat-utc-mtime` and was not switched or edited.

Reference executable: local read-only VLC `4.0.0-dev-38799-g5dd4aebdab`
bundle is temporarily available. Runtime capture is supporting evidence, not a
prerequisite when user-supplied screenshots already cover the state being
specified.

This plan controls phase-two dispatch and supersedes the batch-oriented package
order in Sections 15--17 of `UI_IMPLEMENTATION_PLAN.md`. That document remains
the product boundary and technical inventory.

## 0. Updated direction and executable roadmap

### Product decision

Make the application immediately recognizable as the VLC 4 desktop UI, with
close matching of composition, proportions, typography, icons, colors, control
placement, and interaction states. Avalonia renders the presentation; VLCLR and
the VLC 3 internal core continue to provide playback. UI fidelity does not
require adopting Qt, VLC 4 native APIs, or VLC 4's indexed media library.

The latest user instruction explicitly preserves VLC 3. Use the compatible
framework pin in `eng/versions.json` (`e90b786de74c6ed440dea8bf65329907739b0c60`,
`feature/modern-player-ui-transport`) as the recovery baseline, with VLC
`3.0.24-beta1` / module ABI `3_0_0f`. Verify that complete combination before
claiming it runs today. The sibling VLCLR checkout currently targets different
work; select an isolated compatible checkout through `VLCLR_ROOT` when execution
resumes. The failed VLC 4-main experiment remains historical evidence in
[VLCLR_MAIN_MIGRATION.md](VLCLR_MAIN_MIGRATION.md), not a UI prerequisite.

Evidence precedence: explicit user corrections, user-supplied screenshots for
the states they show, a matching reference runtime, then pinned source inference.
The older executable below does not match the newly inspected master; record
that difference instead of claiming runtime confirmation. Source dimensions are
logical values subject to VLC's scale; screenshot pixels are not automatically
Avalonia DIPs. Keep the September 8 playback corrections, including the title
beside the cone and compact controls below 900 DIPs.

This roadmap covers the whole product. The per-screen gates below govern
implementation and detailed screen specifications; they do not block maintaining
the roadmap. The original planning update did not imply visual acceptance;
Playback was subsequently accepted after the user reviewed the running result.

### Current gap assessment

Source audit: `MainWindow.axaml`, `MainWindow.axaml.cs`,
`Styles/PlayerChrome.axaml`, `ViewModels/PlayerPresentationViewModel.cs`, and
the existing playback specification. This is a code/screenshot-reference audit,
not a new runtime visual pass.

| Area | Existing foundation | Work needed for the requested result |
|---|---|---|
| Playback | Edge-to-edge video, cone/title header, centered transport, custom timeline/volume, auto-hide, fullscreen, More and track controls | Finish September 8 corrections in the running app; compare proportions, glyphs, gradients, endpoint painting and focus/hover states against the reference |
| Visual system | Shared `PlayerChrome.axaml`, but many literal colors, radii and inline icon paths in the window | Introduce semantic VLC-derived resources and a consistent icon set; replace inconsistent warm-orange borders, blue-tinted grays and oversized panel rounding where the reference differs |
| Home | A 430-DIP Library drawer with recent/favorite/resume data | Build the actual VLC-like landing page and navigation shell; preserve existing data and no-scan behavior |
| Queue and tracks | Existing queue/media drawer and generation-safe commands | Match VLC row density, current-item indication, toolbar and selection treatments; make tracks/speed/delay controls a coherent playback surface |
| Settings | Centered panel, existing categories, search and persistence | Build a polished page using the same shell/resources, with clear category hierarchy, spacing, validation and compact navigation |
| Window and navigation | Most screens and overlay coordination live in `MainWindow` | Extract screen views as they change; keep one playback session/video host alive through Home, Settings, queue and fullscreen transitions |

### VLC 4 source map

Paths below are relative to `../vlc/modules/gui/qt` at the reference commit.
They are inspection targets for layout and behavior, not dependencies to link
into the app. Some files were inventoried only; detailed behavior must be read
when the corresponding screen is specified.

| Presentation concern | Upstream reference | Avalonia destination / deliverable |
|---|---|---|
| Spacing, text sizes, radii, motion, colors | `style/VLCStyle.qml`, `style/systempalette.hpp`, `style/systempalette.cpp`, `style/colorcontext.cpp` | Shared semantic brushes, dimensions, typography and interaction styles under `Styles/` |
| Shell, navigation, empty Home | `maininterface/qml/MainDisplay.qml`, `MainViewLoader.qml`, `SideNavigationPane.qml`, `SideNavigationDelegate.qml`, `NoMedialibHome.qml` in the same directory | Shell and Home views, active navigation, cone/open state, compact navigation |
| Populated media surfaces | `medialibrary/qml/HomeDisplay.qml`, `HomePage.qml`, `VideoGridItem.qml`, `widgets/qml/GridItem.qml`, `MediaCover.qml` | Recent/continue/favorite sections and reusable cards/rows over existing history |
| Playback composition | `player/qml/Player.qml`, `TopBar.qml`, `ControlBar.qml`, `PlayerControlLayout.qml`, `SliderBar.qml` | Playback view, top/bottom overlays, timeline and balanced transport groups |
| Volume and visibility | `player/qml/controlbarcontrols/VolumeWidget.qml`, `player/qml/PlayerToolbarVisibilityFSM.qml`, `PlayerPlaylistVisibilityFSM.qml` | Existing slider/chrome controllers with equivalent observable behavior |
| Queue and playback menus | `playlist/qml/PlaylistPane.qml`, `PlaylistDelegate.qml`, `PlaylistToolbar.qml`, `player/qml/TracksMenu.qml`, `PlaybackSpeed.qml` | Queue rows/toolbar, track selection, speed/delay menus and focus restoration |
| Icons and placeholders | `pixmaps/VLCIcons.ttf`, `pixmaps/toolbar/`, `pixmaps/placeholder/`, `widgets/qml/IconToolButton.qml` | Audited vector assets or consistent Avalonia geometries with source/license attribution |
| Preference semantics | `dialogs/preferences/simple_preferences.cpp`, `preferences.cpp` | Capability-backed settings inventory; visual treatment follows our VLC-like shell |

Read reference files through the pinned Git object while the sibling checkout is
in use. Refresh master only if a specific missing UI change justifies it, then
record the new hash and affected reference states. Do not move the visual target
mid-screen without recording the difference.

### Visual foundation to specify and apply

These are source-backed starting values, not retroactive approval of new pixels:

| Token / pattern | Source observation | Planned treatment |
|---|---|---|
| Accent | `orange500 = #FF8800` in `systempalette.hpp` | Default VLC orange for progress, selected navigation and primary actions; preserve accessible foreground contrast |
| Surfaces | `darkGrey300 = #212121`; neutral gray family `#1E1E1E` to `#303030` | Near-black shell/video and charcoal content; assign separate semantic tokens to surfaces, borders, hover and selection |
| Spacing | `VLCStyle` uses 2, 4, 8, 12, 16, 24, 32, 36 before scaling | Reuse a small shared spacing scale; calibrate actual placement against screenshots |
| Typography/icons | Normal text 12, large 14, xlarge 16, xxlarge 20, xxxlarge 24; normal icon 24 before scaling | Define roles and deterministic font fallbacks; preserve optical glyph alignment and use larger accessible hit areas where needed |
| Navigation | Expanded pane 160 and navigation button height 35 before scaling | Match the screenshot's sidebar/content proportions after scale calibration; collapse to a usable compact route selector |
| Shape/motion | Button radius 4, grid cover radius 4; short/long durations 100/200 ms | Small, consistent rounding and restrained fades; retain reduced-motion behavior |
| Playback groups | `PlayerControlLayout` defines left/center/right groups; normal group gap 36 and control gap 16 before scaling | Keep primary transport centered on the window, with collision-free edge groups and explicit compact layout |
| Volume | `VolumeWidget` derives its range from `MainCtx.maxVolume`, displays percentage, and marks 100% when amplification is possible | Keep the requested 0–200% product range and visible percentage; verify native readback. Do not infer a fixed upstream maximum |

Create shared styles for icon buttons, primary actions, menu rows, navigation
items, sliders, cards, empty/error states and focus rings. Use VLC vector/icon
sources where suitable, preserving applicable attribution; otherwise use
consistent vector geometries. No bitmap mockups or generated logos are needed.
Audit ordinary Avalonia control templates too, so combo boxes, scrollbars,
menus and dialogs do not fall back to a visibly different design language.

### Delivery order and concrete exit criteria

| Step | Scope and deliverables | Exit evidence |
|---|---|---|
| F0 — Re-establish the VLC 3 baseline | Select isolated pinned VLCLR/runtime inputs; build and launch the current app; capture current Playback and record capability status | Reproducible dependency paths, build results, native smoke result and before captures; engine stays VLC 3 |
| P — Finish Playback fidelity | Apply shared resources needed by Playback; audit existing September 8 fixes; refine header, cone, glyphs, gradients, transport, seek/volume, More, queue and tracks; cover audio-only, loading and error states | Playback state contact sheet, interaction tests, CPU/GPU overlay checks and Gate P2 review |
| H — Implement VLC-like Home and shell | After P2, write the detailed Home spec. Build sidebar, header, charcoal content panel, centered empty cone/open actions, then recent/continue/favorite cards/rows; add compact navigation and a persistent mini-player when media is active | H1 spec review, then empty/populated/compact captures and Home → Playback → Home flow without reopening or losing playback; H2 result review |
| S — Unify Settings | After H2, specify category navigation/search and shared page styling; use existing settings/hotkey services; cover validation/reset/reopen/restart semantics | S1 spec review, then every category at desktop/compact sizes with keyboard and persistence evidence; S2 result review |
| I — Final visual and functional qualification | Review all menus, dialogs, focus/hover/disabled states, density, DPI, long titles and navigation transitions; complete native regression and document intentional differences | Complete Windows walkthrough/contact sheets, native results and difference ledger; I1 review; macOS/Linux remain separate qualification work |

Implementation should extract `PlaybackView`, `HomeView`, `SettingsView` and
reusable navigation/queue controls incrementally as their screens change.
`MainWindow` owns the shell and video-host lifetime; engine-neutral view models
own presentation state. Reuse the command registry, queue snapshots, history and
settings services. Keep the native transport and D3D11/CPU surface boundaries.
Avoid turning a visual redesign into a parallel engine or data-store rewrite.

Home is the startup destination when no media is requested; opening media takes
the user to Playback. Returning Home keeps active media playing, with a compact
now-playing strip for title, play/pause, progress and return-to-player. Specify
focus restoration, Settings return route, Back, stop, queue and fullscreen
transitions before implementing H. No second player instance is created.

For artwork, use already available artwork/local cache through an asynchronous
presentation service, with bounded decoding and cancellation. Provide matching
VLC-style placeholders when artwork is missing. A thumbnail extraction engine
is not required to finish Home; scanning disks is still excluded.

### Fidelity boundaries and intentional differences

| VLC 4 reference feature | Product decision |
|---|---|
| Empty Home cone and primary actions | Match hierarchy and spacing. Use Open file plus a supported Open folder/network action in place of Open Disc until disc opening is explicitly supported |
| Browse / Discover navigation | Match navigation styling. Include only destinations with real content/actions; discovery services and network-device browsing are not supplied by the current contracts |
| Indexed Video/Music/Artists/Albums library | Reuse the visual card/list patterns for explicit history/favorites/resume; do not imply indexing, fabricate metadata or add empty catalog destinations |
| Bookmark and title placement in supplied Playback image | Keep the user's later corrections: no bookmark action in the bar, title aligned with cone |
| Casting, snapshot, aspect/crop/zoom, deinterlace | Omit unavailable actions; add only after corresponding VLCLR/VLC 3 capabilities and native checks exist |
| Subtitle menu | Preserve track/delay controls but keep visual subtitle rendering unqualified until subpicture composition passes both native and visual tests |
| Qt preferences and platform window decorations | Settings uses the coherent modern shell. Keep platform window behavior/accessibility; any title-bar visual differences are reviewed explicitly |

### Visual acceptance standard

Passing builds proves functionality, not that the application looks like VLC 4.
For each screen, produce a same-state comparison at matched client dimensions,
scale and theme. Include reference, Avalonia result and annotated differences;
mask differing video/artwork content when evaluating chrome. Do not compare
unmatched executable versions as if they were the same state.

- Cover 1920x1080 and 1280x720 client sizes, the original 1920x1140 reference
  where useful, and compact 480x360; test 100%, 125%, 150% scale. Record physical
  versus simulated scale separately. Playback also checks the 900-DIP breakpoint.
- Playback: paused/playing with controls, hidden controls, fullscreen, queue,
  audio/subtitles, More, seek at both ends, volume 0/100/200, long title,
  audio-only, opening/buffering, error and unavailable capabilities.
- Home: empty, populated with/without artwork, selected/hover/focus, missing
  media, privacy-disabled history, compact sidebar and active mini-player.
- Settings: every category, search results/no results, editing/conflict/error,
  reset confirmation, scroll containment and compact navigation.
- Review composition/proportions first, then typography/icons, then colors and
  surfaces, then state/motion polish. No clipped thumbs/text, overlapping control
  groups, inconsistent glyph weights, lost focus, or dead visible actions.
- Record each intentional difference with its reason and acceptance status.
  A numeric screenshot score alone cannot accept a screen; font rasterization
  and native decorations may differ, while misplaced controls still need fixing.

Keep raw references under ignored artifacts and commit derived specifications
and product-owned comparison evidence as appropriate. Run affected managed tests
per screen and the relevant existing `--playback-controls-smoke`, layout,
queue/media-capabilities and composition/lifecycle modes. Final qualification
includes Debug/Release, Native AOT, hardware opaque, software upload, CPU and
forced-adapter fallback. Do not rerun native suites for this documentation edit.

## 1. Working agreement

2026-09-10 execution update: the user requested “add it and keep going on the
plan” and clarified “commit as you go”. This authorizes writing the remaining
specifications, implementing and validating each step, and committing it without
another permission stop between steps. The original review gates below remain
review milestones, not mandatory pauses under this newer instruction. Do not
record unobserved visual acceptance. No push or merge is authorized.

Add **PiP after Home and before Settings**. Reference
`player/qml/PIPPlayer.qml` at VLC master `1a853315` for compact controls and return
behavior. The first desktop PiP implementation temporarily turns the existing
player window into a resizable, always-on-top video window (480x270 initially,
320x180 minimum), with play/pause, return and close actions. Save and restore
normal bounds, route, window state and topmost preference. Escape returns to the
main layout. The main shell is temporarily replaced, rather than keeping two
windows open; this avoids detaching the native surface or creating a second
player. Multiple simultaneous video windows remain a separate follow-up.

PiP acceptance: actual video remains visible; pause/seek/position and engine
generation survive enter/exit; resizing, fullscreen-to-PiP, Escape/return,
topmost restoration and normal app shutdown work. Media opening, Home and
Settings commands restore the full layout first. Use Avalonia window APIs and
keep Windows qualification distinct from macOS/Linux. Record captures, run
affected tests and native composition checks, then commit the step.

The next Home specification is [HOME_SCREEN_SPEC.md](HOME_SCREEN_SPEC.md).

Work one screen at a time in this order:

1. Playback screen.
2. Home and media-library screen.
3. Settings screen.
4. Cross-screen integration and platform qualification.

Every screen has two mandatory user approval gates:

```text
Reference capture -> complete written spec -> USER SPEC APPROVAL
                                             |
                                             v
                                  one-screen implementation
                                             |
                                             v
                           live app + captures + test report
                                             |
                                             v
                                  USER RESULT APPROVAL
                                             |
                                             v
                                      next screen
```

The agent must stop at each gate. Silence, a passing test suite, or an internal
review is not approval. The next screen is not specified or implemented until
the user explicitly accepts the current result.

Feedback is handled immediately on the current screen through focused correction
commits and a new live review. Do not accumulate feedback for a final redesign.

Only one fresh `gpt-5.6-terra` implementation subagent works in the shared
checkout at a time. It receives exactly one approved screen specification. The
orchestrator reviews its diff and tests, launches the result, and presents it to
the user. No agent pushes, merges, rebases, amends, changes branches, or starts
the next screen.

## 2. What a complete screen specification contains

Implementation cannot start from a mood board or a few screenshots. Each screen
specification must include:

1. **Purpose and non-goals** — what the screen enables and what it deliberately
   omits.
2. **Reference evidence** — VLC 4 executable version, captures, exact Qt/QML
   source paths, and any source/runtime mismatch.
3. **Layout specification** — hierarchy, alignment, measurements, minimum and
   maximum sizes, spacing, safe areas, and z-order.
4. **Component inventory** — every visible control, icon, menu, drawer, dialog,
   empty state, loading state, error state, and unavailable state.
5. **State model** — initial, opening, playing/active, paused/inactive, disabled,
   focused, hovered, pressed, selected, stale, and failure states as applicable.
6. **Behavior** — commands, pointer/touchpad behavior, keyboard shortcuts,
   Escape/back behavior, focus entry/restoration, confirmations, and timing.
7. **Capability mapping** — the existing VLC 3 product contract behind each
   action, plus explicit omissions for unavailable bindings.
8. **Responsive behavior** — desktop, compact, fullscreen, 100%, 125%, and 150%
   scale behavior, including what collapses, moves, or becomes an overflow item.
9. **Cross-platform behavior** — platform-neutral UI logic, platform service
   boundaries, native-surface assumptions, and deferred macOS/Linux checks.
10. **Accessibility** — automation name/help, keyboard order, visible focus,
    contrast, reduced motion, and screen-reader announcements where needed.
11. **Visual tokens** — colors, typography, radii, elevation, icon source, and
    animation values with provenance.
12. **Acceptance scenarios** — exact interactions, screenshots, headless tests,
    runtime smokes, and known limitations the user will review.
13. **Open decisions** — a short list of choices that genuinely require user
    preference before implementation.

The spec is committed separately. The orchestrator then presents a concise
visual/behavior summary and waits for explicit user approval.

## 3. Incremental reference capture

Do not capture the entire VLC 4 application up front. Start with user-supplied
screenshots, then use the executable or matching Qt/QML source only to answer a
specific unresolved behavior or state question for the screen currently being
specified. This keeps the feedback loop short and prevents unused parity work.

### Current local reference packet

The user supplied these captures at the repository root on 2026-09-02:

| Local-only file | Size | What it establishes | Active use |
|---|---:|---|---|
| `home.PNG` | 1920x1140 | VLC 4 dark Home shell, left navigation, empty/open state, header actions, and orange selection language | Queued for Screen 2; do not expand the Home spec during Playback |
| `playback on hover.PNG` | 1920x1140 | Wide playback canvas with controls revealed, top overlay, title placement, full-width timeline, bottom command groups, and orange progress/volume accents | Primary visual baseline for Screen 1 |

The captures remain untracked local evidence. In particular, the playback image
contains a third-party media frame and must not be committed. Commit derived
measurements, diagrams, requirements, and product-owned comparison captures
instead.

For the states shown, these user-supplied images are the visual truth. The
executable is used selectively for behavior and missing states. Source inspection
is an inference unless it is confirmed by a capture or an interaction test.

For every capture session:

- use an isolated temporary VLC profile with no media discovery or scanning when
  the executable is needed;
- record executable version/build, closest source revision, OS, scale, window
  client size, theme, state, actions, and sanitized fixture identity;
- store raw captures and manifests under ignored
  `artifacts/vlc4-reference/<revision>/<screen>/`;
- inspect matching QML, tokens, and assets in the read-only VLC source checkout;
- commit derived measurements, state maps, and approved asset provenance only;
- never commit personal paths, history, credentials, or copyrighted media
  frames; and
- treat a stable executable interaction as behavior truth and source-only
  conclusions as inferences when revisions differ.

Do not block a screen spec on exhaustive VLC capture. If a missing reference
state would materially change the Avalonia result, request one targeted capture
or present the assumption at the screen's spec gate. Otherwise specify the state
from the product contract and validate the Avalonia implementation directly.

The VLC 4 settings window is captured only to inventory useful behavior and
categories. It is not a visual target.

## 4. Cross-platform contract

The presentation must be cross-platform even though the current native playback
qualification and interactive review happen on Windows.

Required throughout all three screens:

- view models, commands, settings, history, layout policy, and state machines
  contain no Windows, D3D11, COM, HWND, registry, drive-letter, or shell-specific
  assumptions;
- XAML uses Avalonia controls/resources and does not depend on Win32-only visual
  behavior;
- file pickers, clipboard, window state, notifications, paths, and platform
  integration stay behind injected interfaces;
- the existing Windows D3D11 surface remains behind the video-surface/transport
  boundary and cannot leak into screen presentation logic;
- controls remain usable with a non-D3D surface or an unavailable playback
  capability state;
- case sensitivity and URI/path handling are tested without assuming Windows;
- fonts and icons have deterministic cross-platform fallbacks;
- shortcuts account for macOS Command conventions through a platform mapping,
  not hard-coded UI text; and
- headless tests exercise the UI logic without loading VLC or Windows native
  libraries.

Windows acceptance proves the current product. macOS and Linux remain
**unqualified** until their actual runners and VLC/native dependencies are
available. Later qualification must include:

| Platform | Deferred physical validation |
|---|---|
| macOS | Build/test, app launch, file picker, clipboard, menu/shortcut mapping, window/fullscreen behavior, scaling, video surface or honest fallback, reopen, and shutdown |
| Linux | Build/test on selected desktop/session, file picker, clipboard, window/fullscreen behavior, scaling, video surface or honest fallback, reopen, and shutdown |

Code review and Windows headless tests may establish portability readiness, but
must never be reported as a macOS/Linux pass.

## 5. Screen 1 — Playback

Gate P2 was accepted on 2026-09-10. Preserve the accepted Playback baseline while
the Home/Library specification is prepared; targeted corrections remain possible.

### PLAYBACK-SPEC — Full playback screen specification

Specification: [PLAYBACK_SCREEN_SPEC.md](PLAYBACK_SCREEN_SPEC.md)

Reference scope:

- empty/no-media player;
- opening/buffering and failure;
- video playing and paused;
- controls visible, hovered, focused, dragged, and auto-hidden;
- fullscreen enter, active controls, hidden controls, wake, and exit;
- seek start/preview/commit at both endpoints;
- volume, mute, speed, previous/next, play/pause, stop, queue entry, title,
  elapsed/duration, open action, and primary overflow/context menu; and
- 1920x1080, 1280x720, and 480px compact layouts at available scales.

Screenshot-derived baseline from `playback on hover.PNG`:

- the player is a near-black, edge-to-edge shell with the video centered at its
  native aspect ratio and honest pillarbox/letterbox space;
- the visible-controls state uses a transient top overlay for back navigation,
  brand, media title, overflow, and queue/playlist entry points;
- the bottom overlay uses a dark gradient over the media rather than a permanent
  opaque control panel;
- the seek track spans the window above the command row, with elapsed and
  duration labels anchored near its two ends;
- primary transport stays optically centered: seek-back, previous when
  available, prominent play/pause, next when available, and seek-forward;
- secondary actions are split into left and right edge groups so they do not
  displace the primary transport. The spec must map each observed icon to a real
  VLC 3 capability before including it;
- orange is restrained to progress, volume fill, selection, or the primary
  control outline; inactive icons and tracks remain neutral;
- volume is a single aligned icon/track group at the right, followed by casting,
  display-mode, or fullscreen actions only when supported; and
- pointer hover, keyboard focus, transport input, or seek/volume interaction
  reveals the overlay; inactivity hides it without hiding focus or interrupting
  an active drag.

The captured primary transport glyph is `Play`, so this image is recorded as
**controls visible with an effectively paused transport state**. It is not used
to infer the playing glyph. The approved product contract still requires `Pause`
whenever playback is effectively playing.

Reference gaps to resolve inside `PLAYBACK-SPEC`, without starting another
screen:

1. Define opening, buffering, failure, active-playing, auto-hidden, and fullscreen
   states from the product contract and current Avalonia capabilities.
2. Mark any visual behavior not proven by the screenshot or runtime as an
   explicit assumption.
3. Ask the user for another VLC 4 screenshot only if that assumption would cause
   a materially different layout or interaction.
4. Produce product-owned Avalonia reference frames for every state before Gate
   P2; VLC 4 media-frame screenshots are not required in the repository.

The spec decides which VLC 4 shell/navigation elements are necessary around the
player, but it must not design the Home/Library or Settings content yet.

Preserved product decisions:

- orange is used deliberately for elapsed progress or one active control accent;
- the play icon reflects effective playback and changes to pause;
- seeking is interactive and commits accurately;
- seek and volume thumbs remain fully visible;
- the sound icon and volume track share one optical center;
- there is no redundant Playing/Paused status label;
- there is no customer GPU-composition badge; and
- unavailable video controls are absent or honestly explained.

Spec commit: `docs: specify VLC 4-inspired playback screen`

Gate P1 — User approves the playback specification before code changes.

### PLAYBACK-IMPL — Implement only the approved playback screen

Allowed scope:

- playback view, minimum shell/chrome required to host it, transport components,
  player menus/overlays, playback-specific resources, application bindings, and
  their tests/smokes;
- no Home/Library redesign; and
- no Settings redesign beyond preserving access to the existing screen.

Implementation validation:

- Debug/Release managed build and affected tests;
- headless pointer/keyboard tests for playback controls and focus;
- seek endpoints, play/pause state, volume alignment, compact containment, and
  fullscreen control smoke;
- engine lifecycle/reopen/clean shutdown;
- hardware opaque, software upload, CPU fallback, and forced-adapter fallback;
- reference/Avalonia contact sheets for every approved playback state; and
- cross-platform boundary audit with macOS/Linux still explicitly unqualified.

Primary commit: `feat: add VLC 4-inspired playback screen`

Gate P2 — Launch the Release app for the user. Present the implemented playback
screen, captures, commands, test results, and known differences. Apply feedback
and repeat this gate until the user explicitly approves it.

Approval commit: `docs: record playback screen approval`

## 6. Screen 2 — Home and media library

This screen begins only after Gate P2 is approved.

### HOME-SPEC — Full Home/Library specification

Reference and product scope:

- startup landing state and navigation from/to playback;
- recent media, explicit favorites, resume history, and no-history state;
- list/grid presentation, selected/hover/focus states, item metadata, overflow
  actions, open/play/enqueue, individual remove, and clear confirmation;
- queue/playlist entry points where they clarify the home experience;
- loading, unavailable, missing-file, storage-error, and privacy-disabled states;
- desktop and compact navigation/layout; and
- explicit no-scan behavior. VLC 4 media indexing is not copied.

The spec must decide the smallest useful Home surface. It may combine Home and
Library if separate destinations do not improve this product.

Spec commit: `docs: specify modern Home and Library screen`

Gate H1 — User approves the Home/Library specification.

### HOME-IMPL — Implement only the approved Home/Library screen

Allowed scope:

- Home/Library navigation and content, existing history/favorites/resume data,
  required reusable cards/lists/empty states, and focused tests;
- playback changes only when required to navigate cleanly between screens; and
- no Settings redesign.

Implementation validation:

- history v2 migration, malformed/read-only persistence, privacy, no-scan, clear,
  missing-item, and generation tests;
- desktop/compact layout and keyboard/focus tests;
- navigation to playback and open/enqueue behavior;
- product/reference contact sheets for approved states; and
- full affected managed and native regression checks.

Primary commit: `feat: add modern Home and media library screen`

Gate H2 — Launch and present the Home/Library screen for user review. Apply
feedback and repeat until explicitly approved.

Approval commit: `docs: record Home and Library screen approval`

## 7. Screen 3 — Settings

This screen begins only after Gate H2 is approved.

### SETTINGS-SPEC — Original modern settings specification

VLC 4 preferences provide a behavior/category inventory only. The visual design
is original and must fit the approved Playback and Home/Library screens.

Required design direction:

- searchable categories with a two-pane desktop layout and compact category
  navigation on narrow windows;
- clear section cards with concise titles and summaries;
- switches for binary choices, sliders/numeric controls for bounded values, and
  choices only where users benefit from seeing alternatives;
- clear `Live`, `Next open`, and `Restart` semantics without repetitive badges;
- inline validation and conflict resolution rather than delayed generic errors;
- progressive disclosure for Advanced options;
- General, Appearance, Playback, Audio, Video, Subtitles, Hotkeys, and Advanced
  coverage based on real product capabilities;
- history privacy/clear controls, per-section reset, global reset confirmation,
  media diagnostics, and shortcut editing;
- unavailable controls omitted unless the explanation helps the user understand
  a visible product limitation; and
- desktop, compact, keyboard, screen-reader, contrast, and reduced-motion states.

The spec includes wireframes/captures of every category and identifies which
existing setting controls remain, move, combine, or disappear.

Spec commit: `docs: specify modern Avalonia settings screen`

Gate S1 — User approves the Settings specification and visual direction.

### SETTINGS-IMPL — Implement only the approved Settings screen

Allowed scope:

- Settings screen/components/resources, existing settings and hotkey bindings,
  required platform service abstractions, and focused tests;
- no unrelated Playback or Home/Library redesign.

Implementation validation:

- persistence/migration/corruption/reset tests;
- typed VLC startup argument tests and explicit restart/reopen behavior;
- shortcut normalization, conflict, editor-focus, and persistence tests;
- desktop/compact category, search, focus, confirmation, and accessibility tests;
- product-owned contact sheets for every category; and
- full managed/native regression qualification.

Primary commit: `feat: add modern Avalonia settings screen`

Gate S2 — Launch and present the Settings screen category by category. Apply
feedback and repeat until explicitly approved.

Approval commit: `docs: record Settings screen approval`

## 8. Cross-screen integration and deferred platform qualification

This phase begins only after P2, H2, and S2 are approved. It does not redesign
the accepted screens.

Tasks:

1. Resolve navigation, shared-resource, focus, shortcut, and responsive issues
   that occur only when the three approved screens are integrated.
2. Produce final screen-to-screen flows and Windows qualification evidence.
3. Run Debug/Release builds/tests, Native AOT publish, and every applicable
   layout, lifecycle, queue, capability, hardware, upload, CPU, and forced
   fallback smoke.
4. Audit platform-neutral assemblies and isolate remaining Windows dependencies.
5. Create separate macOS and Linux qualification packages when actual runners,
   VLC runtimes, and native surface choices are available.

No visual change to an approved screen is allowed without showing it to the user
again.

Primary commit: `feat: integrate approved VLC 4-inspired screens`

Gate I1 — User reviews the complete Windows flow and exact deferred macOS/Linux
qualification list before any merge or push.

## 9. Branch and commit policy

After the Playback specification is approved, create
`feature/vlc4-ui-parity` from baseline
`c6e1f70e7a3c25a4041757b05d319283479d63b3`. Record a changed base explicitly if
the prior branch is merged first.

Each screen produces a readable sequence:

```text
docs: specify <screen>
feat: implement <screen>
fix: address <screen> review feedback       # zero or more
docs: record <screen> approval
```

Specifications may be committed on the planning branch before the implementation
branch exists. Implementation and review corrections remain on the phase-two
branch. No push or merge happens without separate user direction.

## 10. Approval ledger

Record implementation and validation evidence as it becomes available. Record
user approval only after the named user gate; implementation is not evidence of
result approval.

| Screen | Reference evidence | Spec commit | Spec approval | Implementation tip | Windows validation | User result approval | macOS | Linux |
|---|---|---|---|---|---|---|---|---|
| Playback | local hover/paused capture, master source map, refreshed state captures and native window capture | `9913358`, volume correction `24e53f9`; current `PLAYBACK_SCREEN_SPEC.md` includes September 8 review corrections | implementation already proceeded; original approval date not recorded | `a088ead` plus September 10 fidelity changes on `feature/modern-player-ui` | Debug/Release 156 tests each; native matrix passed with documented skips; see `PLAYBACK_REVIEW_2026-09-10.md` | Gate P2 accepted 2026-09-10: “ok great commit it” after live review | unqualified | unqualified |
| Home / Library | local Home capture and VLC master source | `2812915`, `HOME_SCREEN_SPEC.md` | continued execution authorized 2026-09-10 | Home shell, shared history and mini-player | Release 160 tests; Home native/session and player/layout smokes passed; `HOME_REVIEW_2026-09-10.md` | pending visual review | unqualified | unqualified |
| Picture in picture | VLC master `PIPPlayer.qml` | `PICTURE_IN_PICTURE_SPEC.md` | continued execution authorized 2026-09-10 | compact same-window mode | Release 162 tests; native PiP, compositor/fallback and Home/Playback regressions passed; `PICTURE_IN_PICTURE_REVIEW_2026-09-10.md` | pending visual review | unqualified | unqualified |
| Settings | modern shared shell, VLC category inventory | `fc39deb`, `SETTINGS_SCREEN_SPEC.md` | continued execution authorized 2026-09-10 | extracted full-page SettingsView | Release 165 tests; all-category compact/wide smoke and Home/PiP/Playback regressions passed; `SETTINGS_REVIEW_2026-09-10.md` | pending visual review | unqualified | unqualified |
| Integration | shared routes and native session | n/a | continued execution authorized 2026-09-10 | screen transitions implemented | final Windows matrix next | pending review | deferred | deferred |

## 11. Immediate next step

F0 and the Playback fidelity pass are complete. Following the live demonstration,
the user said “ok great commit it” on 2026-09-10; record this as Gate P2 result
acceptance and authorization to commit the work.

Home is now implemented and validated; see `HOME_REVIEW_2026-09-10.md`.
PiP is implemented and validated; see `PICTURE_IN_PICTURE_REVIEW_2026-09-10.md`.
Settings is implemented and validated; see `SETTINGS_REVIEW_2026-09-10.md`.
Next is the final Windows integration matrix and combined review. Continue and
commit validated steps under the updated
working agreement; record visual acceptance separately when the user reviews.

Media information remains part of Playback: More → Media information, the drawer's
Info tab, and `Ctrl+I` already expose metadata and diagnostics copy. Preserve those
entry points during Home integration. Detailed codec/stream information is the
remaining M1 capability work in `PLAYER_CAPABILITY_MATRIX.md`; it needs reviewed
VLCLR/VLC 3 bindings and is not claimed by the current metadata surface.

The completed F0 procedure selected the existing
VLC 3-compatible VLCLR pin in an isolated checkout, set `VLCLR_ROOT` and the
matching `VLC_DIRECTORY`, and built/launched the current Playback screen. The
report records actual dependency paths and revisions. Do not switch unrelated sibling
working trees or migrate the product to VLC 4-targeting main.

The actual implementation branch is `feature/modern-player-ui`; the proposed
`feature/vlc4-ui-parity` branch was not created. Continue on the existing branch.
The user's follow-up “go” authorized execution after the planning update; their
later acceptance after the demonstration completed Gate P2. Home/Library now
follows, then Settings and cross-screen integration.

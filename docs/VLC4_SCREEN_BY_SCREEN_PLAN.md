# VLC 4-inspired UI screen-by-screen plan

Status: active phase-two execution plan; planning only

Product baseline: `feature/modern-player-ui` at
`c6e1f70e7a3c25a4041757b05d319283479d63b3`

Reference source: VLC `4.0.0-dev` at
`5dd4aebdabf4a625822036bda08a24a91295158c`

Reference executable: local read-only VLC `4.0.0-dev-38799-g5dd4aebdab`
bundle is temporarily available. Runtime capture is supporting evidence, not a
prerequisite when user-supplied screenshots already cover the state being
specified.

This plan controls phase-two dispatch and supersedes the batch-oriented package
order in Sections 15--17 of `UI_IMPLEMENTATION_PLAN.md`. That document remains
the product boundary and technical inventory.

## 1. Working agreement

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

This is the first and only active screen until the user accepts it.

### PLAYBACK-SPEC — Full playback screen specification

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

Update only after the named user gate:

| Screen | Reference evidence | Spec commit | Spec approval | Implementation tip | Windows validation | User result approval | macOS | Linux |
|---|---|---|---|---|---|---|---|---|
| Playback | local hover/paused capture available | pending | pending | pending | pending | pending | unqualified | unqualified |
| Home / Library | local Home capture queued | pending | blocked by Playback | pending | pending | pending | unqualified | unqualified |
| Settings | deferred | pending | blocked by Home / Library | pending | pending | pending | unqualified | unqualified |
| Integration | n/a | n/a | blocked by all screens | pending | pending | pending | deferred | deferred |

## 11. Immediate next step

Use `playback on hover.PNG` as the initial visual reference, audit the current
Avalonia playback capabilities, and write `PLAYBACK-SPEC`. Resolve unshown states
through explicit product decisions and request only targeted additional evidence
when a decision materially depends on VLC 4. Present the complete specification
to the user and stop at Gate P1. Do not implement the Playback screen yet.

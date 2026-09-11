# VLC Avalonia UI implementation plan

Status: UI-00 through UI-08 are complete and qualified. Phase two uses the
screen-by-screen specification and user-approval gates in
[VLC4_SCREEN_BY_SCREEN_PLAN.md](VLC4_SCREEN_BY_SCREEN_PLAN.md). Sections 12--18
remain its product boundary and technical inventory. Physical
multi-monitor/multi-GPU and OS suspend/resume remain explicitly unqualified.
Planning baseline: 2026-09-01
Primary scope: modern Windows desktop UI with daily-use VLC playback parity
Background track: GPU presentation of VLC-rendered subpictures on the opaque
D3D11 path

2026-09-10 direction: the active roadmap targets close VLC 4 **visual and
interaction fidelity in Avalonia**, retaining **VLCLR / VLC 3** for playback.
Section 0 of [VLC4_SCREEN_BY_SCREEN_PLAN.md](VLC4_SCREEN_BY_SCREEN_PLAN.md)
records the current source audit, visual tokens, screen gaps, delivery order
and comparison criteria. Earlier UI-00–08 qualification is historical; it does
not imply the phase-two screens are visually accepted or the currently selected
sibling framework checkout is compatible. A VLC 4 engine migration is outside
this UI roadmap.

## 1. Outcome

Turn the current video-first proof of concept into a polished desktop player
that covers the VLC features people use during normal playback without copying
VLC's dense menus and preference tree.

The finished product should:

1. Keep video visually dominant and hide nonessential chrome during playback.
2. Make common actions available directly and advanced actions discoverable
   through contextual drawers, menus, and searchable settings.
3. Support keyboard, pointer, touchpad, high-DPI, and accessibility workflows.
4. Persist user preferences safely and state clearly when a setting requires a
   media reopen or application restart.
5. Keep native VLC pointers and APIs behind engine-neutral contracts.
6. Preserve the qualified D3D11 hardware and CPU fallback paths.

"Near parity" means parity for local and network media playback. Transcoding,
broadcasting, capture-device authoring, plugin administration, and every
expert VLC variable are separate product tracks and are not prerequisites for
this plan.

## 2. Experience model

### Main player

- Top bar: Open split button, media title, queue button, settings button, and
  window actions.
- Bottom dock: previous, play/pause, next, timeline, mute/volume, playback
  speed, and fullscreen.
- Right drawer: queue, chapters, audio tracks, and subtitle tracks.
- Context menu: common playback, audio, subtitle, and video actions.
- Fullscreen: controls fade after an adjustable delay and return on pointer
  movement, keyboard input, or focus changes.
- Narrow windows: secondary actions collapse into an overflow menu without
  clipping the timeline or volume thumb.

### Modern interaction principles

- Use progressive disclosure instead of reproducing VLC's full menu bar.
- Use one command source for buttons, menus, context menus, and hotkeys.
- Never show a control as functional when its engine capability is absent.
- Preserve focus, visible keyboard navigation, and screen-reader names.
- Prefer reversible actions and provide reset-to-default for preferences.
- Use orange only as a deliberate active-state accent over the dark/white
  visual system.

## 3. Settings information architecture

The first complete settings experience contains:

| Page | Settings |
|---|---|
| General | Resume playback, remember recent media, single-instance behavior, always on top, confirmation behavior |
| Appearance | System/dark theme, accent, compact/comfortable density, playback-control timeout |
| Playback | Autoplay, default speed, short/long skip intervals, remember position, repeat behavior |
| Audio | Remember volume, initial volume, output device, normalization, passthrough when supported |
| Video | Hardware decoding mode, fit mode, default aspect ratio, deinterlacing |
| Subtitles | Enabled by default, preferred language, encoding, font size, outline, background |
| Hotkeys | Searchable command list, conflict detection, editable bindings, reset |
| Advanced | Network/file caching and reviewed VLC startup options, diagnostics, reset all settings |

Every setting is classified as one of:

- `Live`: applies immediately.
- `NextOpen`: applies when media is next opened or reopened.
- `Restart`: applies after restarting the application.

Persist settings as a versioned JSON document using atomic replacement. Keep
defaults in code, migrate older schemas explicitly, tolerate an invalid file
by preserving it for diagnosis, and expose a reset operation. Recent media and
resume history must have independent privacy toggles and clear-history actions.

## 4. Architecture boundaries

Before expanding the UI, introduce these boundaries:

- A central command registry with execution, `CanExecute`, display name,
  category, and default shortcut metadata.
- Immutable view-facing snapshots for transport, queue, tracks, chapters,
  media metadata, and engine capabilities.
- Focused engine-neutral interfaces for transport, playlist, tracks, audio,
  video, and media opening. Do not grow one unbounded `IMediaEngine` surface.
- Application services for settings, recent media, resume positions, file/URL
  opening, and window state.
- Reusable Avalonia controls and views under clear `Controls`, `Views`,
  `ViewModels`, `Commands`, `Services`, and `Styles` areas as needed.
- Dispatcher marshalling at the engine/application boundary, not scattered
  throughout visual controls.

Views must not call VLC APIs, hold native pointers, or infer capabilities from
platform details. Startup-only settings are translated to reviewed VLC options
by the engine adapter.

## 5. Mandatory GPT-5.6 Terra dispatch rules

Implementation of every work package in Section 7 must be performed by exactly
one fresh GPT-5.6 Terra (`gpt-5.6-terra`) subagent. The orchestrating agent
coordinates, reviews, validates, and integrates; it must not silently
implement a package itself or substitute another model. Package agents may not
spawn nested implementation agents.

If a Terra subagent is unavailable, stop before implementation and report the
blocker to the user. Planning, read-only inspection, and final integration
validation may be performed by the orchestrator.

For every package:

1. Record the Terra agent/task identifier in the completion table.
2. Verify that the selected model is `gpt-5.6-terra` before allowing edits.
3. Give the subagent exactly one package, including prerequisites, allowed
   files, acceptance criteria, validation, and commit message.
4. Require it to read repository instructions and inspect current status and
   relevant diffs before editing.
5. Preserve unrelated user changes. Never reset, clean, stash, or broadly
   reformat the shared worktree.
6. Dispatch sequentially in one shared worktree. Parallel work is allowed only
   in isolated Git worktrees with disjoint package ownership.
7. Require focused tests during implementation and all package validation
   before a commit.
8. Stage explicit paths only. Do not push, rebase, amend, or change branches.
9. Return the commit hash, commands and results, remaining status, risks, and
   screenshots or interaction notes when the package changes visuals.

Only one implementation subagent may be active in the shared checkout. Builds
share `bin`/`obj`, and solution/project files are common integration points, so
apparently disjoint packages are still not safe to dispatch concurrently.
Before each dispatch, record the clean starting commit. Before the next
dispatch, the orchestrator verifies the returned commit, diff, tests, and clean
status.

Native or cross-repository gaps discovered by a UI package must become a
separate prerequisite package. Do not mix speculative VLCLR changes into a UI
commit.

### Branch and commit policy

Before UI-00 starts, the orchestrator must:

1. Verify that `main` contains the approved playback UI commit and has no
   working-tree changes other than this new plan file.
2. Create and switch once to the integration branch
   `feature/modern-player-ui`.
3. Commit this plan on that branch as
   `docs: add modern player UI implementation plan`.
4. Record that plan commit as the implementation baseline.

All UI-00 through UI-08 Terra subagents work sequentially on that integration
branch. Subagents must not create, switch, merge, or delete branches. Each work
package produces exactly one focused commit using the message specified by the
package, after its full validation passes. The next package receives the exact
preceding commit hash as a prerequisite.

If a missing capability requires sibling `vlclr` work, dispatch it as a
separate Terra prerequisite with its own branch and commit in that repository,
then record and pin that hash before resuming the product integration branch.
Do not combine framework and product changes in one logical commit.

No package pushes or merges. After UI-08, the orchestrator runs final
qualification and presents the complete commit series and branch status to the
user. Merging `feature/modern-player-ui` into `main` and pushing either branch
require separate user direction. Prefer a fast-forward merge when `main` has
not diverged; otherwise review the divergence before selecting a merge or
rebase strategy.

## 6. Validation conventions

Managed baseline:

```powershell
dotnet build vlc-avalonia.slnx --no-restore
dotnet build vlc-avalonia.slnx -c Release --no-restore
dotnet test vlc-avalonia.slnx --no-build --no-restore
git diff --check
```

Any package that changes engine behavior must also run the applicable bounded
smokes from `docs/D3D11_COMPOSITION.md`, including engine lifecycle and UI
composition when relevant. It must not infer native behavior from a build.

Visual packages must verify at minimum:

- 1280x720, 1920x1080, and a narrow window layout;
- 100%, 125%, and 150% display scaling where the environment permits;
- keyboard-only navigation and visible focus;
- pointer seeking at the timeline ends;
- fullscreen control appearance and dismissal;
- dark and system theme variants when both exist.

Introduce Avalonia headless interaction tests and deterministic screenshot
coverage early. Native video composition remains covered by the existing smoke
modes rather than by headless tests.

## 7. Work packages

### UI-00 — Capability matrix and product contract

Repository: `vlc-avalonia`
Prerequisite: current `main`

Tasks:

1. Create a feature matrix covering current support, required product binding,
   required VLCLR binding, startup/reopen constraints, and validation method.
2. Define daily-use parity, advanced parity, and explicit non-goals.
3. Inventory commands and default shortcuts before controls are added.
4. Record which subtitle behavior works on CPU BGRA and which requires the
   background D3D11 subpicture track.

Acceptance:

- Every control planned below maps to a real or explicitly missing capability.
- No placeholder control is described as working.
- Cross-repository prerequisites are identified before UI implementation.

Allowed files: documentation only.

Commit: `docs: define modern player UI capability matrix`

### UI-01 — Presentation foundation and test harness

Repository: `vlc-avalonia`
Prerequisite: UI-00

Tasks:

1. Extract design tokens, icons, player chrome, timeline, and volume control
   from `MainWindow` into reusable resources and controls.
2. Establish view-model and application-service boundaries without moving VLC
   implementation details into presentation code.
3. Add a central command registry and bind the existing controls through it.
4. Add Avalonia headless interaction tests and deterministic shell snapshots.
5. Preserve the approved orange progress accent and aligned audio control.
6. Create a pure-.NET `Player.Application` layer for commands and application
   services, with no Avalonia or native VLC types. Do not create a separate UI
   library yet; reassess that boundary after UI-03.

Acceptance:

- Existing open, play/pause, seek, volume, stop, and always-on-top behavior is
  unchanged.
- `MainWindow` becomes composition and navigation, not the command owner.
- One command drives every representation of an action.
- Existing managed tests and native composition smoke remain green.

Likely files: `src/Player.Contracts/`, `src/Player.Domain/`, new
`src/Player.Application/`, presentation files under `src/VLCAvalonia/`, new
`tests/Player.Application.Tests/`, new `tests/VLCAvalonia.UITests/`, and
project/solution files.

Commit: `refactor: establish player presentation architecture`

### UI-02 — Modern transport and fullscreen milestone

Repository: `vlc-avalonia`
Prerequisite: UI-01 and any capability package identified by UI-00

Tasks:

1. Add mute, fullscreen, short skip backward/forward, and playback speed.
2. Add media title and duration without restoring a redundant Playing/Paused
   status label.
3. Implement fullscreen auto-hide with focus-safe wake-up behavior.
4. Add default keyboard commands and a modern context menu.
5. Make the bottom dock responsive at narrow widths.

Acceptance:

- Button, context-menu, and shortcut execution stay synchronized.
- Play/pause, mute, and fullscreen icons follow actual state.
- Controls never hide while a menu, tooltip, slider drag, or keyboard focus is
  active.
- The timeline and volume thumb remain fully visible at all supported sizes.

Commit: `feat: add modern transport and fullscreen controls`

### UI-03 — Settings foundation and MVP

Repository: `vlc-avalonia`
Prerequisite: UI-01

Tasks:

1. Add the versioned, atomic settings service and migration tests.
2. Build searchable settings navigation with General, Appearance, and Playback
   pages.
3. Implement theme, density, control timeout, resume preference, remember
   volume, default speed, and skip intervals.
4. Label `Live`, `NextOpen`, and `Restart` behavior in the UI.
5. Add per-page reset and global reset confirmation.

Acceptance:

- Settings survive restart and invalid settings recover safely.
- UI and shortcuts react immediately to live settings.
- Settings that require reopen/restart never pretend to apply immediately.
- Reset restores documented defaults.

Commit: `feat: add persistent modern player settings`

### UI-04 — Open, recent media, and resume

Repository: `vlc-avalonia`
Prerequisites: UI-02 and UI-03

Tasks:

1. Replace the single Open button with a split action for file, folder, and
   network URL.
2. Add drag-and-drop and recent-media presentation.
3. Persist resume positions with configurable minimum/maximum thresholds.
4. Add clear-history and privacy controls.
5. Present useful, nontechnical errors for invalid, unavailable, or failed
   sources while retaining detailed diagnostics.

Acceptance:

- File and URL opening share one validated application service.
- Duplicate recent entries normalize to one item.
- Resume can be accepted, declined, or disabled.
- History is never retained when its privacy setting is off.

Commit: `feat: add modern media opening and resume`

### UI-05 — Queue and playlist parity

Repository: product first; separate `vlclr` prerequisite if required
Prerequisites: UI-00, UI-02, and UI-04

Tasks:

1. Add a queue drawer with current item, selection, reorder, remove, and clear.
2. Enable previous/next only when valid.
3. Add repeat modes and shuffle with state reflected everywhere.
4. Support add-and-play versus enqueue for file, folder, URL, and drag/drop.
5. Add playlist load/save only after core queue behavior is stable.

Acceptance:

- Queue snapshots are generation-safe and marshalled to the dispatcher.
- Reorder/remove during playback cannot invalidate native ownership.
- Previous, next, repeat, and shuffle survive reopen and end-of-media events.
- Empty, loading, and failure states are designed and tested.

Commit: `feat: add playback queue and playlist controls`

### UI-06 — Tracks, chapters, and synchronization

Repository: product first; separate `vlclr` prerequisite if required
Prerequisite: UI-05

Tasks:

1. Add audio-track, subtitle-track, and chapter selection to the right drawer
   and context menu.
2. Add external audio/subtitle file actions where supported.
3. Add audio and subtitle delay controls with reset.
4. Expose capabilities honestly when a media item has no tracks or chapters.
5. Keep GPU subpicture rendering as a background prerequisite for claiming
   full subtitle parity on the opaque path.

Acceptance:

- Track lists update safely when media changes.
- Selected items match VLC's reported state.
- Delay controls use clear units and have bounded values.
- Image-based and styled subtitles are not reduced to plain text by the UI.

Commit: `feat: add track chapter and synchronization controls`

### UI-07 — Complete settings and media adjustments

Repository: product first; separate `vlclr` prerequisite if required
Prerequisites: UI-03 and UI-06

Tasks:

1. Add Audio, Video, Subtitles, Hotkeys, and Advanced settings pages.
2. Add aspect ratio, crop, zoom, deinterlace, snapshot, output-device, and
   normalization controls where the capability matrix permits.
3. Implement searchable and editable shortcuts with conflict handling.
4. Add media information, codec details, and a diagnostics copy action.
5. Ensure startup-only VLC options are validated and never assembled from
   unchecked free-form input.

Acceptance:

- Settings and contextual controls show the same effective values.
- Hotkey conflicts cannot be saved silently.
- Snapshot and media-information actions report success/failure clearly.
- Advanced settings cannot break command-line quoting or native startup.

Commit: `feat: complete playback settings and media tools`

### UI-08 — Library, accessibility, and final qualification

Repository: `vlc-avalonia`
Prerequisites: UI-01 through UI-07

Tasks:

1. Add an optional lightweight library over recent items, favorites, and
   resume history. Do not require scanning by default.
2. Complete screen-reader names, keyboard navigation, focus restoration,
   reduced-motion behavior, contrast, and localization readiness.
3. Test high DPI, narrow windows, fullscreen, multiple monitors, suspend,
   device loss, reopen, and clean shutdown.
4. Run Debug/Release, managed tests, Native AOT publish, and all applicable
   hardware/fallback/UI/lifecycle smokes.
5. Update user documentation and the capability matrix with verified results.

Acceptance:

- Daily-use parity items are either verified or explicitly marked unavailable.
- No critical action requires pointer-only interaction.
- The library honors privacy settings and can be fully cleared.
- Hardware composition and fallback qualification remain green.

Commit: `feat: qualify the modern VLC Avalonia experience`

## 8. Milestones and dependency order

```text
Milestone A — Modern core player
UI-00 -> UI-01 -> UI-02
                  |
                  +---- UI-03

Milestone B — Daily-use VLC parity
UI-02 + UI-03 -> UI-04 -> UI-05 -> UI-06

Milestone C — Settings, tools, and polish
UI-03 + UI-06 -> UI-07 -> UI-08

Background, independently scheduled:
GPU presentation of VLC-rendered subpictures
HDR metadata/tone mapping
orientation transforms
codec/driver and physical multi-GPU qualification
```

Default dispatch is sequential: UI-00, UI-01, UI-02, UI-03, UI-04, UI-05,
UI-06, UI-07, UI-08. A separate VLCLR prerequisite discovered by UI-00 is
completed and pinned before its dependent product package starts.

## 9. Completion record

Update this table only after validation and commit:

| Package | Terra agent/task | Repository | Commit | Validation | Result |
|---|---|---|---|---|---|
| UI-00 | `ui_00` | `vlc-avalonia` / `feature/modern-player-ui` | `a6e38c2..33fa5ef` | Historical package acceptance reconstructed from the corrective commit and final branch evidence; package-local suite counts were not preserved. | complete |
| UI-01 | `ui_01` | `vlc-avalonia` / `feature/modern-player-ui` | `c59bc54..9af346f` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-02 | `ui_02` | `vlc-avalonia` / `feature/modern-player-ui` | `31b194a..92e1532` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-03 | `ui_03` | `vlc-avalonia` / `feature/modern-player-ui` | `a88fa5a..c9430b5` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-04 | `ui_04` | `vlc-avalonia` / `feature/modern-player-ui` | `012806e..9a7a293` (qualified code tip `9a7a293`) | Debug+Release restore/build; tests 25 app + 72 UI + 7 engine each; Release layout/ui/engine-smoke (`cycles=1`) PASS, including transformed chrome, privacy-confirmation, and toast geometry | complete |
| UI-05 | `ui_05` | `vlc-avalonia` / `feature/modern-player-ui` | `9065842..402f1f4` (qualified code tip `402f1f4`; VLCLR pin `914751006ca3c1f859bbdaa5a0144b227efa3ff4`) | Debug+Release forced restore/rebuild; tests 28 app + 77 UI + 8 engine each; Release engine/UI/layout (1280px and 480px)/queue smoke PASS. Native add/reorder/select/remove/navigation/repeat/order are qualified; product Clear resets the core and publishes a new empty generation because synchronous VLC 3 `playlist_Clear` after current-item deletion stalls `libvlc_InternalDestroy`. Fresh enqueue uses an empty core with `playNow=false`; explicit select/play and mode replay are qualified. | complete |
| UI-06 | `ui_06` | `vlc-avalonia` / `feature/modern-player-ui` | `b3708f9..f609bda` (VLCLR pin `4ce5edc6c77efa4643894ec5de56a1db8a7422b9`) | Debug+Release build; 29 application + 79 UI + 8 engine tests; Release media-capabilities, queue, engine (`cycles=1`), and layout (1280px/480px) smokes PASS. The dispatched media smoke emitted Passed: Tracks, Chapters, AudioDelay, SubtitleDelay, Attachment (pending-next-open), Complete; Skipped: 0; Failed: 0. Its ledger emits distinct title/chapter skips only when the fixture lacks them and never treats skips as parity. | complete |
| UI-07 | `ui_07` | `vlc-avalonia` / `feature/modern-player-ui` | `cee77bf..2de038c` (VLCLR pin `e90b786de74c6ed440dea8bf65329907739b0c60`) | Debug+Release affected suites: 41 application + 83 UI + 8 engine; normal Release build. Release player-capabilities smoke: Passed AudioSettings, MediaInformation, Complete; Skipped VideoPresentation, Deinterlace, Snapshot; Failed 0. Release media-capabilities, verbose queue, engine (`cycles=1`), opaque/UI-composition, and 1280px/480px layout smokes PASS. | complete with explicit unavailable video/device/codec limitations; external attachments are pending-next-open and subtitle rendering remains unqualified. |
| UI-08 | `ui_08` | `vlc-avalonia` / `feature/modern-player-ui` | `ac74f1b..6a5fc39` (feature `1c3bebf`; VLCLR pin `e90b786de74c6ed440dea8bf65329907739b0c60`) | Debug+Release affected `--no-restore` suites: 53 application + 86 UI Passed, 0 Failed, 0 Skipped; normal Release build Passed. Earlier package Native AOT publish, engine (8), opaque, queue, three-path composition, forced adapter-mismatch fallback, player-capabilities (3 Passed / 3 explicitly Skipped / 0 Failed), and media-capabilities (6 Passed / 0 Skipped / 0 Failed) remain green. Revised Release layout (1280px/480px) PASS: Library explicitly closes Queue/Open/URL/Settings sibling overlays and exercises its clear-confirmation cancel path; Release UI-composition fullscreen/reopen PASS. | complete with explicit physical multi-monitor/multi-GPU, OS suspend/resume, arbitrary compositor-scale, video-adjustment/device-enumeration/codec-stream, and subtitle-rendering limitations. |

## 10. GPT-5.6 Terra subagent prompt template

```text
Implement <PACKAGE-ID> from docs/UI_IMPLEMENTATION_PLAN.md using
`gpt-5.6-terra`.

Repository: <absolute repository path>
Prerequisite commits: <exact hashes>
Allowed files: <copy or narrow from the package>
Out of scope: every other package, unrelated refactors, pushing, rebasing,
amending, branch changes, and background renderer work unless explicitly named.

Read all applicable AGENTS.md files and the complete package before acting.
Inspect git status and relevant diffs. Preserve all unrelated changes. First
verify the capability exists; if it requires missing VLCLR work, stop and
report the smallest prerequisite package instead of faking the UI behavior.

Implement the package, add focused tests, run every listed validation, and
perform the required visual checks. Stage only explicit package paths and
create the specified commit only when validation passes.

Return: model/version, task identifier, summary, changed files,
validation commands and results, visual evidence/notes, commit hash, remaining
git status, and risks.
```

The orchestrator must reject a completion report that does not identify a
`gpt-5.6-terra` model/version or that claims native behavior without the
required smoke.

## 11. Principal risks

- The current engine contract supports only open, play/pause, seek, volume,
  and stop. Mute state, rate, tracks, chapters, queue behavior, and output
  devices require capability work before corresponding UI can be enabled.
- Queue and track features may require sibling `vlclr` changes. Add reviewed
  bindings there rather than duplicating native ABI declarations in the app.
- Current hard-coded dark brushes are not yet a complete theme system.
- Fullscreen auto-hide and focus handling must not disrupt the Avalonia
  composition surface, slider capture, or lifecycle smoke tests.
- Hotkeys must ignore editable controls and respect focused sliders and menus.
- Settings paths and filesystem operations require injection for deterministic
  tests and safe migration/corruption recovery.
- Do not restore the removed playback-status label or GPU-composition badge.
- Subtitle selection may be exposed only with honest capability state. Full
  opaque-D3D11 subtitle presentation remains a background engineering track.

## 12. Phase two — VLC 4-inspired daily-use experience on VLC 3/Avalonia

### Outcome and boundary

Use the strongest parts of the current VLC 4 desktop UI's visual language,
information architecture, responsive behavior, and interactions in Avalonia
while keeping the qualified VLC 3 engine, VLCLR bindings, and custom
video-composition path. This is a selective clean Avalonia translation, not a Qt
dependency, a line-by-line QML port, or a promise of 100% VLC 4 UI parity.

VLC 4 UI parity and VLC 4 engine parity are different claims. A VLC 4 control
may be implemented only when the VLC 3 capability matrix has a working product
binding. Otherwise the corresponding surface is omitted or shown as unavailable
with an explanation. The phase must not weaken the working seek, play/pause,
volume, queue, history, privacy, focus, or fallback behavior qualified by UI-08.

The target should feel recognizably informed by VLC 4 while remaining a focused,
modern player rather than a replica. It retains the deliberate product decisions
already approved here:

- video remains visually dominant during playback;
- orange is a restrained active-state accent over the dark/white system;
- there is no redundant Playing/Paused label or customer GPU-composition badge;
- unsupported VLC 4 engine features never masquerade as working controls; and
- subtitle/subpicture composition remains a background renderer track.

### Selected product scope

The VLC 4 reference is authoritative only for the selected daily-use surfaces.
Settings deliberately use a new design because the VLC 4 preferences UI is not
the modernization target.

| Treatment | Surfaces |
|---|---|
| Match closely | Main window shell, primary navigation, playback view, transport/control bar, fullscreen behavior, queue/playlist presentation, context/overflow menus, empty/loading/error states, and responsive interaction patterns. |
| Adapt to this product | Home/browse entry points, lightweight no-scan library, recents/favorites/resume, track/chapter/synchronization panels, media information, open file/folder/URL flows, and unavailable VLC 3 capabilities. |
| Design independently | Settings/preferences, advanced capability explanations, hotkey editor, reset/confirmation flows, and `Live`/`NextOpen`/`Restart` presentation. These must look modern and coherent with the VLC 4-inspired shell, not like the current VLC 4 preferences window. |
| Exclude | Pixel-perfect parity, every VLC 4 page/dialog/menu, media indexing, streaming/transcoding/capture authoring, service discovery catalogs, renderer administration, skins, plugin management, and controls without a safe VLC 3 binding. |

The initial customer-facing result therefore consists of six areas:

1. VLC 4-inspired application shell and navigation.
2. VLC 4-inspired video/player screen and playback controls.
3. Modern queue, playlist, and lightweight library surfaces.
4. Modern media actions for tracks, chapters, synchronization, and information.
5. A newly designed settings experience specific to this Avalonia product.
6. Responsive, keyboard-accessible, reduced-motion, high-contrast states.

### Available reference baseline

| Reference | Local state | Use |
|---|---|---|
| VLC 4 source | `C:\Users\Martin\Code\videolabs\vlc`, `master`, commit `5dd4aebdabf4a625822036bda08a24a91295158c`, described as `4.0.0-dev-38799-g5dd4aebdab` | Qt/QML hierarchy, state rules, design tokens, icons, assets, accessibility text, and interaction semantics. Read-only reference. |
| VLC 4 executable | Not currently present in the inspected repositories | Required for authoritative runtime behavior and screenshot capture. Acquire a build and record its exact revision before V4UI-01. |
| VLC 3 runtime | `C:\Users\Martin\Code\videolabs\vlclr\vlc-3.0.24-beta1\vlc.exe` | Product engine/runtime qualification only; it is not a VLC 4 visual reference. |
| Avalonia baseline | `feature/modern-player-ui`, reviewed tip `58550fc810b1dfe61f41caec068084b718804f87` | Starting point for the new UI parity branch. |

The preferred reference set is all three of source, runnable executable, and
captured screenshots. Source defines intent and reusable asset provenance; the
executable defines effective behavior; screenshots provide reviewable golden
states. If the executable and source revisions differ, record the mismatch and
use the executable as the behavioral truth while source-derived conclusions are
labelled as inferences.

Before copying any VLC icon, font, or image, record its source path, revision,
license, and required attribution. Reference screenshots are local engineering
artifacts by default. Do not commit user history, personal paths, network
credentials, copyrighted media frames, or bulk VLC captures unless explicitly
approved.

## 13. Reference capture and comparison method

### Isolated capture environment

Run the VLC 4 reference build with an isolated temporary configuration and no
automatic media discovery, scanning, updater interaction, or unrelated network
access. Use deterministic reference media owned or approved for test use. Each
capture run records:

- executable path, file version, source/build revision when available, and OS;
- window client size, monitor scale, theme, and full-screen/windowed state;
- exact navigation path and keyboard/pointer actions used to reach the state;
- media fixture and capability state, with sensitive paths redacted; and
- screenshot filename plus a small machine-readable manifest.

Keep raw VLC 4 captures under an ignored
`artifacts/vlc4-reference/<revision>/` directory. Commit only derived UI
specifications, measurement tables, asset provenance, automation code, and
approved non-sensitive baselines. Avalonia-owned golden screenshots belong
under the test project after review.

### Mandatory reference states

| Area | Required states |
|---|---|
| Startup | First run where reproducible, empty player, home/browse navigation, and no-library state |
| Playback | Opening, playing, paused, seeking, muted, volume, speed, end/error, controls visible, and controls hidden |
| Window | 1280x720, 1920x1080, 480px narrow, maximized, fullscreen, restored, 100%, 125%, and 150% scale where available |
| Navigation | Home, video, music/audio, browse/network, library/history, playlist/queue, and back navigation |
| Media controls | Audio tracks, subtitle tracks, chapters, synchronization, information, and unavailable/empty variants |
| Secondary UI | Context menus, overflow menus, open URL/file flows, dialogs, toasts, and confirmations |
| Preferences | Inventory available categories and behavior only. VLC 4 preference visuals are explicitly not a parity target. Capture enough evidence to avoid losing useful settings or semantics. |
| Accessibility | Keyboard-only traversal, visible focus, Escape behavior, focus restoration, automation names, contrast, and reduced motion |

Capture both the VLC 4 reference and the matching Avalonia state. Dynamic video,
timestamps, cursor position, and platform text rasterization may be masked in
automated comparison, but geometry, hierarchy, spacing, control state, icon
silhouette, and deliberate color differences must remain reviewable.

Reference-to-Avalonia acceptance uses measured geometry and annotated contact
sheets for the selected parity surfaces rather than claiming impossible pixel
identity across Qt and Avalonia. Settings are reviewed against the product's own
modern design specification. Approved Avalonia states then become deterministic
regression baselines.

## 14. Phase-two branch, commit, and dispatch policy

After V4UI-00 is approved and before implementation begins, create
`feature/vlc4-ui-parity` from product commit
`58550fc810b1dfe61f41caec068084b718804f87`. If the completed modern UI branch is
merged first, verify that the selected base contains that exact history and
record the new base instead of rebasing silently.

The VLC 4 source checkout is read-only reference material. Do not switch it,
patch it, build over user changes, or commit generated capture files there. The
VLC 3 runtime and sibling VLCLR repository also remain unchanged unless a UI
package discovers a genuinely required native capability. Any such work gets a
separate VLCLR branch, Terra prerequisite, commit series, validation, and pinned
hash before dependent product work resumes.

Each V4UI package is owned by one fresh `gpt-5.6-terra` subagent and is dispatched
sequentially in the shared checkout. The orchestrator reviews the reference
evidence, diff, screenshots, tests, and commit before dispatching the next
package. A package creates its named primary commit after validation; review
corrections use separate focused commits. Agents do not push, merge, rebase,
amend, or change branches.

No implementation package starts until V4UI-00 has pinned the executable and
source baselines. No final merge or push occurs without separate user direction.

## 15. VLC 4 UI parity work packages

Execution note: the package list in Sections 15--17 is retained as technical
inventory but is superseded by the screen order and mandatory user gates in
[VLC4_SCREEN_BY_SCREEN_PLAN.md](VLC4_SCREEN_BY_SCREEN_PLAN.md). Do not dispatch
these batch packages directly.

### V4UI-00 — Pin and capture the VLC 4 reference

Repository: `vlc-avalonia` documentation, capture tooling, and ignored artifacts

Prerequisites: reviewed UI-08 tip; VLC 4 source; runnable VLC 4 executable

Tasks:

1. Identify the executable's exact version/build and map it to the closest
   available source revision without changing the reference checkout.
2. Add reproducible Windows window-capture and UI-automation tooling with an
   isolated temporary profile and sanitized manifest.
3. Capture the selected mandatory state matrix and inspect the matching Qt/QML
   source for component hierarchy, state transitions, tokens, icons, and
   shortcuts. Treat preferences as a behavior inventory, not a visual target.
4. Produce a feature/state map from VLC 4 surfaces to the existing VLC 3 product
   contracts, including working, adapted, unavailable, and non-goal states.
5. Record asset provenance and decide which artifacts may be committed.

Acceptance:

- The executable and source baselines are pinned; any mismatch is explicit.
- Another run can reproduce every capture without using personal application
  state or scanning the machine.
- Every selected VLC 4-inspired surface links to runtime evidence, source
  evidence, and an engine capability state; independently designed settings link
  to an approved Avalonia specification.
- No implementation styling is accepted from memory or isolated screenshots.

Commit: `docs: pin VLC 4 UI reference baseline`

### V4UI-01 — Translate the VLC 4 design system

Prerequisite: V4UI-00

Tasks:

1. Translate measured colors, typography, spacing, radii, elevation, focus,
   animation, density, and responsive breakpoints into Avalonia resources.
2. Review and import only approved icons/assets with provenance; prefer existing
   vector/code-native assets over raster recreation.
3. Establish reusable navigation, page, toolbar, card, menu, drawer, dialog,
   empty-state, and notification components.
4. Extend deterministic headless rendering and screenshot comparison support.

Acceptance:

- Tokens and components reproduce the reference states without one-off page
  constants.
- Dark/system variants, contrast, focus, and reduced motion remain testable.
- Existing timeline and volume alignment/endpoint behavior remain green.

Commit: `refactor: add VLC 4 Avalonia design system`

### V4UI-02 — Rebuild the window shell and navigation

Prerequisite: V4UI-01

Tasks:

1. Reproduce the VLC 4 window chrome, navigation rail/header, back behavior,
   page transitions, global search placement, and responsive collapse rules.
2. Map VLC 4 home, video, music/audio, browse, and playlist destinations to the
   lightweight local product model without enabling background scanning.
3. Preserve custom composition z-order, drag regions, window actions, keyboard
   navigation, focus restoration, and narrow-window containment.

Acceptance:

- Primary VLC 4 destinations and navigation behavior match the pinned reference
  or have an annotated capability-based adaptation.
- No critical navigation action is pointer-only.
- Window chrome and overlays remain safe in fullscreen and compact layouts.

Commit: `feat: add VLC 4 style application shell`

### V4UI-03 — Match the VLC 4 playback experience

Prerequisite: V4UI-02

Tasks:

1. Reproduce the pinned VLC 4 playback view, control bar, progress treatment,
   control grouping, active/disabled states, hover/focus behavior, timing, and
   fullscreen reveal/dismissal.
2. Map play/pause, previous/next, stop, seek, volume/mute, speed, fullscreen,
   queue, and media title to the existing central command registry.
3. Preserve the approved orange accent, visible seek/volume thumbs, correct
   play/pause icon state, accurate seeking, and audio-control alignment.
4. Adapt VLC 4 controls that lack safe VLC 3 bindings as explicit unavailable
   states rather than placeholders.

Acceptance:

- Reference/Avalonia contact sheets cover empty, playing, paused, hover,
  keyboard focus, compact, and fullscreen states.
- Button, menu, context-menu, and shortcut state remain synchronized.
- All transport, layout, engine, and three-path composition smokes pass.

Commit: `feat: match VLC 4 playback experience`

### V4UI-04 — Match library, browse, and playlist surfaces

Prerequisite: V4UI-03

Tasks:

1. Reproduce VLC 4 list/grid hierarchy, sorting/filter affordances, selection,
   contextual actions, empty/error/loading states, and playlist presentation.
2. Adapt the UI to the existing no-scan recent/favorites/resume library and
   generation-safe VLC 3 queue.
3. Preserve privacy switches, complete clearing, explicit favorite creation,
   drag/drop, open/enqueue behavior, and safe queue mutation.
4. Do not introduce media indexing or filesystem scanning without a separate
   user-approved product package.

Acceptance:

- Library and playlist states match the reference interaction model while their
  data limitations are explicit.
- Queue, history migration, read-only persistence, and privacy tests remain
  green.
- Wide/narrow reference captures show no clipping or overlapping surfaces.

Commit: `feat: match VLC 4 library and playlist surfaces`

### V4UI-05 — Match menus and media-detail surfaces

Prerequisite: V4UI-04

Tasks:

1. Reproduce VLC 4 overflow/context menus, track/chapter panels,
   synchronization, media information, diagnostics, confirmations, and errors.
2. Preserve fresh native readback and generation safety for queue, tracks,
   chapters, delays, attachments, and metadata.
3. Keep aspect/crop/zoom, deinterlace, snapshot, device enumeration, codec
   streams, and subtitle rendering unavailable where the matrix requires it.

Acceptance:

- Enabled, empty, loading, error, and unavailable states are visually and
  behaviorally represented.
- Skipped native capabilities remain explicit non-parity results.
- Media, player-capability, queue, and lifecycle smokes pass.

Commit: `feat: match VLC 4 playback menus and media details`

### V4UI-06 — Design modern settings and secondary flows

Prerequisite: V4UI-05

Tasks:

1. Create a new Avalonia settings design that is visually coherent with the
   VLC 4-inspired shell but does not copy VLC 4's preferences UI.
2. Use searchable categories, responsive navigation, clear section cards,
   switches/sliders/choices with useful summaries, inline validation, and a
   progressively disclosed Advanced area instead of a dense preference tree.
3. Map existing settings to the new presentation while preserving `Live`,
   `NextOpen`, and `Restart` semantics and typed allowlisted startup arguments.
4. Keep unavailable settings visible only when the explanation helps the user;
   otherwise omit them. Preserve shortcut conflict validation, per-section reset,
   global reset confirmation, history privacy/clear controls, and diagnostics.
5. Adapt open-file/folder/URL, recent/resume, toast, diagnostics, and failure
   surfaces to the same modern component system.

Acceptance:

- Settings and contextual controls expose the same effective values.
- Invalid settings/history remain recoverable and privacy actions remain
  complete.
- Product-owned captures cover every settings category and secondary flow at
  desktop and compact widths. Review explicitly confirms that settings are not a
  copy of the VLC 4 preferences window.

Commit: `feat: add modern Avalonia settings and dialogs`

### V4UI-07 — Complete responsive and accessible parity

Prerequisite: V4UI-06

Tasks:

1. Complete keyboard order, screen-reader names/help, focus traps/restoration,
   high-contrast behavior, reduced motion, and localization resource coverage.
2. Verify VLC 4 responsive adaptations at all captured widths/scales and define
   deliberate Avalonia adaptations where the reference has no narrow layout.
3. Qualify maximized/fullscreen transitions, reopen/shutdown, fallback/device
   loss simulation, and window placement without claiming unavailable physical
   monitor tests.

Acceptance:

- No critical action is pointer-only and no hidden overlay retains focus.
- All new customer strings are resource-backed.
- Every required state is contained and usable at 480px and the captured desktop
  sizes/scales.

Commit: `feat: complete VLC 4 responsive accessibility parity`

### V4UI-08 — Visual regression and final qualification

Prerequisite: V4UI-07

Tasks:

1. Produce final reference/Avalonia contact sheets for the selected parity
   surfaces and product-owned contact sheets for settings; review every annotated
   divergence.
2. Promote approved Avalonia renders to deterministic golden tests with masks
   only for documented dynamic regions.
3. Run Debug/Release builds and tests, Native AOT publish, all UI/layout,
   lifecycle, queue, capability, opaque/hardware, upload, CPU, and forced
   fallback smokes.
4. Update the README, capability matrix, reference manifest, and completion
   ledger with exact evidence and honest limitations.

Acceptance:

- Every selected VLC 4 reference state is matched, deliberately adapted, or
  explicitly unavailable with evidence. Excluded surfaces are not counted as
  parity gaps.
- No unexplained high-priority visual or interaction divergence remains.
- Existing VLC 3 native behavior and composition qualification remain green.
- The tracked product and sibling repositories are clean; no reference checkout
  was modified.

Commit: `feat: qualify VLC 4 UI parity on Avalonia`

## 16. Phase-two dependency order

```text
Reference gate
V4UI-00
   |
Design and shell
V4UI-01 -> V4UI-02
                 |
Core experience  +-> V4UI-03 -> V4UI-04 -> V4UI-05 -> V4UI-06
                                                        |
Final polish                                            v
                                                    V4UI-07 -> V4UI-08

Background only:
VLC-rendered subpicture composition, HDR/tone mapping, orientation transforms,
new media indexing, and physical multi-monitor/multi-GPU qualification
```

## 17. Phase-two completion record

Update only after package validation and commit:

| Package | Terra agent/task | Reference revision | Product commit | Validation | Result |
|---|---|---|---|---|---|
| V4UI-00 | pending | source `5dd4aeb`; executable pending | pending | pending | blocked until a runnable VLC 4 build is pinned |
| V4UI-01 | pending | pending | pending | pending | pending |
| V4UI-02 | pending | pending | pending | pending | pending |
| V4UI-03 | pending | pending | pending | pending | pending |
| V4UI-04 | pending | pending | pending | pending | pending |
| V4UI-05 | pending | pending | pending | pending | pending |
| V4UI-06 | pending | pending | pending | pending | pending |
| V4UI-07 | pending | pending | pending | pending | pending |
| V4UI-08 | pending | pending | pending | pending | pending |

## 18. Phase-two risks and stop conditions

- VLC 4 remains a development line. A source/executable mismatch can make QML
  inspection misleading; pin and report both before implementation.
- The target is selective daily-use parity, not completeness. Stop scope growth
  when it does not improve the six selected customer-facing areas.
- Qt and Avalonia rasterize text, shadows, animation, and native menus
  differently. Measure layout and state fidelity without claiming raw pixel
  identity across frameworks.
- VLC 4 library/indexing behavior is not authorization to scan user disks in
  this sample. The no-scan privacy boundary remains until separately approved.
- Visual similarity does not justify speculative VLC 3 ABI calls. Missing engine
  behavior stops at an unavailable state or a separately approved VLCLR package.
- Reference automation must not alter the user's VLC profile, recent history,
  associations, updater state, or network services.
- Do not copy an icon/font/image until its provenance and repository licensing
  treatment are recorded.
- Stop V4UI-00 and ask for a runnable VLC 4 build if exact behavior cannot be
  observed. Source-only inference is insufficient for the implementation gate.

# Conic UI implementation plan

Status: complete through UI-08; final managed, Native AOT, layout, lifecycle,
composition, queue and capability qualification is recorded below. Physical
multi-monitor/multi-GPU and OS suspend/resume remain explicitly unqualified.
Planning baseline: 2026-09-01
Primary scope: modern Windows desktop UI with daily-use VLC playback parity
Background track: GPU presentation of VLC-rendered subpictures on the opaque
D3D11 path

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
dotnet build conic.slnx --no-restore
dotnet build conic.slnx -c Release --no-restore
dotnet test conic.slnx --no-build --no-restore
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

Repository: `conic`
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

Repository: `conic`
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
`src/Player.Application/`, presentation files under `src/Conic/`, new
`tests/Player.Application.Tests/`, new `tests/Conic.UITests/`, and
project/solution files.

Commit: `refactor: establish player presentation architecture`

### UI-02 — Modern transport and fullscreen milestone

Repository: `conic`
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

Repository: `conic`
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

Repository: `conic`
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

Repository: `conic`
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

Commit: `feat: qualify the modern Conic experience`

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
| UI-00 | `ui_00` | `conic` / `feature/modern-player-ui` | `a6e38c2..33fa5ef` | Historical package acceptance reconstructed from the corrective commit and final branch evidence; package-local suite counts were not preserved. | complete |
| UI-01 | `ui_01` | `conic` / `feature/modern-player-ui` | `c59bc54..9af346f` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-02 | `ui_02` | `conic` / `feature/modern-player-ui` | `31b194a..92e1532` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-03 | `ui_03` | `conic` / `feature/modern-player-ui` | `a88fa5a..c9430b5` | Historical package acceptance reconstructed from the focused commit range and final aggregate qualification; package-local suite counts were not preserved. | complete |
| UI-04 | `ui_04` | `conic` / `feature/modern-player-ui` | `012806e..9a7a293` (qualified code tip `9a7a293`) | Debug+Release restore/build; tests 25 app + 72 UI + 7 engine each; Release layout/ui/engine-smoke (`cycles=1`) PASS, including transformed chrome, privacy-confirmation, and toast geometry | complete |
| UI-05 | `ui_05` | `conic` / `feature/modern-player-ui` | `9065842..402f1f4` (qualified code tip `402f1f4`; VLCLR pin `914751006ca3c1f859bbdaa5a0144b227efa3ff4`) | Debug+Release forced restore/rebuild; tests 28 app + 77 UI + 8 engine each; Release engine/UI/layout (1280px and 480px)/queue smoke PASS. Native add/reorder/select/remove/navigation/repeat/order are qualified; product Clear resets the core and publishes a new empty generation because synchronous VLC 3 `playlist_Clear` after current-item deletion stalls `libvlc_InternalDestroy`. Fresh enqueue uses an empty core with `playNow=false`; explicit select/play and mode replay are qualified. | complete |
| UI-06 | `ui_06` | `conic` / `feature/modern-player-ui` | `b3708f9..f609bda` (VLCLR pin `4ce5edc6c77efa4643894ec5de56a1db8a7422b9`) | Debug+Release build; 29 application + 79 UI + 8 engine tests; Release media-capabilities, queue, engine (`cycles=1`), and layout (1280px/480px) smokes PASS. The dispatched media smoke emitted Passed: Tracks, Chapters, AudioDelay, SubtitleDelay, Attachment (pending-next-open), Complete; Skipped: 0; Failed: 0. Its ledger emits distinct title/chapter skips only when the fixture lacks them and never treats skips as parity. | complete |
| UI-07 | `ui_07` | `conic` / `feature/modern-player-ui` | `cee77bf..2de038c` (VLCLR pin `e90b786de74c6ed440dea8bf65329907739b0c60`) | Debug+Release affected suites: 41 application + 83 UI + 8 engine; normal Release build. Release player-capabilities smoke: Passed AudioSettings, MediaInformation, Complete; Skipped VideoPresentation, Deinterlace, Snapshot; Failed 0. Release media-capabilities, verbose queue, engine (`cycles=1`), opaque/UI-composition, and 1280px/480px layout smokes PASS. | complete with explicit unavailable video/device/codec limitations; external attachments are pending-next-open and subtitle rendering remains unqualified. |
| UI-08 | `ui_08` | `conic` / `feature/modern-player-ui` | `ac74f1b..38d15ec` (feature `1c3bebf`; VLCLR pin `e90b786de74c6ed440dea8bf65329907739b0c60`) | Debug+Release affected `--no-restore` suites: 50 application + 86 UI Passed, 0 Failed, 0 Skipped; normal Release build Passed. Earlier package Native AOT publish, engine (8), opaque, queue, three-path composition, forced adapter-mismatch fallback, player-capabilities (3 Passed / 3 explicitly Skipped / 0 Failed), and media-capabilities (6 Passed / 0 Skipped / 0 Failed) remain green. Revised Release layout (1280px/480px) PASS: Library explicitly closes Queue/Open/URL/Settings sibling overlays and exercises its clear-confirmation cancel path; Release UI-composition fullscreen/reopen PASS. | complete with explicit physical multi-monitor/multi-GPU, OS suspend/resume, arbitrary compositor-scale, video-adjustment/device-enumeration/codec-stream, and subtitle-rendering limitations. |

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

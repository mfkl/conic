# Modern player capability matrix

Current reconciliation (2026-09-12): the Windows backend, queue/transport,
media information and video geometry implementations supersede several early
prerequisites in this inventory. The current VLCLR pin is in
[`eng/versions.json`](../eng/versions.json); current delivery checks and remaining
gates are in [contest status](contest/STATUS.md). Subtitle rendering,
deinterlacing, snapshots and device selection remain unfinished. Test counts
and commit references below describe their respective historical checks.

Historical UI-08 product qualification at `6a5fc39` (feature commit
`1c3bebf`); pinned sibling VLCLR
revision `e90b786de74c6ed440dea8bf65329907739b0c60`.
This is the product contract for the UI work packages. A `Missing` row is a
reason to keep its control absent or disabled with an explanatory capability
state; it is never permission to ship a placeholder.

## Evidence and terms

`Product binding` means an engine-neutral contract, immutable snapshot, and
application command implemented in this repository. `VLCLR binding` means a
reviewed, ABI-validated sibling-repository API. `Existing VLCLR` is not enough
by itself: a product binding is still required before a control can work.

The initial `IMediaEngine` inventory below has been superseded by focused
transport, queue, media-capability, audio and media-information contracts.
Their immutable snapshots are generation-safe and their commands are shared by
buttons, menus and keyboard dispatch. New features must continue to use
reviewed VLCLR coverage rather than app-private native calls.

| Status | Meaning |
|---|---|
| Working | Present in the released product surface and exercised by existing code/smoke. |
| Existing native only | VLC/VLCLR has a relevant primitive, but the app has no safe product contract. |
| Missing | No reviewed product binding; the stated VLCLR prerequisite also is absent or insufficient. |
| Background prerequisite | Feature is intentionally not part of a UI package and cannot be claimed. |

## Playback, window, and opening controls

| Control / capability | Current support | Required product binding | Required VLCLR binding | Apply timing / constraints | Validation | Tier |
|---|---|---|---|---|---|---|
| Open one local file | Working: top-bar picker sets `MediaPath`; engine opens a path. | Shared file/URL opening service, command, error/result contract. | None for existing local open. | Live; opening stops the current core and creates a new VLC instance. | Picker/service tests and engine reopen smoke. | Daily-use |
| Open test fixture | Working developer-only button; not a customer feature. | Remove from normal product chrome or put behind diagnostics. | None. | Local test asset only. | Existing smoke uses fixture. | Non-goal |
| Open folder / enqueue folder | Working: validated flat folder expansion adds to the generation-safe queue. | Source expansion, add-and-play/enqueue commands and errors. | Reviewed Q1 playlist binding. | Asynchronous expansion rejects inaccessible sources. | Application/queue tests and Release queue smoke. | Daily-use |
| Open network URL | Working: credential-free HTTP(S) validation, canonicalization and user-facing errors. | Validated source service and shared open command. | Existing reviewed single-source path. | Next open rebuilds the VLC core. | URI/service tests; engine reopen smoke. | Daily-use |
| Drag and drop | Working for validated local files; multiple files enqueue when the queue is available. | Drop-to-open/enqueue service and Avalonia routing. | Reviewed Q1 queue binding. | Live after validation. | UI/application tests and queue smoke. | Daily-use |
| Recent media / resume | Working: atomic versioned local document, duplicate normalization, privacy gates, per-section clear and resume prompt. | Shared history service and commands. | None. | Persist only after usable open; resume applies on a later ready input. | Migration/privacy/resume tests and lifecycle smoke. | Daily-use |
| Play / pause | Working: one button calls `TogglePause`; status drives icon. | Command registry and `CanExecute` from snapshot. | Existing app-private `playlist_Control` call must be replaced by a VLCLR playlist-control abstraction before broad reuse. | Live while a playlist is available. | Contract tests plus engine smoke pause/resume. | Daily-use |
| Stop | Working: one button calls `Stop`. | Command registry and state-aware enablement. | Same reviewed playlist-control abstraction as play/pause. | Live; waits up to 10 seconds, then core is cleaned up. | Existing engine smoke stop/reopen. | Daily-use |
| Seek timeline | Working when VLC reports `can-seek`; pointer drag is protected. | Command registry; elapsed/duration snapshot remains sufficient. | Replace app-private `input_vaControl` with reviewed safe operation. | Live; normalized 0..1 and unavailable for unseekable media. | Existing engine smoke and headless endpoint/drag tests. | Daily-use |
| Short / long skip | Working: configurable bounded relative seek with keyboard, dock and context-menu commands. | Shared command and settings. | Reviewed relative input control. | Live only when seekable. | Application tests and Release engine smoke. | Daily-use |
| Volume | Working: slider sets 0..125%; snapshot refreshes it. | Command registry, remembered-volume setting. | Already has `PlaylistVolumeGet/Set`. | Live; persistence is next application startup/open policy. | Existing engine smoke plus setting tests. | Daily-use |
| Mute | Working: effective mute snapshot, icon, menu and `M` command. | Audio command/state. | Reviewed `PlaylistMuteGet/Set`. | Live while a playlist exists. | Application tests and Release engine smoke. | Daily-use |
| Playback speed | Working: rate snapshot, selector and context menu; default speed is Next open. | Rate command/state and setting. | Reviewed input rate control. | Live per media; default applies next open. | Bounds/state tests and Release engine smoke. | Daily-use |
| Previous / next | Working when queue neighbors are valid. | Queue snapshot/navigation commands. | Reviewed Q1 navigation/callback ownership. | Live only with a valid neighbor. | Release verbose queue smoke. | Daily-use |
| Fullscreen | Working: `F11`, dock/menu command and focus-safe auto-hide. | Window-state and chrome-visibility controller. | None. | Live; overlays, focus and slider drag keep chrome visible. | Release UI-composition and layout smokes. | Daily-use |
| Always on top | Working live checkbox through the shared command. Persistence is not claimed. | Window-state command. | None. | Live for the current window. | UI tests/layout smoke. | Daily-use |
| Title / duration | Working: title fallback and duration snapshot, without a Playing/Paused label. | Metadata snapshot/title policy. | Reviewed media-information binding. | Refresh per media generation. | Media information tests and player-capabilities smoke. | Daily-use |

## Queue, tracks, and media adjustment controls

| Control / capability | Current support | Required product binding | Required VLCLR binding | Apply timing / constraints | Validation | Tier |
|---|---|---|---|---|---|---|
| Queue drawer, select, reorder, remove, clear | Working: immutable generation-safe drawer with keyboard-focusable actions and designed empty/loading/failure states. | Queue contract/snapshots and mutation commands. | Q1 reviewed playlist enumeration, mutation, callbacks and ownership. | Live; clear intentionally resets to a new empty core generation. | Application/UI tests and Release verbose queue smoke. | Daily-use |
| Add-and-play vs enqueue; playlist load/save | Working: validated file/folder/URL paths can add-and-play or enqueue; flat M3U/M3U8 load/save is explicit. | Source service and playlist document parser/writer. | Q1. | Explicit I/O outside engine thread. | Parser/security tests and queue smoke. | Advanced |
| Repeat and shuffle | Working: observed effective mode is reflected by drawer/menu commands. | Queue mode commands/snapshot. | Q2 reviewed mode APIs/notifications. | Live and verified across queue operations. | Release verbose queue smoke. | Daily-use |
| Audio tracks / subtitle tracks | Working selection surface: immutable copied snapshots, coherent playback drawer/context menu, and fresh VLC readback. Empty/loading/error/unavailable states are explicit. | `MediaCapabilitiesState`, generation-safe commands and dispatcher presentation. | T1 (`VLCMediaCapabilities`). | Refresh on input/media generation; selection intent is not treated as effective until readback. | Release media-capabilities smoke and stale-state UI tests. | Daily-use |
| Chapters | Working selection surface with title/chapter lists and empty state. | Same capability snapshot and selection commands. | T2 (`VLCMediaCapabilities`). | Refresh on media generation; selected chapter/title is read back from VLC. | Release media-capabilities smoke; fixture capabilities are reported as distinct Passed/Skipped phases. | Daily-use |
| External audio / subtitle file | Working as a validated **pending-next-open** attachment only. The UI never lists it as a live current track without a later fresh T1 snapshot. | Picker/action and explicit pending result language. | T3 (`VLCMediaCapabilities`). | Next open only; no Reopen/Apply-now claim. | Release media-capabilities smoke creates and cleans a unique temporary SRT. | Advanced |
| Audio / subtitle delay and reset | Working with direct signed millisecond entry, ±100 ms actions, and exact zero reset. | Bounded millisecond commands and effective delay snapshot. | T4 (`VLCMediaCapabilities`). | Live per current input after readback; product bound is ±10,000 ms, deliberately narrower than VLCLR's safety ceiling. | Release media-capabilities smoke and application/UI bounds tests. | Daily-use |
| Aspect ratio, crop, zoom | Working in More → Video adjustments / Video drawer, with native choices and reset. | Generation/output-bound snapshot; shared CPU/GPU crop, pixel aspect and sizing. | **V1 implemented:** VLCLR `50d8492`, reviewed `INPUT_GET_VOUTS`, checked variables and owned choice lists. | Live for one active output, including paused playback; defaults on new input. | CPU fallback and opaque GPU native checks; see `VIDEO_ADJUSTMENTS_REVIEW_2026-09-10.md`. | Advanced |
| Deinterlace | Missing. | Video setting + media adjustment contract. | **VLCLR prerequisite V2:** reviewed startup/per-input control and available modes. | Mark NextOpen or Live only after native verification; do not infer. | Interlaced-fixture smoke. | Advanced |
| Snapshot | Missing. | Snapshot command, output-path service, result/error reporting. | **VLCLR prerequisite V3:** reviewed snapshot trigger and completion/failure path. | Live only with active video output. | File-output and failure smoke. | Advanced |
| Audio output device / normalization / passthrough | Typed, allowlisted restart-only startup argv for output module, normvol, and S/PDIF; device enumeration/selection remains unavailable. | `VLCAudioStartupOptions.ToArguments()` is the only UI startup path; defaults are empty and settings are normalized. | **VLCLR prerequisite A1:** reviewed audio-output device enumeration/selection and relevant variables/options. | Restart. | Exact argv/default/migration tests; target-hardware device changes remain unqualified. | Advanced |
| Media/codec information and diagnostics copy | Generation-safe metadata and codec-stream snapshots, scrollable Info drawer and async diagnostics copy. | URI/query/user-info and control injection are redacted from diagnostics. | **M1 implemented:** VLCLR `b12127b` copies VLC 3 input-item information; IDs 0–1023 and allowlisted fields only. | Refresh per input; clear details on input change. | Ownership/stale-read, immutability/redaction, clipboard and native rendering checks; see `MEDIA_INFORMATION_REVIEW_2026-09-10.md`. | Advanced |
| Lightweight library | Working: no-scan Library presents the existing atomic recent/resume document plus explicit favorites. Recent, favorite, and saved-position entries each have keyboard-accessible remove actions; each history section and **Clear library** are keyboard-accessible. | `MediaHistoryService` v2 migration and shared registry commands; no duplicate store. | None. | Recent/resume privacy switches clear their own data immediately; favorites are user-created and clearable. Valid current v2 history remains readable without a write; migration/sanitization writeback failure retains normalized in-memory data with a diagnostic. | 53 application + 86 UI Release tests; wide/compact layout smoke verifies right-drawer and sibling-overlay exclusivity. | Daily-use |

## Subtitle rendering contract

The control for selecting a subtitle track is distinct from rendering that
track. UI-06 makes selection available with an explicit UI caveat, but does
not establish visual subtitle parity. It never converts image-based or styled
subtitles to UI text.

| Render path | Honest current status | Product claim allowed |
|---|---|---|
| CPU BGRA fallback | With `SubpictureChromas=0`, VLC core may pre-blend a subpicture into the CPU frame before the display callback. This custom vout does not itself compose subpictures, and no CPU subtitle presentation has been qualified. The composition document explicitly records that subtitle/subpicture composition is not implemented. | None. Do not claim CPU subtitle support until a CPU-path native and visual smoke proves it. |
| Opaque D3D11 path | Hardware frames are converted/presented as a BGRA shared texture. This custom vout does not composite the `subpicturePtr`; a GPU subpicture composition track is explicitly background work. | None. Do not claim full, image-based, or styled subtitle parity. |
| Required future rendering | A renderer must consume VLC-rendered subpictures without reducing image-based or styled subtitles to text and must compose correctly on both paths. | Only after CPU and opaque-path native/visual qualification. |

## Startup and settings contract

| Setting / action | Current support | Timing and required work | Tier |
|---|---|---|---|
| Theme, density, control timeout, reduced motion | Working: versioned atomic settings; theme/density/timeout/reduced-motion apply live. Window restoration is not persisted. | Product settings service and live chrome policy. | Daily-use |
| Default speed, skip intervals, resume, volume, repeat | Working where the engine capability exists: skip is live; default speed/resume/volume apply at the documented next-open boundary; queue repeat is live. | Settings service and queue/audio contracts. | Daily-use |
| Hardware decode, video fit/default aspect, subtitle defaults, caching | Current engine supplies `--avcodec-hw=any` or `none`; no settings UI. | Reviewed, allowlisted VLC startup-option builder; classify each as NextOpen or Restart after smoke. | Advanced |
| Preferred language, encoding, font, outline, background | Missing. | Settings persistence plus track/rendering prerequisite; labels cannot imply rendered subtitle parity. | Advanced |
| Editable hotkeys | Searchable, persisted command registry bindings with per-row save/reset and full-map conflict validation. | Live after save; numeric/unknown IDs and conflicts are rejected before storage; dispatch is suppressed for editors, sliders, combo boxes, menus, and other space/arrow-owning controls. | Advanced |
| Transcoding, broadcasting, capture authoring, plugin administration, expert variable tree | Not planned. | Explicitly excluded from this roadmap. | Non-goal |

## Command inventory and defaults

The registry uses one command identity for buttons, menus, context menus and
shortcuts. Dispatch is suppressed while editors and controls that own arrows or
space have focus; persisted overrides are conflict checked.

| Command | Current state | Proposed default | Capability gate |
|---|---|---|---|
| Open file | Working | `Ctrl+O` | File opening service |
| Open folder | Working | `Ctrl+Shift+O` | Folder/queue Q1 |
| Open URL | Working | `Ctrl+L` | URI opening service |
| Play/Pause | Working | `Space` | Playlist present |
| Stop | Working dock/menu action | No default | Playlist present |
| Seek backward / forward | Working | `Alt+Left` / `Alt+Right` | Relative seek |
| Previous / next | Working when valid | Keyboard focus and Enter; no default | Q1 valid neighbor |
| Mute | Working | `M` | Mute state/binding |
| Volume down / up | Working slider | Arrow keys while volume has focus | Volume state |
| Fullscreen | Working | `F11` | Window-state service |
| Playback speed | Working selector/menu | Keyboard focus and Enter; no default | Rate binding |
| Toggle queue | Working | `Ctrl+J` | Queue snapshot |
| Library | Working; Home adds sections, search and grid/list | `Ctrl+Shift+L`, `Alt+Home` | Local history document |
| Picture in picture | Working on Windows; compact same-window mode | `Ctrl+Shift+P`, Escape to return | Same attached engine/surface; multi-window PiP deferred |
| Subtitle/audio track menus | Working | Keyboard focus and Enter; no default | T1 |
| Settings | Working | `Ctrl+,` | Settings service |
| Media information | Working metadata and codec-stream drawer with refresh and redacted clipboard diagnostics. | `Ctrl+I` | M1 implemented and qualified on Windows; macOS/Linux native qualification open |
| Snapshot | Missing | `Shift+S` | V3 |

## Cross-repository prerequisite packages

Before the dependent UI package starts, implement each required VLCLR item in
the sibling `vlclr` repository on its own branch, validate it against the
pinned VLC source/SDK/runtime, commit it independently, then update the
recorded VLCLR pin before product integration. The minimum candidates are Q1,
Q2, T1--T4, V1--V3, A1, and M1 above. Mute and volume do not need a new VLCLR
binding, but still need product contracts. Play/pause, stop, and seek also need
the existing app-private native calls moved behind reviewed VLCLR APIs before
they are multiplied by UI work.

No native prerequisite is implemented by UI-00.

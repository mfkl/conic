# Modern player capability matrix

Status: UI-00 inventory with UI-06 verified product bindings at
`b3708f9`; pinned sibling VLCLR revision
`e90b786de74c6ed440dea8bf65329907739b0c60`.
This is the product contract for the work packages in
[UI_IMPLEMENTATION_PLAN.md](UI_IMPLEMENTATION_PLAN.md). A `Missing` row is a
reason to keep its control absent or disabled with an explanatory capability
state; it is never permission to ship a placeholder.

## Evidence and terms

`Product binding` means an engine-neutral contract, immutable snapshot, and
application command implemented in this repository. `VLCLR binding` means a
reviewed, ABI-validated sibling-repository API. `Existing VLCLR` is not enough
by itself: a product binding is still required before a control can work.

The current `IMediaEngine` has only `Open`, `TogglePause`, `Seek`,
`SetVolume`, and `Stop`. Its state has source, status, normalized position,
elapsed time, duration, volume, seekability, and error. `VLCCorePlayer`
implements those calls. It also has local private fixed/variadic native calls;
new app features must not extend that pattern and instead require reviewed
VLCLR coverage where noted.

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
| Open folder / enqueue folder | Missing. | Source enumeration policy, add-and-play/enqueue commands, error reporting. | Playlist item mutation/enumeration prerequisite. | Enumeration may be asynchronous; reject inaccessible files. | Deterministic enumeration/service and queue tests. | Daily-use |
| Open network URL | Missing: current `Open` calls `Path.GetFullPath`, so it does not accept URLs. | Validated URI opening service and user-facing errors. | Verify VLC internal startup accepts UTF-8 URI argv; no new binding expected for a single source. | Next open: current engine rebuilds VLC per media open. | URI parser/service test plus local network-media smoke. | Daily-use |
| Drag and drop | Missing. | Drop-to-open/enqueue application service and Avalonia routing. | Same queue prerequisite when enqueueing. | Live after validated source resolution. | UI interaction tests. | Daily-use |
| Recent media / resume | Missing. | Versioned settings/history and resume stores, privacy gates, commands. | None. | Persist after successful open; resume applies only on a later open. | Migration, privacy, duplicate-normalization, resume tests. | Daily-use |
| Play / pause | Working: one button calls `TogglePause`; status drives icon. | Command registry and `CanExecute` from snapshot. | Existing app-private `playlist_Control` call must be replaced by a VLCLR playlist-control abstraction before broad reuse. | Live while a playlist is available. | Contract tests plus engine smoke pause/resume. | Daily-use |
| Stop | Working: one button calls `Stop`. | Command registry and state-aware enablement. | Same reviewed playlist-control abstraction as play/pause. | Live; waits up to 10 seconds, then core is cleaned up. | Existing engine smoke stop/reopen. | Daily-use |
| Seek timeline | Working when VLC reports `can-seek`; pointer drag is protected. | Command registry; elapsed/duration snapshot remains sufficient. | Replace app-private `input_vaControl` with reviewed safe operation. | Live; normalized 0..1 and unavailable for unseekable media. | Existing engine smoke and headless endpoint/drag tests. | Daily-use |
| Short / long skip | Missing. | Relative-seek command with configurable intervals and bounds. | Reviewed relative-time input-control binding; current position read is not a setter. | Live only when seekable. | Contract bounds tests and engine smoke. | Daily-use |
| Volume | Working: slider sets 0..125%; snapshot refreshes it. | Command registry, remembered-volume setting. | Already has `PlaylistVolumeGet/Set`. | Live; persistence is next application startup/open policy. | Existing engine smoke plus setting tests. | Daily-use |
| Mute | Existing native only: VLCLR exposes `PlaylistMuteGet/Set`; UI/state has none. | `IAudio` mute command and immutable mute state. | None beyond existing `PlaylistMuteGet/Set`. | Live while playlist exists. | Binding test already exists; add product state/command and engine smoke. | Daily-use |
| Playback speed | Missing. | Rate command, effective-rate snapshot, default-speed setting. | Reviewed input variable/control binding for rate, including type/range and ownership. | Live per media; default applies next open. | Native rate smoke and bounds/state tests. | Daily-use |
| Previous / next | Visible but deliberately disabled; handlers are empty. | Queue snapshot and navigation commands. | Reviewed playlist navigation and item ownership/callback APIs. | Live only with a valid predecessor/successor. | Queue mutation/end-of-media smoke. | Daily-use |
| Fullscreen | Missing: smoke code sets `WindowState`, but no customer command/control exists. | Window-state service, fullscreen command, auto-hide controller. | None. | Live; controls must remain visible for menu, tooltip, drag, or focus. | UI keyboard/focus and fullscreen layout checks. | Daily-use |
| Always on top | Working checkbox directly sets `Topmost`. | Window-state service and persistent setting. | None. | Live; preference restored at next startup. | Window-service/settings tests. | Daily-use |
| Title / duration | Duration works in state; title is absent and no status label is to return. | Metadata snapshot and title fallback policy. | Reviewed input metadata read binding if VLC does not expose a stable variable. | Refresh per media/generation. | Media metadata test and media-change UI test. | Daily-use |

## Queue, tracks, and media adjustment controls

| Control / capability | Current support | Required product binding | Required VLCLR binding | Apply timing / constraints | Validation | Tier |
|---|---|---|---|---|---|---|
| Queue drawer, select, reorder, remove, clear | Missing. Plugin only lends a main-playlist pointer to the host; no queue model is exposed. | Generation-safe `IPlaylist`, immutable queue snapshots, dispatcher marshalling, mutation commands. | **VLCLR prerequisite Q1:** validated playlist item enumeration, add/remove/move/select/clear, navigation, callbacks, and explicit borrowed/held ownership rules. | Live; serialise mutations with playback and invalidate snapshots on generation change. | VLCLR ABI/layout tests, product queue tests, native reorder/remove smoke. | Daily-use |
| Add-and-play vs enqueue; playlist load/save | Missing. | Source service plus queue commands and playlist document parser/writer. | Q1; verify native playlist import/export feasibility before exposing it. | Load/save is explicit user action; keep I/O outside engine thread. | Parser/security tests and queue smoke. | Advanced |
| Repeat and shuffle | Missing, although VLCLR defines playlist-order/repeat constants only. | Queue mode commands and effective-mode snapshot. | **VLCLR prerequisite Q2:** reviewed setters/getters and change notification for playlist order/repeat. | Live; survives end-of-media and later reopen according to setting policy. | Native behavior smoke across end/reopen. | Daily-use |
| Audio tracks / subtitle tracks | Working selection surface: immutable copied snapshots, coherent playback drawer/context menu, and fresh VLC readback. Empty/loading/error/unavailable states are explicit. | `MediaCapabilitiesState`, generation-safe commands and dispatcher presentation. | T1 (`VLCMediaCapabilities`). | Refresh on input/media generation; selection intent is not treated as effective until readback. | Release media-capabilities smoke and stale-state UI tests. | Daily-use |
| Chapters | Working selection surface with title/chapter lists and empty state. | Same capability snapshot and selection commands. | T2 (`VLCMediaCapabilities`). | Refresh on media generation; selected chapter/title is read back from VLC. | Release media-capabilities smoke; fixture capabilities are reported as distinct Passed/Skipped phases. | Daily-use |
| External audio / subtitle file | Working as a validated **pending-next-open** attachment only. The UI never lists it as a live current track without a later fresh T1 snapshot. | Picker/action and explicit pending result language. | T3 (`VLCMediaCapabilities`). | Next open only; no Reopen/Apply-now claim. | Release media-capabilities smoke creates and cleans a unique temporary SRT. | Advanced |
| Audio / subtitle delay and reset | Working with direct signed millisecond entry, ±100 ms actions, and exact zero reset. | Bounded millisecond commands and effective delay snapshot. | T4 (`VLCMediaCapabilities`). | Live per current input after readback; product bound is ±10,000 ms, deliberately narrower than VLCLR's safety ceiling. | Release media-capabilities smoke and application/UI bounds tests. | Daily-use |
| Aspect ratio, crop, zoom | Missing. `VLCVoutDisplay` has change-query constants, not product controls. | Video-adjustment contract and effective values. | **VLCLR prerequisite V1:** reviewed input/vout controls with supported-choice discovery and lifecycle rules. | Usually live, but may recreate output; capability depends on active vout. | Native adjustment smoke on CPU and opaque paths. | Advanced |
| Deinterlace | Missing. | Video setting + media adjustment contract. | **VLCLR prerequisite V2:** reviewed startup/per-input control and available modes. | Mark NextOpen or Live only after native verification; do not infer. | Interlaced-fixture smoke. | Advanced |
| Snapshot | Missing. | Snapshot command, output-path service, result/error reporting. | **VLCLR prerequisite V3:** reviewed snapshot trigger and completion/failure path. | Live only with active video output. | File-output and failure smoke. | Advanced |
| Audio output device / normalization / passthrough | Missing. | Audio settings contract and capability result. | **VLCLR prerequisite A1:** reviewed audio-output device enumeration/selection and relevant variables/options. | Device selection likely NextOpen/Restart until proven live; startup options require reviewed argv construction. | Device-change smoke on target hardware. | Advanced |
| Media/codec information and diagnostics copy | Missing. | Immutable diagnostics/media-info snapshot and clipboard service. | **VLCLR prerequisite M1:** stable input/ES metadata read APIs as needed. | Refresh per input; diagnostics must not expose secrets. | Snapshot formatting and clipboard tests. | Advanced |

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
| Theme, density, control timeout, window state | Missing persistence; hard-coded dark brushes only. | Product settings service; theme/density/timeout are Live, window restoration is Restart/startup. | Daily-use |
| Autoplay, default speed, skip intervals, resume, volume, repeat | Missing persistence. | Settings service; skip intervals Live, default speed/volume/repeat apply on next relevant media open unless live engine state is changed too. | Daily-use |
| Hardware decode, video fit/default aspect, subtitle defaults, caching | Current engine supplies `--avcodec-hw=any` or `none`; no settings UI. | Reviewed, allowlisted VLC startup-option builder; classify each as NextOpen or Restart after smoke. | Advanced |
| Preferred language, encoding, font, outline, background | Missing. | Settings persistence plus track/rendering prerequisite; labels cannot imply rendered subtitle parity. | Advanced |
| Editable hotkeys | Missing; source has no keyboard shortcut handling. | Central command registry, conflict detector, persisted binding model, focus-aware dispatcher. | Advanced |
| Transcoding, broadcasting, capture authoring, plugin administration, expert variable tree | Not planned. | Explicitly excluded from this roadmap. | Non-goal |

## Command inventory and proposed defaults

No customer keyboard shortcuts currently exist. The following are proposed
product defaults for UI-01 onward, not statements of present behavior. The
registry must use one command identity for buttons, menus, context menus, and
shortcuts, suppress media commands while editing text, and report conflicts.

| Command | Current state | Proposed default | Capability gate |
|---|---|---|---|
| Open file | Working button only | `Ctrl+O` | File opening service |
| Open folder | Missing | `Ctrl+Shift+O` | Folder/queue Q1 |
| Open URL | Missing | `Ctrl+L` | URI opening service |
| Play/Pause | Working button only | `Space`, `MediaPlayPause` | Playlist present |
| Stop | Working button only | `S` | Playlist present |
| Seek backward / forward | Missing | `Alt+Left` / `Alt+Right` | Relative seek |
| Previous / next | Disabled placeholders | `MediaPreviousTrack` / `MediaNextTrack` | Q1 valid neighbor |
| Mute | Missing product binding | `M` | Mute state/binding |
| Volume down / up | Working slider only | `Down` / `Up` | Volume state |
| Fullscreen | Missing product binding | `F11` | Window-state service |
| Speed decrease / increase / reset | Missing | `[` / `]` / `=` | Rate binding |
| Toggle queue | Missing | `Ctrl+J` | Queue snapshot |
| Subtitle/audio track menus | Missing | No default in MVP | T1 |
| Settings | Missing | `Ctrl+,` | Settings service |
| Media information | Missing | `Ctrl+I` | M1 |
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

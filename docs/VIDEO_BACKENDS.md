# Video backend boundary

`CompositionVideoSurface` forwards playback commands and media events, owns its
frame timer, and attaches one `VideoBackendSession` to the visual tree. It no
longer selects a D3D11 adapter, imports native textures, synchronizes keyed
mutexes, or owns bitmaps/compositor resources.

`IVideoBackend` is the application-local boundary. It exposes initialization,
player creation, presentation, rendering, video size/geometry and diagnostic
notifications. Shared layout calculations remain in `VideoSurfaceLayout`.
Native device identifiers are diagnostic strings; native handles and adapter
LUIDs do not cross the interface.

`Video/Windows/D3D11VideoBackend` owns the existing VLC plugin bridge and Avalonia
compositor resources. It selects the compositor's adapter before creating the
matching `WindowsVlcCorePlayer`, imports shared textures and performs keyed-mutex
presentation. Decoder picture pools and GPU color conversion remain in the
Windows Native AOT vout plugin. Existing Windows compatibility fallback behavior
is preserved and is not a qualification path for future hardware backends.

## Attachment lifecycle

- A session initializes its backend once and captures audio startup arguments
  before awaiting device selection.
- Detaching disposes the session. An outstanding initialization cannot create
  a player afterward; a new attachment receives a separate backend/session.
- Engine notifications are posted through the originating session and discarded
  if it is disposed before delivery. This does not depend on an engine using
  itself as the event sender.
- Disposal stops the player before releasing the backend, even when player
  disposal throws. Repeated disposal does nothing.
- D3D11 import/presentation continuations check disposal and fallback state after
  awaits. They cannot report frames or update layout after their backend ends.
  Partial initialization and import failures release compositor resources.

These objects follow Avalonia's UI-thread lifecycle. Native callbacks must post
notifications through the session; this is not a general concurrent owner.
Detachment ends the playback session. Seamless window moves that retain media
state should retain the control's attachment, as the current PiP implementation
does; replacing an engine is not a queue/history migration mechanism.

## Windows regression evidence — 2026-09-11

The branch started from main `3896debed2204c867c529e8d1cd02f15eb797833` and uses
the unchanged framework/runtime pins in `eng/versions.json`.

- Debug and Release: 221 managed tests passed, including eight new session
  lifecycle cases; builds had zero warnings/errors. Release Native AOT publish
  passed. Logs: `artifacts/backend-debug-tests.log` and
  `artifacts/backend-release-tests.log`.
- H.264 hardware smoke: D3D11VA opaque pictures, zero CPU pictures/uploads.
  Engine lifecycle plus five stress cycles: 320 allocations and 320 releases,
  zero CPU uploads. Logs: `artifacts/backend-hardware.log` and
  `artifacts/backend-engine.log`.
- Actual Avalonia compositor: resize/fullscreen, reopen and three control
  detach/reattach cycles passed on Intel Iris Xe. Each new attachment created a
  new player and presented D3D11 frames on the same adapter. Log:
  `artifacts/backend-compositor.log`.
- PiP retained the same engine/generation and passed pause/seek, resize, Escape,
  fullscreen, Home/Settings and close checks. The existing adapter-mismatch smoke
  passed its compatibility fallback assertion. Logs: `artifacts/backend-pip.log`
  and `artifacts/backend-adapter-mismatch.log`.

The expanded compositor regression uses the existing `--ui-smoke` switch. The
other commands are `--smoke --expect-path=hardware-opaque-gpu`,
`--engine-smoke --cycles=5`, `--pip-smoke`, and `--adapter-mismatch-smoke`.
Use the pinned VLC runtime, an H.264 hardware fixture through
`VLC_AVALONIA_TEST_MEDIA`, and an isolated `VLC_AVALONIA_PROFILE`.
These are local Windows GPU results; hosted CI runs managed checks and publishing.

## Next native milestones

The product's native engine, D3D11 plugin bridge, texture probe and native smokes
now live in `Interop/Windows`. Windows DLL loading is selected by
`VideoBackendFactory.InitializeRuntime`; `Program` has no Windows P/Invoke.
`WindowsVlcCoreApi` contains the fixed-signature interface-startup export.
The unused product copy of Win64 `input_vaControl` seeking was removed; actual
seek/rate operations continue through VLCLR's guarded `VLCTransportControl`.
This isolation does not make those framework ABI declarations portable.

The factory still enables Windows x64 only. No Linux/macOS native playback or
CPU-based port is introduced by this boundary.

1. Qualify the pinned VLC 3 ABI on each target architecture. The dependency's
   `VLCTransportControl` uses Win64 `va_list` argument slots, and
   `VLCVideoPresentation` explicitly guards Windows x64. The product's vout and
   internal-core integration are also Windows-specific. Reusable fixes and
   native ABI probes belong in VLCLR, followed by a reviewed product pin update.
2. Start from VLCLR's `tests/AbiValidation/verify-vlc-abi.sh` and
   `vlclr_abi.cpp`, using headers built for the actual target. Existing layout
   assertions are an input to qualification, not proof that variadic calls or
   hardware surface ownership work on that target.
   `eng/verify-native-abi.ps1` now orchestrates the existing probe against the
   pinned source headers on all three CI platforms; `--graphics-probe` collects
   actual compositor capabilities on a target machine without starting VLC.
   See [the target qualification guide](TARGET_QUALIFICATION.md).
3. Keep the new Linux/macOS Desktop CI matrix passing as native work proceeds.
   It builds the app and runs 149 managed UI tests per configuration, without
   loading VLC or running a graphical session. This remains distinct from a
   native ABI probe or hardware test.
4. On the Linux/macOS GPU machines, implement decoder-surface ownership,
   conversion, synchronization and compositor import behind this boundary.
   Validate the hardware-only acceptance gates in `CROSS_PLATFORM.md` before
   changing `NativePlaybackSupport` or enabling a new native plugin RID.

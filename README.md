# Conic

An early modern Avalonia desktop UI for VLC 3. The Windows video path uses a
pure-C# Native AOT VLC `vout display` plugin and a keyed-mutex D3D11 shared
texture, allowing video and Avalonia controls to live in the same composition
tree without child-window airspace limitations.

The authoritative architecture boundary is recorded in
[`docs/rfc/0001-port-boundaries.md`](docs/rfc/0001-port-boundaries.md), and the
pinned native/framework inputs are recorded in [`eng/versions.json`](eng/versions.json).
The dispatch-ready Windows work packages, validation gates, and commit
boundaries are in [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md).

The active [VLC 4 UI fidelity plan](docs/VLC4_SCREEN_BY_SCREEN_PLAN.md) covers
the visual redesign in Avalonia while keeping VLCLR/VLC 3: Playback and the
Home/navigation shell, PiP and Settings are implemented and qualified on Windows.
The [integration report](docs/WINDOWS_UI_INTEGRATION_2026-09-10.md) records the
passing checks, captures and remaining visual/platform reviews.
It includes the local VLC master source map and visual acceptance criteria.
The [September 10 Playback review](docs/PLAYBACK_REVIEW_2026-09-10.md) records
the latest implementation, validation results, captures and review launcher.

## Current state

- Pure C#; no native C shim.
- VLC 3 internal-core playback through the sibling VLCLR repository.
- D3D11 shared-texture transport from the managed vout to Avalonia.
- Public Avalonia `ICompositionGpuInterop` import and
  `CompositionSurfaceVisual` presentation.
- Same-adapter D3D11 video-processor conversion from hardware-decoded NV12 or
  P010 into the shared BGRA composition texture, with zero CPU frame uploads
  on the validated hardware paths.
- CPU BGRA fallback when the active Avalonia backend cannot import D3D11
  keyed-mutex images.
- Cross-device and real-compositor smoke modes.
- An engine-neutral command/state contract with generation-safe snapshots.
- Working local/file-folder/URL opening, queue controls, tracks and timing,
  persistent settings, recent/resume history, and an optional no-scan library.

Windows qualification covers H.264/NV12 and HEVC Main 10/P010 hardware decode
on Intel Iris Xe, plus software-BGRA GPU upload and CPU-bitmap fallback. See
the composition document for measured evidence and explicit coverage limits.

## Repository layout

This first version intentionally uses a project reference to VLCLR:

```text
videolabs/
  vlclr/
  conic/
```

VLCLR and the pinned VLC 3 runtime remain in `../vlclr`. NuGet package
references can replace the project references later.

## Build and run

```powershell
dotnet publish src/Conic.Vout -c Release -r win-x64

Copy-Item `
  src/Conic.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll `
  ../vlclr/vlc-3.0.24-beta1/plugins/video_output/ `
  -Force

../vlclr/vlc-3.0.24-beta1/vlc-cache-gen.exe `
  ../vlclr/vlc-3.0.24-beta1/plugins

dotnet run --project src/Conic
```

Smoke tests use `../vlclr/test_01.mkv` by default:

```powershell
dotnet run --project src/Conic -- `
  --smoke --expect-path=hardware-opaque-gpu
dotnet run --project src/Conic -- `
  --smoke --expect-path=software-bgra-gpu-upload
dotnet run --project src/Conic -- `
  --smoke --expect-path=cpu-bitmap
dotnet run --project src/Conic -- --ui-smoke
dotnet run --project src/Conic -- --engine-smoke --cycles=25
```

Override local paths with `VLCLR_ROOT`, `VLC_DIRECTORY`, and
`CONIC_TEST_MEDIA`.

`VLCLR_ROOT` also selects the VLCLR project and native import library at build
time; use an absolute path when selecting a separate checkout. Use the VLC 3
framework/runtime pins in `eng/versions.json`; if the sibling checkout is on
unrelated work, select an isolated compatible checkout. VLC 4-targeting VLCLR
main is incompatible with this product. The earlier migration experiment is
preserved in [the compatibility record](docs/VLCLR_MAIN_MIGRATION.md); the active
UI plan keeps VLC 3 and requires fresh qualification of the selected inputs.

See [docs/D3D11_COMPOSITION.md](docs/D3D11_COMPOSITION.md) for the validated
implementation, qualification commands, measured counters, and remaining work.

## Using the player

- Start on Home to browse recent media, favorites and saved positions, with
  search and grid/list views. `Alt+Home` returns there during playback; the
  now-playing strip returns to the same playback session.
- Use **Picture in picture** in More or `Ctrl+Shift+P` for a compact always-on-top
  player. Escape restores the previous window and route; closing PiP exits VLC.
- Preferences uses a searchable full page with eight categories. Changes save
  automatically; Escape returns to Home or Playback. Advanced opens Media
  information, and Audio/Subtitles link to the current media controls.
- Open a file with `Ctrl+O`, a URL with `Ctrl+L`, the queue with `Ctrl+J`, the
  Library with `Ctrl+Shift+L`, and Settings with `Ctrl+,`.
- The Library is deliberately lightweight: it presents recent media, explicit
  favorites, and saved resume positions from the same local atomic history
  document. It never scans folders or disks. Add a favorite from the Library;
  use **Clear library** to remove favorites, recents, and all resume positions.
  Each recent, favorite, and saved-position entry also has its own keyboard-accessible remove action.
- Turning off **Remember recent media** or **Remember resume history** in
  Settings clears that respective history immediately. Favorites are explicit
  library entries and are removed by **Clear library** or individually.
- Keyboard focus returns to the invoking control after drawers and overlays
  close. The Appearance page includes **Reduce motion**, which keeps playback
  chrome visible and removes the nonessential play-button motion.

The accessible controls expose names and hints for playback, timeline and
volume adjustment, fullscreen, settings, queue, and Library actions. The dark
surface uses neutral white controls with a restrained orange playback accent.

## Qualification limits

UI layout is exercised at 1280 and 480 logical pixels, plus deterministic
100%, 125%, and 150% DIP-scale policy tests. The real Windows composition
smokes verify fullscreen, reopen, clean shutdown, and safe forced
adapter-mismatch CPU fallback. Physical multi-monitor/multi-GPU switching,
OS suspend/resume, and arbitrary display scaling cannot be simulated here and
remain unqualified. Subtitle/subpicture rendering is also unqualified: track
selection and delay do not claim visual subtitle presentation on this custom
video path.

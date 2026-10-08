# Conic

## Migration in brief

This project rebuilds VLC desktop playback workflows in C# and Avalonia while
retaining the VLC 3 engine through VLCLR. VLC 3.0.23 (December 2025) is the
pre-contest source baseline; later VLC 4 UI references guide the appearance.
This is a UI rewrite, with a deliberately smaller scope than the complete VLC
application.

The main technical challenge was presenting hardware-decoded video with
ordinary Avalonia controls layered over it. On Windows, a C# Native AOT VLC
output plugin converts decoded D3D11 surfaces into a shared texture imported
by Avalonia. Recorded Intel Iris Xe checks show hardware decoding with no CPU
video-frame uploads on that path. Queue, tracks, history/resume, preferences
and PiP use shared commands and state; native resources stay behind the
playback/backend boundary.

The most useful lesson so far: proving a native frame renders is only the
start. Surface ownership, stale callbacks, stop/reopen and package discovery
need explicit checks. The repository includes managed tests and native
smokes, now runnable against a self-contained Windows package outside the
source tree. Windows has implemented playback; macOS playback is implemented
and checked on CI's macOS runners, pending qualification on a real Mac; Linux
native playback remains unfinished.

AI-assisted implementation and validation were used. Total human effort has
not yet been measured. [Current evidence and remaining work](docs/contest/STATUS.md)
separate completed checks from release requirements.
[Before/after captures](docs/contest/COMPARISON.md) show the current Windows
result. Public downloads and final release qualification are still pending.

## Windows review package

Build a local portable ZIP with PowerShell 7 and the pinned official VLC 3.0.24
runtime, which `eng/get-vlc.ps1` downloads from VideoLAN and verifies against
the SHA-256 in `eng/versions.json`:

```powershell
./eng/package.ps1 -VlcDirectory (./eng/get-vlc.ps1)
./eng/test-package.ps1 -Archive <path-to-zip> -Media <path-to-h264-fixture> -Cycles 25
```

Each build creates a new folder under `artifacts/packages` with the ZIP,
checksums, manifest and logs. Package tests extract outside the repository and
run the included executable with isolated profiles and no development runtime
override. CI builds the same ZIP on pushes to `main` and attaches it to a draft
GitHub release for `v*` tags. [Quick start](docs/WINDOWS_QUICK_START.md) ·
[Dependency/distribution status](docs/contest/DEPENDENCIES.md)

An early modern Avalonia desktop UI for VLC 3. The Windows video path uses a
pure-C# Native AOT VLC `vout display` plugin and a keyed-mutex D3D11 shared
texture, allowing video and Avalonia controls to live in the same composition
tree without child-window airspace limitations.

The authoritative architecture boundary is recorded in
[`docs/rfc/0001-port-boundaries.md`](docs/rfc/0001-port-boundaries.md), and the
pinned native/framework inputs are recorded in [`eng/versions.json`](eng/versions.json).
The [CI workflow](https://github.com/mfkl/conic/actions/workflows/ci.yml)
runs Core tests across all three desktop OSes and full Windows build/test/AOT jobs.
The [CI guide](docs/CI.md) documents coverage.
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

| Platform | Playback | Hosted CI |
| --- | --- | --- |
| Windows x64 | D3D11 hardware decoding and GPU presentation implemented | Full solution tests in Debug/Release; Release Native AOT publish |
| Linux x64 | Engine runs on the distribution's VLC 3 (queue, transport, tracks, lifecycle); GPU video output still to be implemented | App build, managed UI tests, Core tests, and engine smokes against Ubuntu's VLC |
| macOS arm64/x64 | VideoToolbox decoding and IOSurface presentation implemented; real-Mac qualification pending ([TODO](docs/MACOS_HOST_TODO.md)) | App build, managed UI tests, Core tests; engine, IOSurface and window smokes against VLC 3.0.24 on arm64 and x64 runners |

Hardware decoding and GPU presentation are required for the Linux/macOS ports.
On Linux the engine is native, but the app still reports playback as
unavailable until the GPU video output exists.

- Pure C#; no native C shim.
- VLC 3 internal-core playback through [VLCLR](https://github.com/mfkl/vlclr/tree/3.x),
  a C# VLC plugin framework, included as a git submodule.
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

VLCLR, the C# framework for VLC plugins that the app and the Native AOT vout
are built on, is a git submodule at `external/vlclr`, tracking the `3.x` branch
of [mfkl/vlclr](https://github.com/mfkl/vlclr/tree/3.x). That branch also holds
the VLC 3 import library and the native layout probe. The pinned commit is
recorded in [`eng/versions.json`](eng/versions.json). Clone with submodules:

```
git clone --recursive https://github.com/mfkl/conic.git
# or, in an existing clone:
git submodule update --init
```

GitHub's "Download ZIP" leaves the submodule out; clone instead.

## Build and run

Windows x64 playback development requires PowerShell 7, .NET 10 (CI pins SDK
10.0.102), the Visual Studio C++ build tools and Windows SDK for Native AOT, and
the VLC 3 runtime pinned in `eng/versions.json`. `eng/get-vlc.ps1` downloads it
to `artifacts/dependencies/vlc-3.0.24`, where development runs find it.

```powershell
./eng/get-vlc.ps1
dotnet publish src/Conic.Vout -c Release -r win-x64

Copy-Item `
  src/Conic.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll `
  artifacts/dependencies/vlc-3.0.24/plugins/video_output/ `
  -Force

artifacts/dependencies/vlc-3.0.24/vlc-cache-gen.exe `
  artifacts/dependencies/vlc-3.0.24/plugins

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

Override local paths with `VLCLR_ROOT`, `VLC_DIRECTORY`, and `CONIC_TEST_MEDIA`.
Installed applications can discover VLC in an adjacent `vlc` directory without
either source checkout. `VLC_DIRECTORY` takes precedence and must point to the
directory containing the native VLC libraries. The default plugin location is
`plugins/video_output` beneath that directory.

On Linux x64, `eng/test-linux-engine.ps1 -Media <clip>` builds the plugin and
app and runs the engine smokes against the installed VLC 3 packages
(`libvlccore9`, `libvlc5`, `vlc-plugin-base`). The GPU video output, and so
playback in the app, is still unimplemented on Linux.

On macOS, `eng/get-vlc.ps1` downloads the pinned VLC 3.0.24 DMG (an installed
`/Applications/VLC.app` also works), `eng/test-macos.ps1 -Media <clip> [-Window]`
runs the engine, IOSurface and window smokes, and `eng/package-macos.ps1`
builds an ad hoc signed `Conic.app`. See
[the cross-platform status and qualification plan](docs/CROSS_PLATFORM.md).

`VLCLR_ROOT` only points development runs at a directory containing the default
smoke media (`test_01.mkv`); it no longer affects the build or the runtime. The app targets the VLC 3 internal ABI. The earlier VLC 4 migration
experiment is preserved in [the compatibility record](docs/VLCLR_MAIN_MIGRATION.md).

See [docs/D3D11_COMPOSITION.md](docs/D3D11_COMPOSITION.md) for the validated
implementation, qualification commands, measured counters, and remaining work.

## Tests and CI

The eighteen-job GitHub Actions matrix runs on pushes and pull requests:

- Core: 72 tests per configuration on Windows, Ubuntu and macOS.
- Hosted graphics diagnostics in Release jobs: launch the compositor probe and
  retain capability reports. Linux uses Xvfb; this does not qualify hardware playback.
- Native ABI: compile and execute VLCLR's layout probe against the pinned VLC
  source headers using the native Windows, Linux and macOS compilers.
- Desktop app: build the app and run 157 managed UI tests per configuration on
  Ubuntu and macOS.
- Full Windows: all managed tests per configuration, plus the Release
  Windows x64 Native AOT plugin publish.
- Linux engine: queue, capability and 25-cycle lifecycle smokes against
  Ubuntu's VLC 3 with a generated clip (dummy video output, software decoding).
- macOS arm64 and x64: engine, IOSurface video output and window smokes
  against the pinned VLC 3.0.24 with a generated clip.

All eight jobs passed for commit `f709051` in
[the initial successful hosted run](https://github.com/mfkl/conic/actions/runs/34602849422).
Run the same checks locally with PowerShell 7:

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release
./eng/test.ps1 -Scope Windows -Configuration Debug
./eng/test.ps1 -Scope Windows -Configuration Release
```

Core tests need neither VLCLR nor native VLC. Desktop tests do not load
native VLC or open a graphical session. Hosted runners do not qualify hardware
decoding or compositor behavior; those checks run separately on GPU machines.
See [CI setup](docs/CI.md) and [platform qualification](docs/CROSS_PLATFORM.md).
The [video backend guide](docs/VIDEO_BACKENDS.md) describes compositor ownership,
attachment lifecycle, and the remaining native work for Linux/macOS.
The [target qualification guide](docs/TARGET_QUALIFICATION.md) explains native
layout checks and `--graphics-probe`, which queries the compositor without VLC.

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
remain unqualified. Subtitles are rendered by VLC and drawn as an overlay above
the video, so they also show with hardware decoding. Text subtitles were
checked by hand on Windows; styled ASS and image subtitles are not yet
qualified.

## License

Conic is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by the Free
Software Foundation, either version 2 of the License, or (at your option) any
later version. It is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
FITNESS FOR A PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text.

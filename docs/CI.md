# Continuous integration

The [Build and test workflow](../.github/workflows/ci.yml) runs on pushes and pull
requests. It uses the .NET SDK from `eng/versions.json`, commit-pinned actions,
read-only repository permissions and cancels superseded runs on the same ref.

## Automatic coverage

The Core matrix runs Debug and Release tests on Windows Server 2022, Ubuntu
24.04 and macOS 15. It builds and tests `Player.Application.Tests` and
`Player.Engine.ContractTests`: currently 72 cases per matrix entry. Neither
project needs VLCLR, native VLC, a display server or a GPU. TRX results are
uploaded even when a test fails and retained for 14 days.

Core checks do not build the UI or native plugin, and do not qualify playback.

## Linux/macOS app coverage

Four Desktop jobs build the real Avalonia app and run all 157 managed UI tests
on Ubuntu 24.04 and macOS 15, in Debug and Release.
The `Desktop` script scope builds the UI test project and its app references;
it excludes the Windows-only Native AOT vout project. It does not load native
VLC or require a graphical session. These tests cover presentation models,
storage, layout policies and backend lifecycle, not rendered UI or playback.

The complete workflow has eighteen jobs, plus packaging and release jobs on
`main`, tags and manual runs.

## Hosted graphics diagnostics

Release app jobs also launch `--graphics-probe`: Ubuntu uses an Xvfb X11 display;
macOS and Windows use the runner desktop. No native VLC runtime is loaded.
`eng/test-graphics.ps1` enforces a 45-second process timeout, validates the JSON
and exit code, and uploads stdout/stderr, capability JSON and `result.json`.
Crashes, malformed/missing reports, timeouts and query errors fail the job.

Hosted diagnostics explicitly allow a valid `interop-unavailable` result. A
green job establishes that GUI startup and reporting work; it does not establish
GPU access or hardware decoding. `interop-advertised` also remains unqualified
for actual image import and playback. There is no `continue-on-error` bypass.
Linux coverage here is X11 through Xvfb, not a Wayland session.

## Native ABI coverage

Three Native ABI jobs compile and execute the VLCLR layout probe
(`external/vlclr/tests/AbiValidation/vlclr_abi.cpp`) against the exact VLC source revision in
`eng/versions.json`, using MSVC x64 on Windows and the host C++ compiler on
Linux/macOS. A sibling checkout of VLC, `vlc`, supplies the source headers.
Their artifacts include JSON provenance and compiler/probe logs, even on failure.

The layout probe checks static size/offset assertions and module ABI constants.
It does not qualify actual runtime calls, decoder surfaces or playback. See
[target qualification](TARGET_QUALIFICATION.md).

## Linux engine coverage

The Linux engine job installs Ubuntu 24.04's VLC 3 packages (`libvlccore9`,
`libvlc5`, `vlc-plugin-base`), generates a 30-second H.264/AAC clip with ffmpeg
and runs `eng/test-linux-engine.ps1`. That script publishes the Linux Native AOT
plugin (playlist control interface only), builds the app with the smoke harness
and runs the queue, media-capabilities, player-capabilities and 25-cycle engine
smokes against the real runtime. Logs are uploaded even on failure.

VLC renders to its `dummy` video output and decodes in software, so this job
qualifies queue, transport, tracks, delays, media information and lifecycle
on Linux, not hardware decoding or GPU presentation.

## macOS engine and video coverage

Two macOS jobs, arm64 on `macos-15` and x64 on `macos-15-intel`, download the
pinned VLC 3.0.24 DMG for their architecture with `eng/get-vlc.ps1` (SHA-256
from VideoLAN's published sums), generate the same clip with ffmpeg from
Homebrew and run `eng/test-macos.ps1` twice:

- headless: queue, media-capabilities and player-capabilities smokes, the
  IOSurface smoke (BGRA surfaces with hardware then software decoding, every
  lent pixel buffer returned, no CPU copies) and a 10-cycle engine smoke with
  the IOSurface video output;
- `-Window`, in the runner's desktop session: GPU and CPU-fallback video
  smokes with screen captures, compositor reattachment and PiP, then the UI
  smokes (Home, drawer, menus, playback controls, Stop, media information,
  thumbnails, accessibility and scale, Settings) and the engine and audio
  settings smokes against `eng/make-settings-fixtures.ps1`'s fixtures. Each
  case starts from its own profile.

The runners are virtual machines with a paravirtualized GPU; on arm64 VLC
selects VideoToolbox there. This qualifies the transport and lifecycle, not
playback quality, frame pacing or real hardware, which need a real Mac. Logs and captures are uploaded
even on failure.

`Package / macOS arm64` and `Package / macOS x64` run on `main`, tags and
manual runs: `eng/package-macos.ps1` builds an ad hoc signed
`Conic.app` with VLC bundled in `Contents/Frameworks/vlc` and uploads
the ZIP. They are not attached to releases until the bundle is qualified on a
real Mac.

## Full Windows coverage

The Windows matrix builds the complete solution and runs all 229
managed tests in Debug and Release. Release also publishes the Windows x64
Native AOT plugin. Test results and native outputs are retained for 14 days.

VLCLR is the public `external/vlclr` submodule, checked out with
`submodules: true`, so the workflow needs no secret or repository variable, and
pull requests from forks run every job. Artifact upload paths are relative to the runner workspace
and point into `conic/artifacts/ci`. Do not use `pull_request_target` to
execute untrusted PR code.

Hosted jobs do not run hardware-decoding or compositor smokes. Those require
qualified GPU machines and the pinned native VLC runtime, as described in
[CROSS_PLATFORM.md](CROSS_PLATFORM.md). Local Windows hardware results are recorded
there separately from CI coverage.

## Run the same checks locally

PowerShell 7 and .NET 10 are required. Restore is enabled by default; use
`-NoRestore` only after dependencies have been restored.

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release
./eng/test.ps1 -Scope Windows -Configuration Debug
./eng/test.ps1 -Scope Windows -Configuration Release
```

The scripts return failure when a build, test or native publish fails.
They write results under `artifacts/ci`. CI creates a job-local `global.json` to
enforce the exact SDK pin; local scripts use the installed SDK selection.

Local validation on Windows: actionlint 1.7.12 passed; the Core Release script
passed 72 tests; the full Windows script passed 213 tests in both Debug and
Release. Release built with zero warnings/errors and published the AOT plugin.
These runs used the installed SDK 10.0.303 and restored dependencies.

The first hosted run on 2026-09-11 passed both full Windows jobs and both Windows
Core jobs with the exact SDK 10.0.102 pin. Linux/macOS exposed a history test that
implicitly expected Windows filename comparison; the fixture now requests that
policy explicitly. Current hosted results are available in
[GitHub Actions](https://github.com/mfkl/conic/actions/workflows/ci.yml).

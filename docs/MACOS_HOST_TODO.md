# macOS host TODO: qualify on a real Mac

Branch `feature/macos-port` runs Conic on macOS, arm64 and x64: the
VLC 3 engine, a video output that hands VLC's IOSurface-backed CoreVideo
frames to Avalonia's compositor, and an app bundle. CI exercises all of it on
GitHub's macOS runners, which are virtual machines. This list is what only a
real Mac can confirm. Tick items as they pass and keep the evidence (logs,
screen captures, versions) under `artifacts/` with the machine details from step 1.

## How the video path works

1. The plugin's macOS output (`src/Conic.Vout/IOSurfaceVoutPlugin.cs`)
   asks VLC for `CVPB`, BGRA `CVPixelBuffer` pictures. VideoToolbox decodes to
   NV12 buffers (`420v`) and VLC's `cvpx` converter turns them into BGRA with
   `VTPixelTransferSession`, on the GPU. Software decoding is converted to BGRA
   and uploaded into a pixel buffer by the same module. VLC's pools are
   IOSurface-backed.
2. For each frame the output retains the pixel buffer and passes it, with its
   IOSurface, visible rectangle and display generation, to the app.
3. The app (`src/Conic/Video/MacOS/IOSurfaceVideoBackend.cs`) imports the
   IOSurface into Avalonia (`ICompositionGpuInterop`, `IOSurfaceRef`), caches
   up to 12 imports, and updates a `CompositionDrawingSurface` with two
   `MTLSharedEvent` timelines: "ready", signaled from the CPU because VLC only
   lends finished buffers, and "done", signaled by the compositor after its
   copy. Buffers go back to VLC once "done" passes their value.
4. Without IOSurface import (or with `--video-cpu-smoke`), the output asks for
   plain BGRA and the app draws CPU frames.

VLC 3 derives its own plugin directory from libvlccore only inside VLC.app's
executable, so the app sets `VLC_PLUGIN_PATH` (and `VLC_DATA_PATH`) to the
runtime's `plugins/` and `share/`.

## What CI already established (GitHub macOS runners)

| Check | arm64 (`macos-15`) | x64 (`macos-15-intel`) |
| --- | --- | --- |
| VLCLR layouts against the VLC 3.0.24 headers, including `vout_display_t`, the non-Windows `vout_display_cfg_t` and control numbering, `cvpxpic_ctx` | Passed | Not run (arm64 compiler only) |
| Variadic calls and `va_list`: `snprintf`/`vsnprintf` through `VLCVariadic` and `VLCVaList` | Passed (unit tests; arguments on the stack) | Exercised by the smokes below (System V) |
| Queue, media and player capability smokes against the pinned VLC 3.0.24 DMG | Passed | Passed |
| IOSurface smoke: BGRA surfaces, every lent buffer returned, no CPU copies | Passed, **VideoToolbox decoded** ("Using Video Toolbox to decode 'h264'") | Passed; the runner has no VideoToolbox hardware decoder, so VLC used avcodec |
| Engine smoke with the IOSurface output, 10 cycles | Passed | Passed |
| Window smokes: GPU video, CPU fallback, compositor reattachment | Passed | Passed |
| Real screen captures of the GPU path (`screencapture` during `--video-smoke --video-capture`) | Video upright, colors as on the CPU path, aspect and crop applied | Same |
| PiP smoke | **Fails:** see "PiP window size" below | **Fails:** same |

The runner is a VM with a paravirtualized GPU. Smoothness, frame pacing,
power use, real VideoToolbox hardware, display changes and multiple GPUs
cannot be judged there.

## 1. Record the machine

- [ ] Model and chip (`sysctl -n machdep.cpu.brand_string`), macOS version (`sw_vers`).
- [ ] GPUs (`system_profiler SPDisplaysDataType`): on Intel MacBook Pros, note both GPUs.
- [ ] Displays: built-in Retina, external, refresh rates (ProMotion).
- [ ] .NET SDK (`dotnet --version`, CI pins 10.0.102), PowerShell 7 (`pwsh --version`), Xcode command line tools (`xcode-select -p`).

## 2. Get the code and VLC

```sh
git clone --recursive https://github.com/mfkl/conic.git && cd conic
git switch feature/macos-port   # or main once merged
pwsh ./eng/get-vlc.ps1          # pinned VLC 3.0.24 DMG, SHA-256 checked; prints the lib directory
brew install ffmpeg             # only to generate test clips
```

An installed `/Applications/VLC.app` (3.0.x) is used when no other runtime is found.

## 3. Baseline

- [ ] `dotnet test conic.slnx -c Debug` passes.
- [ ] `pwsh ./eng/test-macos.ps1 -Media <clip>` passes (queue, capabilities, IOSurface, 10-cycle engine).
- [ ] The summary line says the hardware pass used the `videotoolbox` decoder.
- [ ] `pwsh ./eng/test-macos.ps1 -Media <clip> -Window` passes. PiP fails on the
  runners (see "PiP window size" below); check whether it does here.
  Look at `artifacts/macos/screen-video-gpu-*.png`: the video must be visible,
  upright, with correct colors (red/blue not swapped), filling the expected area.
- [ ] `./eng/verify-native-abi.ps1 -VlcSourceRoot <vlc checkout at the pinned commit>` passes on this machine.

## 4. Play real media

Run the app from source (`dotnet run --project src/Conic -c Release`) and,
for each clip, confirm in Activity Monitor that `VTDecoderXPCService` is busy
and CPU use stays low:

- [ ] H.264 1080p and 4K.
- [ ] HEVC Main and Main 10 (10-bit goes through 8-bit BGRA; HDR is not tone-mapped).
- [ ] VP9 / AV1 where the Mac decodes them in hardware (M3 and later for AV1).
- [ ] A software-only codec (e.g. MPEG-2 or VP8): plays through the same IOSurface path.
- [ ] Subtitles (text and image), aspect/crop/zoom from the Video drawer.
- [ ] Pause, seek, stop, reopen, queue next/previous, playback rate.
- [ ] Resize, fullscreen, PiP, moving the window between displays with different scale factors.
- [ ] Sleep/wake while playing; unplugging an external display while playing.
- [ ] Intel with two GPUs: the status line reports `compositor-device-match=true`; force the other GPU (an app that needs the discrete GPU) and keep playing.
- [ ] Leave a long file playing for 30 minutes: memory stays flat (IOSurface import cache and pixel-buffer returns).

## 5. Package

```sh
pwsh ./eng/package-macos.ps1 -VlcDirectory "$(pwsh ./eng/get-vlc.ps1)"
```

Or download `package-macos-arm64` / `package-macos-x64` from a CI run on
`main` or a manual run. The bundle is signed ad hoc and not notarized:

```sh
xattr -dr com.apple.quarantine "Conic.app"
```

- [ ] The app starts from `/Applications` and from a folder with spaces, with no repository and no environment variables.
- [ ] Without `-VlcDirectory`, the bundle uses `/Applications/VLC.app`.
- [ ] Home thumbnails appear for MP4/MOV files (Quick Look through `qlmanage`; MKV has no Quick Look thumbnail).

## 6. Decisions made (October 8)

- **Window chrome: macOS traffic lights.** On macOS the window keeps its
  system decorations (`WindowDecorations.Full`) with the client area extended
  under them; the in-app caption buttons are hidden and the top chrome and
  Home header start 78 px from the left, clear of the traffic lights
  (`Controls/PlatformChrome.cs`). PiP stays borderless, without traffic lights.
- **Shortcuts: Cmd.** Defaults written with Ctrl use Cmd on macOS
  (`PlayerShortcutPolicy.PlatformDefault`); Settings shows `Cmd` and accepts
  `Cmd`, `Command` or `Meta`.
- **Bundle identity:** `io.videolabs.conic`; no `.icns` icon yet.
- **Distribution:** ad hoc signing is fine for now; no notarization.
- **Minimum macOS version.** `Info.plist` sets none; .NET 10 and Avalonia 12 decide.

To check on the Mac:

- [ ] Traffic lights sit in the top chrome without overlapping the back button
  or the Home header, are vertically centered on the 44 px caption row, and
  close, minimize and zoom the window; dragging the top chrome moves it.
- [ ] Fullscreen (green button and the app's toggle) hides and restores the chrome.
- [ ] Entering and leaving PiP switches between borderless and decorated
  correctly (watch the PiP window size bug below).
- [ ] Cmd+O, Cmd+Shift+O, Cmd+L, Cmd+I, Cmd+J, Cmd+, and Cmd+Shift+P work and
  show as `Cmd+…` in Settings; Cmd+Q and Cmd+W behave as expected.

## 7. Known limits

- 10-bit and HDR content is converted to 8-bit BGRA.
- `MetalSharedEvent` device matching tries both byte orders of Avalonia's
  device LUID; on both runners it matched (`compositor-device-match=true` in
  the status line). Check it on a Mac with two GPUs.
- Without IOSurface import the app falls back to CPU frames for the whole
  playback session; it does not switch back.
- **PiP window size (open bug).** Entering PiP sometimes leaves the window
  with an absurd size, intermittently and on both runners:
  - arm64: the first PiP entry aborted the app. AppKit logged `CAMetalLayer
    ignoring invalid setDrawableSize width=100480.000000 height=1044.000000`,
    then Avalonia threw `Failed to create Skia render target surface` from
    `ServerCompositionTarget.Render` (unhandled, exit 134). In an earlier run
    the same entry worked.
  - x64: after the smoke's quick fullscreen, PiP, Home, Settings and Open URL
    transitions, the PiP window reported a client size of 480x2645370 (another
    run: 480x1316070) at the expected position, and no further frames were
    presented.

  `TogglePictureInPicture` sets `MinWidth`/`MinHeight`, `Width = 480`,
  `Height = 270`, `Topmost` and `Position` in one go; nothing in the app writes
  such sizes. macOS applies frame changes and fullscreen exits asynchronously,
  so this looks like a race with Avalonia.Native. First item to reproduce on a
  real Mac (enter PiP from a normal and from a fullscreen window), then decide
  whether to defer the size and position until the window reports its new
  state, or to report it to Avalonia.

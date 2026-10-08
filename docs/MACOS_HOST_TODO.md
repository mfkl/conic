# macOS host TODO: qualify on a real Mac

Branch `feature/macos-port` runs VLC Avalonia on macOS, arm64 and x64: the
VLC 3 engine, a video output that hands VLC's IOSurface-backed CoreVideo
frames to Avalonia's compositor, and an app bundle. CI exercises all of it on
GitHub's macOS runners, which are virtual machines. This list is what only a
real Mac can confirm. Tick items as they pass and keep the evidence (logs,
screen captures, versions) under `artifacts/` with the machine details from step 1.

## How the video path works

1. The plugin's macOS output (`src/VLCAvalonia.Vout/IOSurfaceVoutPlugin.cs`)
   asks VLC for `CVPB`, BGRA `CVPixelBuffer` pictures. VideoToolbox decodes to
   NV12 buffers (`420v`) and VLC's `cvpx` converter turns them into BGRA with
   `VTPixelTransferSession`, on the GPU. Software decoding is converted to BGRA
   and uploaded into a pixel buffer by the same module. VLC's pools are
   IOSurface-backed.
2. For each frame the output retains the pixel buffer and passes it, with its
   IOSurface, visible rectangle and display generation, to the app.
3. The app (`src/VLCAvalonia/Video/MacOS/IOSurfaceVideoBackend.cs`) imports the
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

## Real Mac results (October 8)

MacBook Pro, Apple M1 Pro (14-core GPU), macOS 14.5 (23F79), built-in
Liquid Retina XDR display only. .NET SDK 10.0.201 (not the pinned 10.0.102),
PowerShell 7.6.6, Xcode 15 tools, pinned VLC 3.0.24 from `get-vlc.ps1`.
Clips: CI's `testsrc2` H.264/AAC MKV and a 720p H.264/AAC MP4.

- Every headless and window case passes on both clips, with VideoToolbox
  decoding and `compositor-device-match=true`. A first run made while the
  machine was saturated (Homebrew compiling ffmpeg) timed out in the queue and
  capability cases; they passed once it was idle.
- Screen captures need Screen Recording permission for the terminal that runs
  `test-macos.ps1`; without it they only show the wallpaper. With it, video
  is upright, colors match the CPU path, and aspect and crop apply.
- PiP passed 20 consecutive `--pip-smoke` runs, with no `setDrawableSize`
  warning: the PiP window size bug has not reproduced on real hardware.
- Fixed during the pass: four shortcut tests expected `Ctrl` defaults on
  macOS; the traffic lights sat in AppKit's 28 pt title bar instead of the
  caption row (see section 6); the player's "Ready to play" state showed
  through Home's translucent panel; `openMedia` button labels were not
  centered.
- VLC's own translations leak into the Video drawer: on a French system the
  default aspect ratio and crop read "Prédéfini(e)".

Later the same day: a real click on the player's back arrow did nothing,
because the enlarged title bar container spanned the whole window; it now
spans the traffic lights only. The menu bar, app name, Dock icon and
auto-hiding traffic lights were added (section 6).

Next on this Mac: the hand checks in section 6 (traffic light clicks, drag,
live resize, green-button fullscreen, Cmd shortcuts), the real-media list in
section 4, packaging (section 5) and the ABI check against a VLC checkout.
CI should also run `dotnet test` on macOS, which would have caught the
shortcut tests.

## 1. Record the machine

- [x] Model and chip (`sysctl -n machdep.cpu.brand_string`), macOS version (`sw_vers`).
- [x] GPUs (`system_profiler SPDisplaysDataType`): on Intel MacBook Pros, note both GPUs.
- [x] Displays: built-in Retina, external, refresh rates (ProMotion).
- [x] .NET SDK (`dotnet --version`, CI pins 10.0.102), PowerShell 7 (`pwsh --version`), Xcode command line tools (`xcode-select -p`).

## 2. Get the code and VLC

```sh
git clone --recursive https://github.com/mfkl/vlc-avalonia.git && cd vlc-avalonia
git switch feature/macos-port   # or main once merged
pwsh ./eng/get-vlc.ps1          # pinned VLC 3.0.24 DMG, SHA-256 checked; prints the lib directory
brew install ffmpeg             # only to generate test clips
```

An installed `/Applications/VLC.app` (3.0.x) is used when no other runtime is found.

## 3. Baseline

- [x] `dotnet test vlc-avalonia.slnx -c Debug` passes.
- [x] `pwsh ./eng/test-macos.ps1 -Media <clip>` passes (queue, capabilities, IOSurface, 10-cycle engine).
- [x] The summary line says the hardware pass used the `videotoolbox` decoder.
- [x] `pwsh ./eng/test-macos.ps1 -Media <clip> -Window` passes. PiP fails on the
  runners (see "PiP window size" below); check whether it does here.
  Look at `artifacts/macos/screen-video-gpu-*.png`: the video must be visible,
  upright, with correct colors (red/blue not swapped), filling the expected area.
- [ ] `./eng/verify-native-abi.ps1 -VlcSourceRoot <vlc checkout at the pinned commit>` passes on this machine.

## 4. Play real media

Run the app from source (`dotnet run --project src/VLCAvalonia -c Release`) and,
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
xattr -dr com.apple.quarantine "VLC Avalonia.app"
```

- [ ] The app starts from `/Applications` and from a folder with spaces, with no repository and no environment variables.
- [ ] Without `-VlcDirectory`, the bundle uses `/Applications/VLC.app`.
- [ ] Home thumbnails appear for MP4/MOV files (Quick Look through `qlmanage`; MKV has no Quick Look thumbnail).

## 6. Decisions made (October 8)

- **Window chrome: macOS traffic lights.** On macOS the window keeps its
  system decorations (`WindowDecorations.Full`) with the client area extended
  under them; the in-app caption buttons are hidden and the top chrome and
  Home header start 92 px from the left, clear of the traffic lights
  (`Controls/PlatformChrome.cs`). Avalonia 12 leaves the traffic lights in
  AppKit's 28 pt title bar whatever the title bar height hint, so
  `Interop/MacOS/MacWindowButtons.cs` resizes the title bar container and
  centers them on the visible caption row (30 pt down on the player, 26 pt on
  Home), 20 pt from the left, after every resize, state, activation and Home
  change. The container only spans the three buttons: the window server
  drags the window from anywhere in it, so a full-width container swallowed
  real clicks on the back arrow (events the app posts to itself skip that
  check). Its `_NSTitlebarDecorationView` is hidden, since its top-edge
  highlight would end in a notch. The traffic lights hide and show with the
  player's auto-hiding chrome and stay on Home. PiP stays borderless, without
  traffic lights.
- **Menu bar.** The app menu is named "VLC Avalonia" (`Application.Name`) and
  holds Settings… (Cmd+,); File, Playback, View and Window menus run the same
  registry commands as the in-app chrome (`MainWindow.MacMenu.cs`). Only
  shortcuts that use Command become key equivalents: AppKit runs those before
  the window sees the key, so Space, M or Alt+arrows there would bypass the
  editor focus rules. View also has Enter Full Screen (Ctrl+Cmd+F); Window
  has Minimize (Cmd+M), Zoom and Close Window (Cmd+W).
- **Home back arrow** ("Return to player") is hidden while there is nothing
  to return to, instead of showing disabled next to the traffic lights.
- **Shortcuts: Cmd.** Defaults written with Ctrl use Cmd on macOS
  (`PlayerShortcutPolicy.PlatformDefault`); Settings shows `Cmd` and accepts
  `Cmd`, `Command` or `Meta`.
- **Bundle identity:** `io.videolabs.vlc-avalonia`. The icon is the cone on a
  dark rounded square in Apple's icon grid: `eng/macos/AppIcon.svg`, rendered
  with AppKit into `eng/macos/AppIcon.icns` (bundle) and
  `Assets/vlc-avalonia-macos.png` (Dock icon when run from source).
- **Distribution:** ad hoc signing is fine for now; no notarization.
- **Minimum macOS version.** `Info.plist` sets none; .NET 10 and Avalonia 12 decide.

`pwsh ./eng/test-macos.ps1 -Window` includes `--caption-click-smoke`: it
posts AppKit clicks to both back arrows and the options button after the
playing chrome hides, checks the traffic lights hide and return with it, and
that Cmd+J (menu key equivalent and window shortcut) opens the queue once.
It cannot reproduce the window server's title bar drag, so real clicks on the
caption row stay a manual check.

To check on the Mac:

- [ ] Traffic lights sit in the top chrome without overlapping the back button
  or the Home header, are vertically centered on the 44 px caption row, and
  close, minimize and zoom the window; dragging the top chrome moves it.
  Position checked in screen captures of the player and Home; clicking,
  dragging and live resizing still to check by hand.
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

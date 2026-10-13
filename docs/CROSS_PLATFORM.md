# Desktop portability — 2026-09-11

Hardware decoding and GPU presentation are requirements for Windows, Linux and
macOS. CPU decoding, GPU upload of software-decoded frames, and CPU bitmap
presentation do not qualify a platform. Enabling `--avcodec-hw=any` alone is not
evidence that hardware decoding occurred.

## macOS port, October 8

Branch `feature/macos-port` runs VLC Avalonia on macOS arm64 and x64 with a
GPU video path. CI runs it on GitHub's `macos-15` (arm64) and
`macos-15-intel` (x64) runners, and it was qualified on an M1 Pro Mac.

- **ABI.** Apple arm64 passes every variadic argument on the stack in its own
  8-byte slot. `vlc_set` (module registration), `vlc_Log` and
  `playlist_Control` therefore go through `VLCVariadic`, which pads the
  fixed-signature call up to x7; a `va_list` is a plain pointer to the slots,
  as on Win64. macOS x64 follows System V like Linux. VLC 3's
  `vout_display_cfg_t` has a leading `is_fullscreen` flag and its display
  control enumeration two extra queries on Windows only;
  `VLCVoutDisplayControls` reads both layouts, and the ABI probe now checks
  `vout_display_t`, those offsets and values, and `cvpxpic_ctx`.
- **Runtime.** The pinned VLC 3.0.24 DMG for the architecture
  (`eng/get-vlc.ps1`), a bundle's `Contents/Frameworks/vlc`, or an installed
  `/Applications/VLC.app`. VLC 3 derives its plugin directory from libvlccore
  only inside VLC.app's own executable (`src/darwin/dirs.c`), so the app sets
  `VLC_PLUGIN_PATH` and `VLC_DATA_PATH`. VLCLR binds to the loaded libraries
  with `VLCCore.UseLibraries`; the plugin binds to the core that loaded it
  with `VLCCore.UseLibraryContaining(vlc_set)`.
- **Video.** The plugin's macOS output asks for BGRA CoreVideo pixel buffers
  (`CVPB`): VideoToolbox output converted by VLC's `cvpx` module
  (`VTPixelTransferSession`), or software decoding uploaded by it. It lends
  each frame's IOSurface to the app, which imports it through Avalonia's
  `ICompositionGpuInterop` (`IOSurfaceRef`) and synchronizes with two
  `MTLSharedEvent` timelines before returning the buffer. CPU BGRA frames are
  the fallback when import is unavailable. Subtitle, CPU-frame and geometry
  exports now live in `VoutHost`, shared with the D3D11 output.
- **App.** Home thumbnails come from Quick Look (`qlmanage`). The window uses
  the macOS traffic lights instead of the in-app caption buttons, and default
  shortcuts use Cmd instead of Ctrl. The menu bar is built once and its key
  equivalents updated in place: Avalonia's exporter throws when a window is
  given a second `NativeMenu`.
- **Settings.** VLC on macOS uses its `securetransport` TLS module, so the
  GnuTLS options (Verify certificates, Cipher preference) are hidden there,
  like the Windows-only proxy, screensaver and audio device rows. VLC's GPU
  picture filters (`ci_filters`) need VLC.app's OpenGL output, so the picture
  adjustments and the deinterlacer convert VideoToolbox frames to I420 and run
  on the CPU. The engine and audio settings smokes run on macOS: the screen is
  sampled with `screencapture`; DirectSound, MMDevice, Direct3D 11, the
  display request and the DPAPI proxy password stay Windows cases.

The software-decoding pass uploads frames into pixel buffers on the CPU; it
checks the transport, not the hardware requirement above, which needs
VideoToolbox on a real Mac.

## Linux engine port, October 8

Branch `feature/linux-port` runs the VLC 3 engine natively on Linux x64. It was
built and checked in WSL2 (Ubuntu 24.04, VLC 3.0.20 distribution packages, .NET
10.0.112). WSL has no usable GPU without the D3D12 translation layer, so
everything that needs GPU output was deliberately left out.

What changed:

- VLCLR accepts Windows x64 and Linux x64 (`VLCPlatform`). Variadic
  `input_Control` queries go through `input_vaControl` with a `va_list` built in
  managed code (`VLCVaList`): a pointer to 8-byte slots on Win64, a System V
  `__va_list_tag` whose register areas are marked consumed on Linux. No native
  helper library is needed. `playlist_Control` has no `va_list` export; its
  integer and pointer arguments use the same registers either way.
- `VLCCore.Free` uses the C runtime VLC allocated with: msvcrt on Windows,
  the process C library on Linux.
- The plugin is split. `ControlInterface` (the playlist host) builds for both
  RIDs; the D3D11 video output builds only for `win-x64`. The Linux `.so`
  exports both `vlc_entry__3_0_0f` and `vlc_entry__3_0_0ft64`, the suffix
  Debian/Ubuntu use since their 64-bit `time_t` transition (identical ABI on
  64-bit targets).
- The app's core player (`VlcCorePlayer`) and runtime loading are portable.
  On Linux the runtime locator falls back to the distribution's VLC
  (`/usr/lib/x86_64-linux-gnu`), the loader binds VLCLR's `libvlccore`/`libvlc`
  imports to the versioned sonames and adds the plugin directory to
  `VLC_PLUGIN_PATH` through libc.
- `NativeEngineSupport` (engine: Windows/Linux x64) is separate from
  `NativePlaybackSupport` (video playback: Windows x64 only). The Linux app
  still shows "Linux hardware playback is not implemented yet."
- Headless smokes are split into `EngineSmoke` (portable) and the Windows-only
  D3D11 checks. On Linux they use VLC's `dummy` video output and software
  decoding. There is no software rendering path.

Evidence, WSL2 on this machine:

- Queue, media-capabilities, player-capabilities (including `INPUT_GET_VOUTS`
  through the new `va_list`) and the 25-cycle engine lifecycle passed against
  VLC 3.0.20. Seek (double argument), rate (int and int*) and position
  (double*) all crossed the System V `va_list`.
- 242 managed tests passed on Linux, including `VLCVaListTests`, which hands a
  `VLCVaList` to glibc's own `vsnprintf`. The same test passes against msvcrt.
- The layout probe passed with g++ 13.3 (x86_64-linux-gnu) against the pinned
  VLC headers (`6de05adc`).
- Windows did not regress: all managed tests and the 14-case package test
  (hardware, software upload and CPU paths, 25 engine cycles with zero CPU
  uploads, compositor, PiP, settings, time zones, broken installs) passed.

Found on the way: with `--avcodec-hw=any`, the distribution VLC (even the
stock `cvlc`) hangs at input teardown in WSL, where libva/VDPAU have no real
GPU. Engine-only smokes therefore request software decoding; hardware decoding
belongs to the GPU backend's qualification on real Linux hardware.

Still to do, on native Linux with a GPU:

1. A Linux `vout display` in the plugin: VA-API opaque surfaces, exported as
   DMA-BUF (`vaExportSurfaceHandle`), imported into Avalonia's compositor
   (`ICompositionGpuInterop`, Vulkan or EGL), with subtitles through the
   existing `SubtitleComposer` and `subpicture_chromas` path.
2. A `IVideoBackend` for it, then enable `NativePlaybackSupport` for Linux.
3. Qualify X11 and Wayland separately (CI's Linux job runs X11 under Xvfb only).
4. Decide the shipped runtime: the distribution's VLC 3 (what this branch uses)
   or a bundled 3.0.24 build, and the package format.

## Implemented from Windows

- The shared video control delegates native transport and rendering to an
  attachment-scoped backend. The Windows implementation owns adapter selection,
  D3D11 import/synchronization and fallback resources. Session ownership prevents
  player startup and queued notifications after detachment. See
  [the backend boundary and regression evidence](VIDEO_BACKENDS.md).
- Runtime discovery no longer initializes repository paths as a prerequisite
  for installed playback. Explicit `VLC_DIRECTORY` takes precedence over
  app-local libraries and the development sibling checkout. Invalid explicit
  overrides fail instead of silently selecting a different runtime.
- Discovery recognizes Windows DLLs, Linux `.so` / VLC 3 sonames and macOS
  `.dylib` / versioned names. macOS bundles may place their runtime under
  `Contents/Frameworks/vlc`. These are filesystem rules, not native-load tests.
- Plugin discovery uses the target OS and process architecture for development
  output paths. Installed plugins belong under the selected runtime's
  `plugins/video_output` directory so the bridge and VLC load the same module.
- Native playback checks the implemented Windows x64 backend before constructing
  the bridge or loading native code. The existing video plugin rejects attempts
  to build it for another RID; it still implements D3D11 only.
- Recent media, favorites, resume positions and thumbnail identities preserve
  path case on Linux/macOS. On insensitive macOS volumes differently spelled
  aliases can remain separate; distinct files on sensitive volumes cannot merge.
  Windows retains its existing insensitive comparison policy. Thumbnail cache
  keys were versioned so old entries cannot carry the previous collision forward.
- Shortcut overrides accept `Cmd`, `Command` and `Meta`, preserve the modifier
  through save/load, and receive Avalonia's Meta modifier during key dispatch.
  Platform-default shortcuts and tooltip labels still need a macOS pass.
- Unix settings expose automatic audio output rather than Windows module names;
  an imported Windows choice is not sent to VLC on Unix. Explicit Unix module
  and device choices await validation of the pinned runtimes.

No Linux/macOS native backend was added and existing Windows decoder/fallback
behavior was not changed. Windows fallback diagnostics do not count toward the
hardware requirement above.

## Remaining hardware work

| Platform | Decoder / surface integration to implement and qualify |
| --- | --- |
| Windows x64 | Existing D3D11VA opaque picture pool, GPU video-processor conversion and keyed-mutex import. Retest lifecycle whenever shared integration changes. |
| Linux | Qualify VLC 3 hardware decoding on the selected GPU/driver, starting with VA-API. Implement ownership and GPU import of those decoded surfaces into the active Avalonia backend. Cover X11 and Wayland separately. Other GPU/decoder combinations require their own coverage. |
| macOS | Implemented on `feature/macos-port` (VideoToolbox, `cvpx` conversion, IOSurface import with Metal shared events). Qualified on an M1 Pro; Intel and dual-GPU Macs are not yet checked by hand. |

VLC 3 includes [VideoToolbox and VA-API decoding](https://images.videolan.org/vlc/releases/3.0.7.html).
Avalonia describes [external GPU resource import](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering),
including Vulkan images and macOS IOSurfaces. These establish candidate APIs;
they do not prove the VLC 3 surface/compositor combination works in this app.

The backend boundary must own decoder device selection, native surface lifetime,
GPU color conversion, synchronization and compositor import. D3D11 handles and
adapter LUIDs must stay in the Windows implementation. Preserve shared command,
queue, state and UI contracts; do not expose native surface pointers to view models.

Before enabling either new platform, validate the exact pinned VLC/VLCLR ABI on
its target architecture: layouts, exported symbols, callback conventions,
variadic calls and `va_list`. The current seek implementation and vout configuration
offsets explicitly assume Win64. Fix reusable native bindings/probes in VLCLR,
then update the product's dependency pin; renaming libraries cannot fix the ABI.

Build native plugins on their target OS. Native AOT does not support
[cross-OS compilation](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/compiling.md).
Also qualify native dependency resolution, plugin discovery/cache generation,
installed layouts, macOS signing/notarization, and Linux distribution libraries.

## Acceptance on the Linux/macOS machines

1. Build the managed app/tests against the pinned VLCLR checkout. Start with the
   UI project and test projects, not a publish of the Windows-only vout project.
2. Build and probe the matching native runtime/plugin. Record OS, architecture,
   VLC/VLCLR commits, GPU/driver and compositor backend.
3. Require observed hardware decoder selection, hardware-backed picture types,
   actual compositor-presented frames, and no CPU frame uploads/readbacks in the
   qualified hardware transport. Test H.264 and HEVC 10-bit where supported.
4. Exercise open, play, pause, seek, stop, reopen, resize, fullscreen and PiP.
   Check resource release and stale-frame rejection after device/surface changes.
5. Verify subtitle composition, cropping/aspect, color handling, display scaling,
   multiple monitors, suspend/resume and device loss. An unsupported hardware
   path must be reported explicitly and cannot pass via software playback.
6. Run installed applications outside either source checkout. Confirm native
   libraries/plugins resolve and no development paths are required.

The engine smokes (`--engine-smoke`, `--queue-smoke`, `--media-capabilities-smoke`,
`--player-capabilities-smoke`) also run on Linux x64; video smokes stay
Windows-only until a Linux GPU backend exists. Filename-policy tests run on Windows
with filesystem fixtures; they must not be presented as Linux/macOS execution.

## Windows validation

Use the framework commit in `eng/versions.json` in a clean sibling checkout.
For the current portability work this is `../vlclr-vlc3-portability`; the
ordinary `../vlclr` working directory may be on the VLC 4 line.

```powershell
./eng/test.ps1 -Scope Windows -Configuration Release -FrameworkRoot ../vlclr-vlc3-portability
```

The results below record the original portability validation against framework
commit `50d849200c3dfb2b047e20a174ae66b56a19adcd`. For subsequent native-control
calling-convention coverage, see [TARGET_QUALIFICATION.md](TARGET_QUALIFICATION.md).

The empty `UsedAvaloniaProducts` property skips build telemetry, whose user-profile
log location is not writable in the restricted workspace. It does not skip compilation
or tests. Release results: 213 tests passed (64 application, 8 engine contract,
141 UI), including 20 new portability cases. Logs: `artifacts/crossplatform-tests.log`.

Release solution build passed with no warnings/errors. The final UI suite also
passed after the last locator changes (`artifacts/crossplatform-ui-tests-final.log`).
The D3D11 project explicitly rejected `linux-x64` and `osx-arm64` through its
backend guard; those checks are not cross-platform compilation results.

Native H.264 smoke passed with opaque `DX11` pictures, zero CPU pictures/uploads,
and balanced allocation/release (40/40). A framework-dependent publish was then
run from a temporary directory outside the repository, with `VLCLR_ROOT` pointing
to a nonexistent checkout and an explicit `VLC_DIRECTORY`; the same hardware
smoke passed. Logs: `artifacts/crossplatform-h264.log` and
`artifacts/crossplatform-installed.log`. This validates the Windows native
transport and installed discovery, not Linux/macOS or a new compositor implementation.
Queue regression passed (`artifacts/crossplatform-queue.log`). The first hardware
attempt stalled waiting for the playlist and used the repository's MPEG-4 fixture;
hardware qualification used the documented generated H.264 clip instead.

Follow-up Windows regression checks also passed:

- Debug: all 213 managed tests, zero failures/skips (`artifacts/windows-recheck-debug-tests.log`).
- Windows x64 Native AOT plugin publish (`artifacts/windows-recheck-aot.log`).
- Hardware engine lifecycle: pause, seek, resume, volume, mute, skip, rate,
  stop/reopen/dispose and five stress cycles. Zero CPU pictures/uploads and
  balanced release, 320/320 (`artifacts/windows-recheck-engine.log`).
- Actual Avalonia GPU compositor: resize/fullscreen and reopen recreated the
  resource/device generation on the same adapter and presented D3D11 frames
  (`artifacts/windows-recheck-compositor.log`).

## CI status

The initial GitHub Actions check on 2026-09-11 found zero workflows/runs.
The new [CI workflow](CI.md) adds automatic Core tests on Windows/Linux/macOS
and full Windows build/test/AOT checks. The pinned VLCLR commit and read-only
dependency access are configured, and both full Windows jobs passed their first
hosted run. The results above are local Windows validation; hosted Core tests
and Windows builds do not qualify Linux/macOS hardware playback.

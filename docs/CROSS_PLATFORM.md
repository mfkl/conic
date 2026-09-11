# Desktop portability — 2026-09-11

Hardware decoding and GPU presentation are requirements for Windows, Linux and
macOS. CPU decoding, GPU upload of software-decoded frames, and CPU bitmap
presentation do not qualify a platform. Enabling `--avcodec-hw=any` alone is not
evidence that hardware decoding occurred.

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
| macOS | Qualify VideoToolbox hardware sessions and decoded surfaces on Apple Silicon, plus Intel if supported. Implement GPU conversion/import and surface synchronization with the active Avalonia compositor. |

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

Native smoke commands remain Windows-only until the corresponding hardware
backend and smoke implementation exist. Filename-policy tests run on Windows
with filesystem fixtures; they must not be presented as Linux/macOS execution.

## Windows validation

Use the framework commit in `eng/versions.json`; the currently available sibling
checkout targets different work. The compatible local checkout is
`artifacts/dependencies/vlclr-codec-info` at
`50d849200c3dfb2b047e20a174ae66b56a19adcd`.

```powershell
$env:VLCLR_ROOT = (Resolve-Path artifacts/dependencies/vlclr-codec-info).Path
dotnet test conic.slnx -c Release --no-restore -p:UsedAvaloniaProducts=
```

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

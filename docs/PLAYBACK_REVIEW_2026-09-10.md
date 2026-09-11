# Playback implementation and review — 2026-09-10

Status: F0 complete; Playback fidelity pass implemented and qualified on the
current Windows machine. Gate P2 accepted on 2026-09-10 after the user viewed the
running app and said “ok great commit it”. Home and Settings redesigns have not
started. Acceptance preserves the documented capability and qualification limits.

## What changed

- Added semantic playback colors from VLC master `1a853315`: VLC orange
  `#FF8800`, neutral charcoal surfaces, restrained hover and selection colors.
- Moved the time labels above a full-width timeline. Removed the unused bottom
  row in wide mode while retaining the compact volume row.
- Replaced plain skip labels with directional icons and interval labels;
  simplified secondary actions and disabled transport styling; made More a
  vertical ellipsis. Keyboard focus remains visible without leaving a permanent
  orange mouse-focus background.
- Gave More and playback drawers neutral surfaces and smaller corner radii.
  Drawers render above transport chrome. Selected drawer tabs, queue items and
  tracks use the shared selection treatment. More uses the available window
  height and remains scrollable on short windows.
- Fixed playback speed display to follow effective engine readback, including
  changes made outside the selector. Readback does not issue a second command.
- Fixed a real resize regression at 899 DIPs: row reassignment during arrange
  needed a deferred measure to grow the dock and contain the volume controls.
- Fixed composition and adapter-mismatch smoke setup to actually open the
  configured fixture. They had been waiting for video in an empty player.
- Expanded the existing interaction smoke with endpoint, centered-transport,
  full-width timeline, speed-readback, 900/899 breakpoint and 480x360 checks,
  and additional state captures. Added `--playback-review` to open the configured
  fixture in a normal interactive window without exiting after a test.

## Reproducible inputs

The unrelated sibling checkouts were preserved. The framework was extracted
with `git archive` from the existing pin; the VLC runtime was copied into ignored
workspace artifacts before installing the newly published plugin there.

| Input | Selected value |
|---|---|
| VLCLR | `e90b786de74c6ed440dea8bf65329907739b0c60` |
| Framework root | `artifacts/dependencies/vlclr-e90b786` |
| Runtime | `artifacts/dependencies/vlc-3.0.24-beta1`; DLL file version `3.0.24-beta1`, module ABI `3_0_0f` |
| Import library | `lib/libvlccore.lib` from the archived framework pin |
| Native smoke fixture | Existing `../vlclr/test_01.mkv`; SHA-256 `C3F37E9DD4740F82019D2E58A9AF58FA318BCC0B4C392FBD831A4FA40EE315A2` |
| Interactive capture fixture | Generated 120-second H.264 1280x720 `testsrc2` pattern; no third-party media frame |
| UI reference | User-supplied screenshots plus source at local VLC master `1a8533156079724db7b63dc6cb73675c76458b14` |

For builds and native checks from the repository root:

```powershell
$env:VLCLR_ROOT = Join-Path $PWD 'artifacts/dependencies/vlclr-e90b786'
$env:VLC_DIRECTORY = Join-Path $PWD 'artifacts/dependencies/vlc-3.0.24-beta1'
$env:CONIC_TEST_MEDIA = (Resolve-Path ../vlclr/test_01.mkv).Path

dotnet build conic.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=
dotnet test conic.slnx -c Release --no-build --no-restore -m:1
# Repeat build/test with -c Debug.
dotnet publish src/Conic.Vout -c Release -r win-x64 --no-restore
```

Initial restore/build needed network access for NuGet. Subsequent builds use the
restored packages. The empty `UsedAvaloniaProducts` override skips the build
telemetry target, which otherwise attempts writes outside the workspace. It
does not disable application compilation or test execution.

## Results

Logs are under ignored `artifacts/playback-validation/`. Native processes had
bounded execution. These results distinguish implemented capabilities from
explicitly skipped functionality.

| Check | Result |
|---|---|
| Debug / Release solution builds | Passed, final builds zero warnings/errors |
| Debug / Release managed tests | 156 passed in each configuration: 55 application, 8 engine contract, 93 UI; zero failed/skipped |
| Native AOT plugin publish and isolated cache regeneration | Passed; publish reported the four existing VLCLR CA1420 marshalling warnings |
| `--playback-controls-smoke` | Passed; sliders, seeking, volume endpoints, effective speed, title alignment, menu dismissal, focus/inactivity, fullscreen captures, breakpoint and compact containment |
| `--layout-smoke` | Passed, including existing menu, queue, library, settings and confirmation layouts at wide/compact widths |
| `--smoke --expect-path=hardware-opaque-gpu` | Passed; H.264/DX11, zero CPU uploads, balanced picture release |
| `--smoke --expect-path=software-bgra-gpu-upload` | Passed |
| `--smoke --expect-path=cpu-bitmap` | Passed |
| `--queue-smoke --queue-smoke-verbose` | Passed: add, clear, reorder, select, remove, previous/next, repeat/shuffle and recovery |
| `--engine-smoke --cycles=5` | Passed: transport, seek, volume readback at 42/150/200%, five stress open/stop cycles and balanced release |
| `--media-capabilities-smoke` | Six phases passed: tracks, chapters, audio/subtitle delay, next-open attachment and completion |
| `--player-capabilities-smoke` | Three phases passed, three explicitly skipped: video adjustments, deinterlace, snapshot |
| `--ui-smoke` | Passed: actual D3D11 compositor presentation, fullscreen/resize and reopen with new device/resource generations |
| `--adapter-mismatch-smoke` | Passed: unavailable adapter correctly selected CPU presentation |
| Interactive `--playback-review` | Launched with generated media; window capture shows native video and Avalonia controls together |

## Review artifacts and remaining differences

- [State contact sheet](../artifacts/playback-review/index.html)
- [Wide Playback](../artifacts/playback-review/playback-controls-wide.png)
- [480x360 Playback](../artifacts/playback-review/playback-controls-compact.png)
- [Actual native video and controls](../artifacts/playback-review/playback-native-window.png)
- [Local interactive launcher](../artifacts/playback-review/launch-review.ps1)

The state contact sheet uses simulated transport snapshots over a black canvas;
it is presentation evidence, not native playback evidence. The separate native
window capture uses the generated fixture. Before captures are retained locally.
The original VLC screenshots are still untracked and were not copied into code.

The native Windows caption currently follows the system and appears light on
this machine. It remains a visible difference from VLC 4's integrated black
caption. The title stays beside the cone and the bookmark control stays omitted,
as requested in the earlier review. Compact mode retains two transport rows.
Queue/media controls remain an overlay; Settings and Home still use the existing
screens until their planned redesigns.

Subtitle/subpicture rendering, aspect/crop/zoom, deinterlace, snapshots and codec
stream details remain unqualified or unavailable as recorded in the capability
matrix. The visual capture matrix is not exhaustive: audio-only artwork and
every loading/error/track variation still need targeted review. Native HDR,
physical multi-GPU switching, suspend/resume, and macOS/Linux are not qualified
by this pass. DPI policy tests cover 100/125/150%; this session did not switch
Windows through each physical scale setting.

Next: prepare the detailed Home/navigation specification for Gate H1. Keep media
information available through More, Info and `Ctrl+I`; full codec/stream details
remain future VLCLR/VLC 3 capability work.

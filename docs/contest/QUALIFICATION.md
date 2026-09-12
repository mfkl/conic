# Windows package qualification — September 12, 2026

**Passed:** 229 managed tests in both Debug and Release, Native AOT publish,
and all 12 extracted-package checks below. This is local Windows evidence,
not a clean-OS, broad hardware, subtitle or cross-platform qualification.

| Input | Recorded value |
|---|---|
| Product | Working-tree implementation on planning base `839404d`; exact staged binaries hashed in the package manifest |
| Framework | VLCLR `09b46d50848cc0abe3c236e7a25e43679ee13c02`, clean tracked checkout |
| Runtime | VLC 3.0.24-beta1; source pin `5fbc6904a2e4a96e9956a5d8503ac4db4ebaf702`; binary/source equivalence not independently established |
| Toolchain | Local .NET SDK 10.0.303; CI SDK pin 10.0.102 was not installed |
| Machine | Windows 10 build 19045, x64; Intel Iris Xe, D3D11VA; driver version not recorded |
| Media | Generated 120-second H.264 1280x720 test pattern; SHA-256 `1970cdcaeadf496c3ad6b5f1458a8b609212c06c1d686ba5c053930338cd3c35` |
| Package | `VLC-Avalonia-0.1.0-preview.1-win-x64.zip`, 161,205,791 bytes |
| Archive SHA-256 | `01eb36da6237554eb298c0591ced407a99221415389ffa08f1738c9281de0922` |

## Checks against the extracted ZIP

The runner verifies staged-file hashes, extracts into a temporary directory
containing spaces and `é`, and starts the included EXE. `VLC_DIRECTORY` and
`VLC_PLUGIN_PATH` are removed; `VLCLR_ROOT` and `DOTNET_ROOT` point to nonexistent
paths. Profiles and review captures go to isolated writable directories. This
demonstrates bundled resolution on the existing developer machine, not absence
of every machine-wide dependency on a fresh OS.

| Check | Result |
|---|---|
| Hardware decode / GPU transport | Passed; opaque DX11 frames, zero CPU video uploads |
| Software decode / GPU upload | Passed; actual software-upload path observed |
| CPU bitmap fallback | Passed; actual bitmap path observed |
| Engine lifecycle, 25 stress cycles | Passed; 1,120 allocations / 1,120 releases, zero CPU pictures/uploads |
| Queue | Passed native queue operations and teardown |
| Avalonia compositor | Passed resize/fullscreen, reopen and detach/reattach |
| Home/Settings playback continuity | Passed same-session advancing playback |
| PiP | Passed 51 GPU frames, pause/seek, resize, Escape, fullscreen, Home/Settings and close |
| Settings | Passed existing category, compact layout, focus and persistence smoke |
| Video adjustments | Passed existing GPU geometry smoke |
| Missing runtime override | Passed exit 2 with actionable startup diagnostic |
| Missing packaged plugin | Passed exit 2; runner restores the renamed file afterward |

The normal startup-error window was also opened and visually inspected;
[capture](images/startup-failure.png). Actual Home and playback windows were
captured from the extracted application, including visible native GPU video.

Initial extraction exposed stale VLC plugin-cache entries caused by timestamp
rounding. Packaging now normalizes plugin timestamps to ZIP-representable
precision before cache generation. The final runner rejects stale-cache
warnings; all final cases passed that check. Extraction in another time zone
has not been qualified.

## Reproduction and retained evidence

```powershell
./eng/test.ps1 -Scope Windows -Configuration Debug -FrameworkRoot ../vlclr-vlc3-portability
./eng/test.ps1 -Scope Windows -Configuration Release -FrameworkRoot ../vlclr-vlc3-portability
./eng/package.ps1 -FrameworkRoot ../vlclr-vlc3-portability -VlcDirectory artifacts/dependencies/vlc-3.0.24-beta1
./eng/test-package.ps1 -Archive <generated-zip> -Media artifacts/playback-review/generated-review.mp4 -Cycles 25
```

Local detailed logs: `artifacts/contest-final-{debug,release}.log`, package
build directory `artifacts/packages/20260912-102208-f81727b1`, and native report,
logs and UI captures under
`artifacts/package-tests/07714092829c426d93e609ed22960b5f`.
These ignored local artifacts are not public downloads. The committed
[evidence summary](evidence/windows-package.json) records the final case results
and key native counters. The release build was clean of compiler errors;
framework/AOT builds retain the four existing VLCLR CA1420 marshalling warnings.

Outstanding: subtitle composition, HEVC/P010 checks on this exact package,
other GPUs, clean OS/non-admin/read-only installation, physical monitor and
sleep/recovery behavior, screen reader review, a 60-minute soak, external beta,
public source/distribution readiness, and a final release from a clean revision.
This local preview does not satisfy the complete contest release gate.

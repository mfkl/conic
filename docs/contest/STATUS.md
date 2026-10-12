# Contest delivery status

Updated October 8, 2026. Deadline October 23, 23:59 UTC; internal target
October 22. Windows is the shipping target. Linux has a native engine port on
branch `feature/linux-port`; its GPU video output will be built and tested by
the maintainer on native Linux. macOS runs with VideoToolbox decoding and
IOSurface presentation on branch `feature/macos-port`, checked on CI's macOS
runners; the maintainer qualifies it on a real Mac. No public release or
contest entry has been made; the maintainer makes the repository public at the
end.

| Task | State | Evidence / next action |
|---|---|---|
| W01 baseline and scope | Passed | VLC 3.0.23 pre-contest baseline; Windows-first scope, Linux engine added October 8 |
| W02 source and license | Passed | VLCLR is the public `external/vlclr` submodule (mfkl/vlclr `3.x`); both GPL-2.0-or-later (October 8) |
| W02 VLC runtime | Passed | `eng/get-vlc.ps1` fetches official VLC 3.0.24 win64 with a pinned SHA-256 |
| W02 requalification on 3.0.24 | Passed locally | 14/14 package cases, 25 engine cycles, zero CPU uploads on Intel Iris Xe (October 8); rerun on `feature/linux-port` also 14/14 |
| W02 public smoke media | Outstanding | `test_01.mkv` still comes from the maintainer's VLCLR checkout; Linux CI now generates its clip with ffmpeg, Windows could too |
| W02 fresh-clone build | Not tested | Build from a fresh clone using only the README |
| W03 Windows package | Passed locally | CI builds the portable ZIP on `main` and tags; draft release still holds the stale preview.1 ZIP |
| W03 paths with spaces/Unicode | Passed locally | Package test installs to `%TEMP%\VLC Avalonia é <guid>` outside the checkout |
| W04 subtitles | Partial | VLC-rendered subtitles overlaid on hardware and CPU paths; text subtitles checked by hand. Styled ASS, image subtitles and live external attachment not qualified |
| W05 portable native controls | Passed for Linux x64 (branch) | Managed `va_list` for System V; real VLC 3.0.20 in WSL2 passes queue, capabilities and 25 engine cycles |
| W06 Linux engine | Passed in WSL2 (branch) | See [CROSS_PLATFORM.md](../CROSS_PLATFORM.md#linux-engine-port-october-8); new CI job runs it against Ubuntu's VLC |
| W06 Linux GPU video output | Not started | VA-API to DMA-BUF to Avalonia import; needs native Linux with a GPU (maintainer) |
| W07 macOS | Implemented, CI-checked (branch) | arm64 and x64 runners pass engine, IOSurface (VideoToolbox on arm64) and video/compositor window smokes against VLC 3.0.24; qualified on an M1 Pro (unit tests, every window smoke with VideoToolbox, ABI probe) |
| W08 daily-use polish | In progress | Fixed: blinking track buttons, Home cards recreated every poll, resume prompt now has Cancel and closes after 8 s. Known: no Home thumbnail for HEVC without Microsoft's HEVC extension |
| W09 other desktop packages | Not started | Depends on the Linux GPU backend |
| W10 Windows qualification | Passed locally | [Package matrix](QUALIFICATION.md); additional GPU, clean OS, soak and external testers not done |
| W11 write-up and demo | Partial | README narrative drafted; demo video and release notes outstanding (cross-platform items) |
| W12 publication/submission | Outstanding | Maintainer publishes the repository and release |

## Scope of the migration

| VLC desktop workflow | Avalonia implementation |
|---|---|
| Open and control media | Files/folders/URLs, drag/drop, transport, timeline, volume and fullscreen |
| Manage playback | Queue navigation/reorder, repeat/shuffle, track/timing controls and media information |
| Return to media | Recent/favorites/resume with explicit local storage and privacy controls |
| Configure and multitask | Searchable settings, keyboard shortcuts and same-window PiP |
| Full VLC feature set | Not claimed: no complete indexing, conversion/capture, device administration, styled/image subtitle parity |

VLC 4 references inform the UI appearance; the engine remains pinned VLC 3.

## Short work journal

- September 12: Windows baseline; portable packaging, startup failure recovery,
  external capture paths and package qualification.
- September 22: smoke harness kept out of product builds; integrated dark
  title bar, app icon and unified vector icons.
- October 7: VLCLR moved into the repository; GPL-2.0-or-later; official
  VLC 3.0.24 runtime; CI-built Windows ZIP and draft release; fixed polled-state
  churn (track buttons, Home cards); subtitles over hardware-decoded video;
  resume prompt Cancel and auto-close; smoke routing fix.
- October 8: full 3.0.24 hardware requalification.
- October 8: Linux engine port on `feature/linux-port`, verified in WSL2.
- October 8: macOS port on `feature/macos-port`: VLCLR on Apple arm64 and
  macOS x64, IOSurface video output, app bundle script, macOS CI jobs.

Next decisions: tag a new Windows preview so the draft release matches `main`;
merge the Linux engine branch; choose whether the Linux GPU backend can be
qualified in time for a three-platform entry or whether the entry ships Windows
with a documented Linux engine port.

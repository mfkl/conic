# Windows UI integration qualification — 2026-09-10

Implementation tip: `0afa192` on `feature/modern-player-ui`. Home (`98ed837`),
picture in picture (`9963754`) and Settings (`0afa192`) have each been implemented,
validated and committed using the repository's configured Martin Finkel identity.
Playback was previously accepted by the user; visual acceptance of the new
screens remains pending. No push or merge was performed.

The engine remains pinned to VLCLR `e90b786de74c6ed440dea8bf65329907739b0c60`
and VLC `3.0.24-beta1`, ABI `3_0_0f`. Builds use the isolated framework under
`artifacts/dependencies/vlclr-e90b786`; native checks use the isolated runtime
under `artifacts/dependencies/vlc-3.0.24-beta1`. Unrelated sibling checkouts were
not switched or edited. Exact setup and fixture details are recorded in the
[Playback review](PLAYBACK_REVIEW_2026-09-10.md).

## Results

| Check | Result |
|---|---|
| Debug and Release builds/tests | Passed; 165 tests in each configuration: 55 application, 8 engine contract, 102 UI; no failed/skipped managed tests |
| Native AOT plugin publish | Passed; copied into isolated VLC and regenerated plugin cache |
| Hardware opaque GPU path | Passed; DX11, zero CPU uploads, balanced release |
| Software BGRA GPU upload | Passed; actual upload path observed |
| CPU bitmap | Passed; actual bitmap frames observed |
| Queue and five engine lifecycle cycles | Passed, including recovery, seek, amplified volume readback and balanced native releases |
| Media capabilities | Six phases passed; external-track attachment is next-open dispatch evidence, not live rendering qualification |
| Player capabilities | Three phases passed, three explicitly skipped: video adjustments, deinterlacing and snapshots |
| Avalonia compositor | Passed fullscreen/resize and reopen with new resource/device generations |
| Forced unavailable adapter | Passed safe CPU fallback and actual frame presentation |
| Home / Settings session | Passed same engine reference/generation and advancing playback through routes |
| PiP | Passed same session, actual Space/pause/seek, minimum size, bounds, maximized/fullscreen, topmost preferences, route restoration and app close |
| Settings | Eight categories at 1280x720 and 480x360; editors, search, reset, focus, Media information and Escape passed |
| Playback/layout regressions | Passed after Settings integration, including 900/899 breakpoint, timeline, volume, menu, queue, library and confirmations |

Clean dependency builds and Native AOT publish retain four pre-existing VLCLR
CA1420 marshalling warnings. Final incremental Release build had no warnings or
errors. Build/test/publish logs are `artifacts/integration-*.log`; native final
matrix logs are `artifacts/playback-validation/final-*.log`. Playback/layout
checks on this implementation are `settings-player.log` and `settings-layout.log`.
Smoke profiles use `VLC_AVALONIA_PROFILE` under ignored artifacts.

New window behavior uses Avalonia APIs. Source/project inspection found no
platform imports or Avalonia dependency in Player.Contracts or Player.Application;
both continue to target plain `net10.0`. This is a boundary audit, not a claim
that native video has been qualified outside Windows.

## Review and next work

The local [combined review](../artifacts/ui-review/index.html) links the Home,
PiP, Settings and Playback captures. The PiP native-window image contains actual
video from an owned generated test pattern; managed screenshots of video controls
do not capture the external GPU texture. Compact captures use logical client
dimensions; the native PiP capture was taken at the machine's 150% display scale.

1. Review Home, PiP and Settings visually and record the user's feedback/acceptance.
   The white native Windows caption remains a known difference from VLC's
   integrated black caption. PiP currently replaces the main window temporarily.
2. M1 follow-up: add reviewed VLC 3/VLCLR codec and stream-detail bindings while
   keeping More → Media information, `Ctrl+I` and Settings → Advanced consistent.
   Current metadata/diagnostics work; codec streams are explicitly unavailable.
3. Qualify subtitle rendering, video adjustments/deinterlace/snapshot prerequisites,
   audio device changes, physical display-scale transitions, multi-GPU and
   suspend/resume on suitable hardware. Finish manual accessibility review and
   targeted audio-only/loading/error visual states.
4. macOS/Linux require their own native presentation paths, runtimes and runners.
   They remain unqualified; Windows results do not imply platform parity.

No native capability is marked complete solely because its UI is present.

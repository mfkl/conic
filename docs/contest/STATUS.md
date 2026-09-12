# Contest delivery status

Updated September 12, 2026. Execution scope: Windows and local submission work,
per the maintainer. macOS/Linux implementation is deferred for this pass.
Owner of the implementation work below: Codex; release/source decisions and
human effort figures: maintainer. No public release or contest entry has been made.

| Task | State | Evidence / next action |
|---|---|---|
| W01 current Windows baseline | Passed locally | 221 existing tests and AOT publish; source pin `09b46d5`, actual SDK 10.0.303; hosted CI at this branch revision not checked |
| W01 migration baseline | Passed | VLC tag `3.0.23`, commit `578d28f6c9f2379164516e689418f92ac74a3445`, dated 2025-12-19; installed reference executable reports 3.0.23 |
| W01 status tracker | Passed | This document; external capacity/hardware not assumed |
| W02 source/dependency inventory | Partial | [Dependencies](DEPENDENCIES.md); full GPL v2 text added to repository LICENSE |
| W02 public source/distribution route | Blocked | VLCLR remains private and has no root license at the reviewed pin; maintainer must resolve source access/terms before public distribution |
| W03 portable Windows build | Implemented | [package.ps1](../../eng/package.ps1): self-contained app, native plugin, VLC, cache, manifest, hashes and ZIP |
| W03 launch instructions | Passed | [Quick start](../WINDOWS_QUICK_START.md) |
| W03 installed-package verification | Passed locally | All 12 [package checks](QUALIFICATION.md), outside checkout with spaces/Unicode and isolated profiles; fresh OS/non-admin/read-only installation still untested |
| W04 subtitles | Outstanding | Existing track/delay controls do not establish visual rendering; native code still does not compose GPU subpictures |
| W05–W07 portable controls/backends | Deferred | No Linux/macOS implementation in this pass |
| W08 broken installation handling | Implemented and unit-tested | Missing/inaccessible/incompatible playback files show an actionable startup window; smoke commands exit 2 with diagnostics |
| W08 packaged UI smoke paths | Implemented | `CONIC_ARTIFACTS` lets existing UI captures run without a repository checkout |
| W09 other desktop packages | Deferred | Windows package only |
| W10 current managed tests | Passed locally | 229 tests in each of Debug and Release, including 8 startup recovery tests; AOT publish passed |
| W10 native package matrix | Passed locally | [Exact package and evidence](QUALIFICATION.md); 25 lifecycle cycles, 1,120 balanced native allocations/releases, zero CPU video uploads on hardware path |
| W10 physical hardware/soak/beta | Not tested | Additional GPU, clean OS, screen reader, sleep/monitor transitions, 60-minute soak and external testers remain |
| W11 concise migration narrative | Drafted | README; actual human effort is still unmeasured |
| W11 comparison images/demo | Partial | [Native Home/playback pairs](COMPARISON.md) captured and inspected; other comparisons and final demo remain outstanding |
| W12 publication/submission | Outstanding | Depends on runnable final package, source delivery, qualification and final materials |

## Scope of the migration

| VLC desktop workflow | Avalonia implementation |
|---|---|
| Open and control media | Files/folders/URLs, drag/drop, transport, timeline, volume and fullscreen |
| Manage playback | Queue navigation/reorder, repeat/shuffle, track/timing controls and media information |
| Return to media | Recent/favorites/resume with explicit local storage and privacy controls |
| Configure and multitask | Searchable settings, keyboard shortcuts and same-window PiP |
| Full VLC feature set | Not claimed: no complete indexing, conversion/capture, device administration or subtitle rendering parity |

VLC 4 references inform the UI appearance; the engine remains pinned VLC 3.
The selected original tag predates the contest; the local checkout was inspected
with `git show`, without modifying it. The installed reference executable is a
separate input from the pinned beta runtime used by this app.

## Short work journal

- September 12: ran the current Windows baseline; implemented portable packaging,
  startup failure recovery, external capture paths and package qualification.
- Extracted-ZIP checks exposed stale plugin-cache timestamps. Packaging now
  normalizes plugin times to ZIP-representable precision before cache generation.
- Added a concise README narrative and release-input inventory. Publication,
  source licensing, subtitle rendering and broader physical qualification remain
  separate unfinished work. No total human-hours estimate is inferred from commits.

Next decision: resolve the VLCLR distribution route before preparing a public
download. Next engineering milestone after package qualification: actual subtitle
composition and its visual/native fixture matrix (W04).

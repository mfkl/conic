# Avalonia Port Challenge delivery plan

Prepared: 2026-09-12. Planning baseline: product `124bea99f53c4296e63ac8526703b827d5d89ea4`, branched from `feature/vlc3-native-controls`.

Objective: maximize the chance of a prize by shipping a dependable VLC desktop UI rewrite with demonstrable Avalonia engineering, useful migration lessons, and working desktop releases. Winning is uncertain; this plan defines deliverables we can control.

Execution started on this branch on September 12, scoped by the maintainer to Windows and locally achievable submission work. **Status, October 8:** Windows is feature-complete for the entry and requalified on VLC 3.0.24; the Linux engine runs natively on branch `feature/linux-port` (verified in WSL2), and its GPU video output is next, built and tested by the maintainer on native Linux; macOS is not started. [Current task status](contest/STATUS.md) records evidence and remaining gates. Unchecked items are still outstanding, including partially completed tasks. Nothing has been published or submitted.

## 1. Contest requirements and positioning

The [official challenge announcement](https://avaloniaui.net/blog/avalonia-port-challenge), checked September 12, sets the deadline at **October 23, 2026, 23:59 UTC**. Our internal submission target is **October 22, 18:00 UTC**.

The open-source entry needs a public OSI-licensed repository, downloadable builds for each target, before/after screenshots, and a migration write-up in the README. The original application must predate the contest. Teams may have up to three members; AI-assisted work is permitted. Judges assess migration difficulty/completeness, application quality, cross-platform implementation, engineering, and the write-up. Best Cross-Platform Port emphasizes Windows/macOS/Linux; Best Everyday Tool emphasizes everyday usefulness and polish. These are contest requirements; the technical gates and dates below are our own delivery decisions. [Source](https://avaloniaui.net/blog/avalonia-port-challenge)

Recommended positioning: **VLC desktop workflows rebuilt in Avalonia, retaining VLC 3 playback and integrating video into Avalonia composition.** Aim for a category prize with a result strong enough to contend for the grand prize. Prioritize Cross-Platform Port if all three native releases pass; Everyday Tool remains a plausible fit for a polished narrower release. Category fit is an assessment, not a prediction of the judges' decision.

State the exact scope: VLC 3 supplies the engine; VLC desktop behavior is the migration subject; VLC 4 UI references inform the presentation. Do not imply a full VLC 4 engine port or complete VLC feature parity. The recognizable project and native integration can attract attention, but the entry must demonstrate a useful completed migration.

## 2. Baseline and evidence limits

| Area | Evidence at this baseline | Work needed |
|---|---|---|
| Windows video | D3D11 backend and Native AOT vout; 14-case package qualification on VLC 3.0.24 (October 8), Intel Iris Xe | Additional GPU vendor, clean machine, soak |
| Linux | Engine (queue, transport, tracks, delays, media information, lifecycle) runs against real VLC 3 on branch `feature/linux-port`; CI job added | GPU video output (VA-API, DMA-BUF import), X11/Wayland qualification, runtime choice and packaging |
| macOS | Managed builds/tests, layout probe, graphics probe | Everything native |
| Everyday controls | Queue, history/resume, settings, PiP, tracks, timing, video geometry and information | Verify end-to-end; close daily-use failures and platform differences |
| Subtitles | Track selection/delay; VLC-rendered subtitles overlaid above the video on GPU and CPU paths, text checked by hand on Windows | Styled ASS and image subtitle checks; live external subtitle attachment |
| Engineering | Boundaries, pinned inputs, fifteen-job CI definition, recorded managed/native checks | Current-revision evidence, durable artifacts, accessible dependencies |
| Submission | Technical README, CI-built Windows ZIP, draft GitHub release (stale preview.1) | Fresh tagged release, comparison images, demo video, release notes, entry form |

Source of truth for inputs is [eng/versions.json](../eng/versions.json): .NET 10.0.102, Avalonia 12.1.1, VLC 3.0.24, VLCLR `09b46d50848cc0abe3c236e7a25e43679ee13c02`. Older reports describe earlier pins and test totals. Preserve them as historical records; do not treat their results as qualification of a new release.

Relevant existing documents:

- [Architecture boundaries](rfc/0001-port-boundaries.md), [backend ownership](VIDEO_BACKENDS.md), [D3D11 implementation](D3D11_COMPOSITION.md).
- [Platform requirements](CROSS_PLATFORM.md), [native/compositor qualification](TARGET_QUALIFICATION.md), [CI coverage](CI.md).
- [Capability matrix](PLAYER_CAPABILITY_MATRIX.md), [VLC 4 UI plan](VLC4_SCREEN_BY_SCREEN_PLAN.md), [Windows integration report](WINDOWS_UI_INTEGRATION_2026-09-10.md).

## 3. Scope, priorities and capacity

**P0** means required for any submission we are willing to ship: installable Windows release, reliable daily playback including subtitles, accessible source/dependency delivery, truthful evidence, required submission materials. **P1** means required for the intended three-platform entry: native Linux/macOS playback, packaging and qualification. **P2** means optional improvements after the release gates pass.

Keep VLC 3 and the existing product/framework boundary. Reusable ABI declarations, safe native controls and subpicture access belong in VLCLR. Platform rendering belongs in the product backends/plugins. Update framework pins only after validating the framework change. The existing requirement for hardware decoding and GPU presentation on each desktop target remains in force; successful managed builds or CPU fallback do not satisfy it.

Initial target machines: Windows x64 with existing Intel coverage plus one additional GPU vendor; Linux x64 with a selected VA-API-capable GPU, testing X11 and Wayland separately; macOS Apple Silicon with VideoToolbox. These are proposed qualification targets, not current support claims. macOS Intel and broader Linux/GPU coverage are P2 until the primary targets pass.

The estimates below are focused engineering days, not calendar promises. They total **34–61 days**, with overlap between integration and qualification possible. About six calendar weeks remain. Full three-platform delivery is aggressive for one engineer, particularly while the native surface routes are unproven. Record actual availability on day one; identify human ownership by domain if additional contributors are available. Do not assume extra contributors, hardware or credentials exist.

| Package | Priority | Estimate | Dependencies | Suggested owner domain |
|---|---|---:|---|---|
| W01 Baseline, scope and resources | P0 | 1 | None | Maintainer |
| W02 Dependency/source delivery | P0 | 1–2 | W01 | Maintainer/release |
| W03 Windows package | P0 | 2–3 | W02 | Release |
| W04 Subtitle rendering | P0 | 4–7 | W01; framework changes as needed | Native graphics |
| W05 Portable native controls | P1 | 2–4 | W01 | Framework/native |
| W06 Linux backend | P1 | 5–10 | W05; early spike can start at W01 | Linux graphics |
| W07 macOS backend | P1 | 5–10 | W05; early spike can start at W01 | macOS graphics |
| W08 Daily-use polish and recovery | P0/P1 | 3–5 | W01; platform passes after W06/W07 | Product |
| W09 Reproducible releases on three OSes | P1 | 3–5 | W02/W03/W06/W07 | Release |
| W10 Qualification and performance evidence | P0/P1 | 4–7 | Runs throughout; final after W04/W08/W09 | Validation |
| W11 Migration narrative and demo | P0 | 3–5 | Start W01; final evidence W10 | Maintainer/editor |
| W12 Final submission | P0 | 1–2 | W02–W04/W08/W10/W11; W06/W07/W09 for three OSes | Maintainer |

Critical path: target access → native control integration → actual decoder-surface import → subtitle/lifecycle integration → native package → clean-machine qualification → frozen evidence → submission. Protect W11 time throughout this path.

Explicitly defer: mobile/WebAssembly, full media indexing, transcoding/capture/broadcasting, plugin administration, independent multi-window PiP, broad redesigns, VLC 4 migration, snapshot/deinterlace/audio-device features unless a qualification finding makes one essential. Keep unsupported controls truthful. Do not expand features merely to increase the capability count.

## 4. Detailed work packages

### W01 — Establish a current baseline and executable scope

- [ ] Record product/framework/runtime hashes, installed toolchains and the current CI run for this exact product revision. Run the existing appropriate checks before changing native code.
- [ ] Reconcile the capability matrix with current source: several rows still describe old prerequisites now implemented elsewhere. Add current status/evidence without rewriting historical reports as fresh results.
- [x] Select and record a pre-contest VLC release/source revision as the original application. Separate that baseline from later VLC 4 visual references. Define the migrated workflows and omissions in one table.
- [ ] Inventory physical Windows, Linux and macOS access, GPUs, OS/session versions, available disk/build tooling, and signing/notarization access. Arrange missing resources immediately; purchases/account actions remain future decisions.
- [ ] Assign a named owner and capacity to each active package. Record unresolved dependencies and dates in a tracker derived from this document.
- [x] Create `docs/contest/STATUS.md` with task IDs, owner, status, blockers, evidence link and next decision date. Use Passed/Failed/Blocked/Not tested distinctly.

Acceptance: a fresh baseline report, named target machines, actual capacity and a bounded migration scope exist. No platform is marked supported solely from compilation.

### W02 — Make the source and dependency story usable

Decision (October 7, revised October 8): VLCLR is a git submodule at `external/vlclr`, tracking the `3.x` branch of the public [mfkl/vlclr](https://github.com/mfkl/vlclr/tree/3.x) repository (VLCLR `main` follows VLC 4). The branch was started from the framework tree this repository had vendored, without the private history, and carries its own GPL-2.0-or-later `LICENSE`. No deploy key or private checkout is needed to build; clone with `--recursive`.

- [ ] Inventory the exact managed/native source and binaries needed to reproduce a release, including VLCLR, VLC runtime, vout, control helper, fonts/icons and sample media.
- [x] Choose a distributable dependency arrangement: VLCLR as a public submodule (decision above).
- [x] Build the app and Native AOT vout from the VLCLR submodule; drop `VLCLR_ROOT` from the build, the `-FrameworkRoot` script parameters, the CI deploy key and the `VLCLR_CI_ENABLED` gate.
- [x] Decide the license covering the repository and VLCLR's `3.x` branch: GPL-2.0-or-later (October 7 and 8).
- [ ] Verify actual dependency licenses and redistribution/source obligations before release; include required license texts, notices and corresponding source/build instructions.
- [x] Give the VLC runtime a reproducible download procedure and hash: `eng/get-vlc.ps1` fetches the official VLC 3.0.24 Windows x64 build and checks the SHA-256 in `eng/versions.json` (October 7). Same module ABI `3_0_0f` as the earlier 3.0.24-beta1 pin; the only SDK header change is a removed playlist sort enum value VLCLR does not use.
- [x] Requalify on hardware with the 3.0.24 runtime (smokes and the 25-cycle package test): 14/14 on October 8.
- [ ] Give the smoke media a public route; it still comes from the maintainer's VLCLR checkout.
- [ ] Verify a fresh checkout can build using the published instructions without the maintainer's sibling workspace or CI deploy key.

Acceptance: another developer can obtain the release's inputs and reproduce the build through the documented route. Runtime users need no source checkout or development credentials.

### W03 — Produce a judge-ready Windows package early

- [x] Add `eng/package.ps1` accepting configuration, RID and output directory; validate inputs and use the pinned dependencies.
- [x] Publish the managed application and Windows Native AOT vout locally, stage the matching VLC runtime/plugins and required helper libraries, generate/validate the plugin cache, and exclude development-only fixtures/logs.
- [x] Produce a versioned self-contained Windows x64 portable review ZIP with app version, current notices, source link and SHA-256 checksums. Public distribution remains blocked on W02; this local package is not release approval.
- [ ] Run from a path containing spaces and non-ASCII characters, with no SDK, installed VLC, sibling checkout or `VLCLR_ROOT`/`VLC_DIRECTORY` overrides. Test a normal non-admin account and an installed directory that is not writable by the app.
- [ ] Verify profile/thumbnail storage uses user-writable locations; move or update the package without losing settings/history. Check missing/corrupt runtime failures are understandable and recoverable.
- [x] Write a one-page launch guide: download, extract, open a file, subtitles, queue, PiP, settings and known limitations.

Acceptance: a fresh-machine user launches the package and reaches working video/audio without a terminal or developer setup. Keep a package hash and test record; a local `dotnet run` is insufficient.

### W04 — Finish real subtitle presentation

- [x] Trace the pinned VLC vout subpicture contract and lifetime through prepare/display. Inspect current `CompositionVoutPlugin` handling, including deletion, before changing ownership.
- [x] Add any required checked subpicture/region access and ownership APIs in VLCLR, with ABI/lifetime validation. Do not duplicate native layouts in the UI project. (VLCLR's existing subpicture structs sufficed.)
- [x] Design region composition preserving VLC-rendered pixels, alpha, placement, timing and styling. Handle multiple regions and release/reuse correctly across pause, seek, stop, new input and device recreation.
- [x] Implement composition on the Windows hardware path and verify the CPU compatibility path separately. Video frames must retain their hardware transport; account for subtitle-region uploads separately from decoded-video uploads. (October 7: RGBA regions composed in the plugin and drawn by a sibling overlay control.)
- [ ] Use controlled, redistributable embedded/external SRT, styled ASS and image-subtitle fixtures. Verify Unicode, overlapping regions, track off/switch, signed delay, paused redraw, aspect/crop, fullscreen and HiDPI.
- [ ] Make attaching an external subtitle useful in the running session. Prefer safe live attachment if the reviewed engine supports it; otherwise provide an explicit user-invoked reopen/apply flow preserving the relevant playback position and queue, with clear failure handling.
- [ ] Keep subtitle logic compatible with the Linux/macOS backend contract; qualify actual composition on each implemented backend before claiming support there.
- [ ] Update the capability matrix and remove the subtitle caveat only for cases that passed visual and native checks. Capture the rendered GPU output using an actual window/display capture.

Acceptance: the subtitle fixture matrix visibly passes, including styled and image subtitles, with no stale regions, ownership imbalance or video-frame CPU transfer regression. A track-selection API pass alone fails this gate.

### W05 — Integrate portable native controls against real VLC

- [x] Start from the existing pinned `VLC3ControlBridge`, native layout probe and CI fixtures. List remaining Win64 guards/offsets in framework and product integration. (October 8: replaced by a managed `va_list` builder; no native helper needed.)
- [ ] Verify library exports, target calling conventions, structure layouts and bridge loading on each selected OS/architecture using the actual pinned VLC runtime.
- [x] Implement reusable playlist/input control ownership behind the framework facade: open, play/pause, stop, seek/rate, queue changes, track/delay controls and cleanup. (Windows x64 and Linux x64.)
- [x] Integrate the tested bridge into playback owners where required. Ensure the helper is built for each host architecture and included in packages. (No helper: the `va_list` is built in managed code.)
- [ ] Exercise failure, callbacks during shutdown, pointer-width handling, stale input generations and repeated create/dispose against real playback. Preserve existing Windows behavior or qualify any changed path.

Acceptance: native runtime controls work on each selected target with correct ownership and bounded shutdown. Native fixtures remain useful evidence but cannot substitute for this gate.

### W06 — Implement and qualify Linux GPU playback

Status, October 8: the engine half is done on `feature/linux-port` and checked in WSL2 against Ubuntu's VLC 3.0.20 (queue, capabilities, 25 engine cycles, 242 managed tests, layout probe), with a CI job. WSL offers no GPU without D3D12 translation, so every item below that needs GPU output is open and moves to native Linux. Details: [CROSS_PLATFORM.md](CROSS_PLATFORM.md#linux-engine-port-october-8).

- [ ] Run the existing ABI and graphics probes on the target Linux machine, separately in X11 and Wayland. Record GPU/driver, runtime, interop handles and supported synchronization.
- [ ] Conduct an early bounded spike: prove hardware decoding on the chosen VLC 3 configuration and import/present an actual decoded surface in Avalonia. VA-API surface export/import is a candidate to investigate, not an assumed working route.
- [ ] Record the successful route or specific incompatibility, including ownership, surface format/modifier, color conversion and synchronization requirements. Use the spike result to estimate full implementation.
- [ ] Implement a Linux backend under the existing `IVideoBackend`/`VideoBackendSession` boundary. Build native pieces on Linux; keep handles and platform identifiers out of view models.
- [ ] Implement surface lifetime, GPU conversion, subtitle composition, frame scheduling/backpressure, resize, disposal and device/import failure behavior. Reject stale callbacks and frames after detach or new input.
- [ ] Run H.264 and HEVC 10-bit where hardware-supported, transport/queue/tracks, fullscreen/PiP, stop/reopen and audio playback. Report unsupported hardware/codecs explicitly.
- [ ] Test X11 and Wayland independently. Record distribution/library requirements and compositor restrictions in the release support matrix.

Acceptance: actual audio/video and subtitle workflows pass in the promised sessions, hardware decode is observed, decoded-video CPU uploads/readbacks are zero on the qualified route, and lifecycle resources balance. Xvfb or a graphics capability report cannot satisfy this gate.

### W07 — Implement and qualify macOS GPU playback

- [ ] Run ABI/control and graphics probes on native Apple Silicon. Identify actual Avalonia image-import/synchronization support and the pinned VLC VideoToolbox surface contract.
- [ ] Conduct an early bounded spike: import/present a real hardware-decoded surface. Investigate IOSurface/CoreVideo and available compositor interop without assuming compatibility from advertised handle support.
- [ ] Define native surface retention, conversion, synchronization, thread affinity and plugin/runtime loading. Resolve required framework bindings before product integration.
- [ ] Implement a macOS backend with the shared command/session boundary, GPU presentation, subtitle regions, disposal and failure recovery.
- [ ] Qualify H.264 and HEVC 10-bit where supported, audio, seeks, queue/tracks, fullscreen, PiP and window transitions. Record native architecture and avoid treating Rosetta behavior as ARM64 qualification.
- [ ] Apply macOS command-key defaults and labels; test menus, file dialogs, focus, window close/quit, display scaling and sleep/wake.

Acceptance: a native Apple Silicon app presents hardware-decoded video and subtitles through Avalonia with correct lifetime/synchronization, zero decoded-video CPU transfers on the qualified route, and usable desktop interactions.

### W08 — Make everyday use polished and resilient

- [ ] Walk through first launch, empty/populated Home, file/folder/URL opening, drag/drop, queue reorder/removal/repeat/shuffle, resume, audio-only media, tracks, PiP and Settings. Fix observed defects rather than reimplementing working features.
- [ ] Exercise inaccessible files, removed recent items, corrupt/unsupported media, HTTP failure/interruption, unseekable streams and rapid open/stop/reopen. Show actionable errors and allow the next valid open to succeed.
- [ ] Verify settings persistence, privacy clearing, playback continuity through Home/Settings/PiP, and restored focus after drawers/dialogs. Ensure unsupported actions have accurate labels/capability states.
- [ ] Review actual app captures against the selected VLC references at wide and compact sizes. Correct visible clipping, inconsistent icons/spacing, title-bar treatment and hover/focus states. Label original/reference images separately from Avalonia results.
- [ ] Perform keyboard-only and screen-reader passes for opening media, play/pause, seeking, volume, tracks, queue and settings on the declared targets. Verify names/values, logical focus order, contrast and reduced motion.
- [ ] Test physical 100/125/150/200% scale where available and monitor transitions, including a 480x360 compact layout. Distinguish policy tests from physical display evidence.
- [ ] Rank defects: release blockers include crash/hang, broken primary workflow, unreadable subtitles, data loss and inaccessible core controls. Fix these before optional visual or advanced feature work.

Acceptance: the core journey succeeds in each claimed release without developer knowledge; no known release blocker remains. Known nonblocking limitations are specific and visible in release notes.

### W09 — Ship reproducible desktop releases

- [ ] Extend the packaging interface to build each native target on its own OS. Use the same product revision and record every framework/runtime pin in the artifact manifest.
- [ ] For Linux, choose one documented portable/distribution package after examining native dependencies; stage plugins/helper libraries, launcher and desktop metadata. Test outside the checkout on the stated distribution/session matrix.
- [ ] For macOS, stage an ARM64 `.app` with correct framework/plugin paths, bundle metadata and icon. Arrange signing/notarization early, sign nested code in the correct order, and test the downloaded artifact under normal Gatekeeper behavior. If credentials are unavailable, resolve the actual launch experience before declaring the package ready.
- [ ] Add a proposed `.github/workflows/release.yml` that creates versioned packages, matching source/dependency artifacts, checksums and provenance. Retain release evidence beyond the existing short CI artifact retention period.
- [ ] Verify each package on a clean user environment, with no source paths or development variables. Test launch, media open, subtitles, audio, fullscreen/PiP, settings and clean exit from the downloaded package itself.
- [ ] Prepare release notes with exact OS/architecture/GPU/session coverage, installation steps, known limits and source/build links. Verify links in a signed-out browser before publication is considered complete.

Acceptance: every advertised target has a downloadable package that passes the same core journey on a clean machine. A build artifact containing only the managed executable does not pass.

### W10 — Prove reliability and the rendering advantage

- [ ] Add a proposed `docs/contest/QUALIFICATION.md` with one row per exact package/target/fixture. Record product hash, pins, package hash, OS, architecture, GPU/driver, session, command, expected outcome, actual outcome and evidence.
- [ ] Reuse existing Core/Desktop/Windows tests and native smokes. Add focused tests for new ownership, subtitle, packaging or recovery behavior; avoid tests that merely duplicate implementation details.
- [ ] Run at least 25 open/play/pause/seek/stop/dispose cycles on each native backend, plus a 60-minute playback soak. Check crashes, stalls, allocations/releases and memory trends after warm-up.
- [ ] Exercise resize/fullscreen/PiP, monitor changes, sleep/wake and unavailable/lost GPU conditions on physical hardware. Establish recovery or a clear bounded failure state; do not call an untested transition passed.
- [ ] Verify A/V sync and color/aspect using controlled fixtures. Cover H.264 1080p, supported 4K/HEVC Main 10, software-decoded video fallback where offered, audio-only media and subtitle formats.
- [ ] Measure startup, time to first presented frame, CPU/process memory, dropped/late frames and video CPU-transfer counters on fixed media/hardware. Separate subtitle texture work from full-video-frame transfers.
- [ ] Compare against the selected VLC baseline using the same machine, media, playback duration and settings; repeat at least three times and publish methodology plus ranges/median. Do not promise a speedup in advance. Explain regressions and hardware limits.
- [ ] Prepare redacted diagnostics that identify backend, pins and failures without leaking user paths/URLs. Link durable logs and native video captures to each claim.
- [ ] Arrange a small beta pass with three to five external testers if available: fresh install plus the core journey, including at least one tester per promised OS. Prepare instructions before requesting outreach; this plan sends no invitations.

Acceptance: every public claim has reproducible evidence on the final packages, with failures/skips retained honestly. Managed test counts are reported separately from native/GPU qualification. No known release-blocking beta defect remains.

Existing starting commands (PowerShell 7; set actual pinned checkout/runtime paths first):

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release -FrameworkRoot $env:VLCLR_ROOT
./eng/test.ps1 -Scope Windows -Configuration Debug -FrameworkRoot $env:VLCLR_ROOT
./eng/test.ps1 -Scope Windows -Configuration Release -FrameworkRoot $env:VLCLR_ROOT
dotnet run --project src/Conic -c Release -- --smoke --expect-path=hardware-opaque-gpu
dotnet run --project src/Conic -c Release -- --engine-smoke --cycles=25
dotnet run --project src/Conic -c Release -- --ui-smoke
```

The Windows scope and existing native smoke implementations are Windows-only at this baseline. New backends must extend native qualification deliberately. Existing graphics probes check capabilities, not playback. Use isolated test profiles and redistributable fixtures; final package qualification must launch packaged binaries as well as development builds.

### W11 — Turn the work into a compelling migration account

- [ ] Start a migration journal now: task, time spent, failure, decision, result and linked commit. Separate measured effort from retrospective estimates; describe AI assistance concretely where useful.
- [x] Draft a succinct self-contained README migration narrative (roughly 200–300 words, per the maintainer's September 12 instruction). Link detailed evidence separately; final submission facts and human effort remain to be completed.
- [ ] Explain the original desktop stack/workflows, why Avalonia, precisely what was rewritten/reused, and what is intentionally outside scope. Include the original pre-contest baseline and migration timeline.
- [ ] Explain the difficult parts with diagrams and code links: engine/UI boundary, the child-window overlap problem, Native AOT vout, GPU conversion/import, surface ownership, portable native calls and subtitles.
- [ ] Describe alternatives tried, what failed, work that transferred cleanly, platform-specific cost, actual effort and remaining limits. Translate internal task IDs into prose an unfamiliar developer can follow.
- [ ] Capture before/after pairs for Home, video with controls/subtitles, queue, Settings and PiP at matching window sizes/media where possible. Identify the app/version in each image; do not present the root reference PNGs as captures of this app.
- [ ] Record a concise 2–3 minute demo from release packages: fresh launch/open, subtitles, seek/queue, PiP/settings and the same workflow on the other qualified OSes. Keep performance overlays separate from normal product UI.
- [ ] Prepare an optional technical appendix with benchmark methodology, backend matrix, relevant snippets and reproducibility instructions. The required migration account must remain readable in the README itself.
- [ ] Have someone unfamiliar with the code follow the README and summarize what was migrated, what works and how to try it. Correct confusing claims and broken links.

Acceptance: a judge can understand the migration and its value, launch a build and inspect supporting evidence without navigating internal development reports. The story and screenshots match the final implementation.

### W12 — Freeze, verify and submit

- [ ] Freeze a release candidate, rerun affected checks and the full promised-platform core journey, then record hashes. Any subsequent implementation change invalidates affected evidence until rechecked.
- [ ] Complete the public release/source package, README narrative, before/after images, exact support matrix, checksums and durable evidence links.
- [ ] Inspect the current entry form and announcement again; confirm team details, links and any fields/rules not present in the announcement. Prepare exact form answers before submission.
- [ ] Run a signed-out review of repository, source inputs, downloads and documentation. Have a fresh user execute the demo path from the actual download.
- [ ] Submit the prepared entry by October 22, 18:00 UTC and retain confirmation. Publication and submission are future execution steps, not actions performed by this planning task.
- [ ] Reserve October 23 for correcting access/packaging/submission failures; avoid new features. Keep submitted artifacts available through judging and record any replacement release explicitly.

Acceptance: entry confirmation exists, every submitted link works, and the downloadable build matches the described/tested revision.

## 5. Calendar and decision gates

Dates below are targets. Update estimates from spike evidence and actual capacity; do not silently shift the final qualification/write-up window to absorb native research.

**October 8 checkpoint.** The September milestones for Linux/macOS hardware playback were not met; the maintainer scoped the pass to Windows first. Windows met its gates. Fourteen days remain: the Linux GPU backend (W06) is the only path to a cross-platform entry, and the October 11 feature freeze below applies to it too unless the maintainer moves it. macOS has not started and is unlikely to fit; dropping it is the maintainer's decision.

| Date | Required evidence or decision |
|---|---|
| Sep 12–16 | W01 complete; W02 distribution route chosen; Windows package skeleton; target machines available; Linux/macOS real-surface spikes underway; migration journal and outline started |
| Sep 17–20 | First clean-machine Windows ZIP; subtitle composition prototype; both platform spikes have a demonstrated path or a written blocker and revised estimate |
| Sep 21–27 | W04 Windows subtitle matrix passes; W05 real runtime controls pass; initial Linux/macOS hardware playback through Avalonia; first before/after set and write-up draft |
| Sep 28–Oct 4 | Three-platform core journeys including subtitles; first native packages; daily-use defects prioritized; choose final advertised target scope from evidence |
| Oct 5–11 | Feature freeze by Oct 11; package/monitor/sleep/recovery passes, external beta where available, benchmarks and narrative draft complete |
| Oct 12–16 | Fix release blockers, rerun affected qualification, finalize signed/distributable packages and capture release demo/screenshots |
| Oct 17–20 | Final release candidate, clean-machine rerun, independent README review, exact submission answers and link checks |
| Oct 21–22 | Publish final artifacts and submit by Oct 22, 18:00 UTC |
| Oct 23 | Submission/access repair buffer; official cutoff 23:59 UTC |

### Recovery decisions if the schedule slips

- **September 16, resources:** missing Linux/macOS hardware or runtime access is a critical-path blocker. Resolve access, assign a capable contributor if available, or record that the full target schedule is at risk. More hosted managed CI does not solve it.
- **September 20, native feasibility:** if a platform cannot present a real decoded surface, write the concrete obstacle and remaining experiment budget. Stop optional work and concentrate on proving that path. Re-estimate before expanding the backend.
- **September 27, delivery feasibility:** if native playback still has no credible completion date, prepare a reduced-platform submission option. The maintainer must explicitly decide whether to change the existing three-platform release commitment; this plan does not silently relax it or replace hardware playback with software qualification.
- **October 4, scope:** advertise only platforms with a credible package/qualification path before freeze. Protect subtitles, usability, source access and the write-up. A narrower submission sacrifices cross-platform competitiveness but may preserve a usable entry.
- **October 11, freeze:** defer P2 work and all nonessential new features. Fix release blockers, package failures and inaccurate claims. Reconsider entry readiness if primary playback or subtitles remain unreliable.
- **October 20, readiness:** missing required artifacts or a build judges cannot launch blocks submission readiness. Use the remaining time to repair access and delivery, not to introduce new architecture.

## 6. Release evidence matrix

Use this template when W01 creates the status/qualification documents. All final results start as **Not tested**, even where historical development evidence exists. Attach exact evidence to every transition to Passed.

| Gate | Windows x64 | Linux x64 X11 | Linux x64 Wayland | macOS ARM64 |
|---|---|---|---|---|
| Clean-machine package launch | Not tested | Not tested | Not tested | Not tested |
| Actual hardware decode + GPU presentation | Not tested | Not tested | Not tested | Not tested |
| Audio, seek, queue, tracks and resume | Not tested | Not tested | Not tested | Not tested |
| Text/styled/image subtitle rendering | Not tested | Not tested | Not tested | Not tested |
| Fullscreen/PiP/settings continuity | Not tested | Not tested | Not tested | Not tested |
| Lifecycle stress and playback soak | Not tested | Not tested | Not tested | Not tested |
| Display/sleep/recovery and accessibility | Not tested | Not tested | Not tested | Not tested |
| Versioned source, notices and reproduction | Not tested | Not tested | Not tested | Not tested |
| Final capture, diagnostics and package hash | Not tested | Not tested | Not tested | Not tested |

Completion rule: a task is done when its acceptance evidence is recorded, not when code exists. The intended three-platform entry is ready only after all promised-target gates, required submission artifacts and final link checks pass. A changed target scope requires an explicit recorded decision and matching public claims.

# Porting VLC's desktop player to Avalonia

Conic is an entry in the
[Avalonia Port Challenge](https://avaloniaui.net/blog/avalonia-port-challenge):
VLC 3's desktop player, rebuilt with Avalonia 12 on top of VLC 3.0.24's own
engine. It runs on Windows and macOS; on Linux the engine runs but video
output isn't written yet. It is a one-off experiment and will not be
maintained.

| VLC 3.0.24 | Conic |
| --- | --- |
| ![VLC 3 playback](contest/images/vlc-playback.png) | ![Conic playback](contest/images/conic-playback.png) |
| ![VLC 3 Simple Preferences](contest/images/vlc-preferences.png) | ![Conic Settings](contest/images/conic-preferences.png) |

More pairs: [VLC 3 and Conic, side by side](contest/COMPARISON.md).

## Starting point

VLC 3.0.23 (December 2025) is the baseline: a C codebase whose desktop
interface is a Qt 5 module (`modules/gui/qt`) loaded by libvlccore like any
other plugin. The interface talks to the core through VLC's internal objects
and variables, not through the public LibVLC API.

## What it cost

- **Time.** 292 commits on 14 working days between August 31 and October 14,
  2026. Most of the app was built in two bursts: the player itself in early
  September, and VLC 3 parity, macOS and Linux in October.
- **Size.** About 25,000 lines of C# in `src` (of which about 5,800 drive the
  smoke tests), 6,800 lines of AXAML and 6,800 lines of unit tests.
- **Prior work.** [VLCLR](https://github.com/mfkl/vlclr), the C# framework for
  VLC plugins, existed before the challenge. Its `3.x` branch, for VLC 3, was
  written for Conic.
- **AI.** AI assistance (Claude Code) was used for implementation and
  testing.

## Approach

**Keep the engine, replace the interface.** The Qt widgets were not
translated. Each everyday workflow (open, queue, transport, tracks, subtitles,
video adjustments, media information, history and resume, preferences, the
log) was rebuilt as an Avalonia view over an engine contract
(`IMediaEngine`, immutable state snapshots, one serialized update stream).
The visual design follows Juxtopposed's
[VLC Redesign](https://www.figma.com/community/file/1691465999134370980/vlc-redesign).

**Talk to the core the way Qt does.** Conic loads libvlccore and starts each
core with `libvlc_InternalCreate`, `libvlc_InternalInit` and
`libvlc_InternalAddIntf`. Its control interface is a VLC plugin, so it reads
and sets variables on the playlist, the input and the audio output from
inside the core, as the Qt module does. This is what makes VLC's preferences
real in Conic: each Settings row maps to a VLC option or variable.

**Plugins in C#.** Both plugins, the control interface and the video output,
are written in C# with [VLCLR](https://github.com/mfkl/vlclr/tree/3.x) and
compiled with Native AOT into ordinary VLC 3 plugin libraries. There is no C
shim.

## What translated cleanly

- **The engine and its options.** Codecs, filters, network protocols and
  hardware decoding are VLC's own, untouched. A Settings row is a VLC option
  or variable, so most of VLC's preferences carried over as data rather than
  code.
- **VLC's tools.** Frame stepping, the A-B loop, snapshots, programs,
  deinterlacing, crop and the video effects are variables on the playing
  input or video output in VLC 3. Once the control plugin could set and
  trigger variables, each one was a few lines plus a smoke test.
- **Menus and hotkeys.** VLC's menu entries became one command registry:
  each command has a name, a menu and a default shortcut, and the macOS menu
  bar, the keyboard and the Controls settings page all read from it.
- **The design.** Avalonia's styles and resources took the Figma file's colors,
  type and spacing as tokens, which gave Dark and Light themes, accent colors
  and a compact density almost for free.

## What needed rethinking

- **Video.** See below: this was most of the work.
- **Dialogs into panels.** VLC's floating playlist, Effects and Filters
  dialog and two-level preferences became the redesign's side panels and
  twelve searchable Settings pages. Effects and Filters is now rows of
  Settings > Audio and Settings > Video.
- **When a setting applies.** VLC reads some options only when media opens or
  when the core starts. Rather than hide this, each Settings row carries a
  tag: **Live**, **Next open** or **Restart**. Rows VLC 3 can't honor in this
  app were dropped.
- **Platform conventions.** On macOS, Hide UI moved from Cmd+H, which macOS
  keeps for hiding the app, to Cmd+Shift+H, and Finder's Open With lists Conic
  through the bundle's document types.

## The hard part: video

VLC draws video into a native window it owns. Avalonia controls can't sit on
top of a foreign native window, and the design puts controls, panels and
menus over the video. Conic's video output plugin instead hands each decoded
frame to Avalonia's compositor as a GPU texture: a D3D11 keyed-mutex shared
texture on Windows, an IOSurface on macOS. Video becomes one more visual in
the window, so z-order, transparency and animation just work.

Getting a first frame on screen was quick. Making it hold up took most of the
time: hardware-decoded NV12 and P010 surfaces converted on the GPU, surface
ownership across threads, stale callbacks after stop, reopen cycles and plugin
discovery inside a portable package. Each got its own smoke test.

## What caught us by surprise

- **Two features are broken in VLC 3.0.24 itself.** Post-processing has no
  visible effect, and the stereo widener refuses the usual audio output
  chains. They are listed as missing rather than shipped as switches that do
  nothing.
- **An SVG path that renders differently.** The cone logo's path used the
  shorthand `s` right after a straight line. Avalonia's geometry parser reflects
  an earlier curve's control point there, and the tip of the cone grew a bulge.
  Writing the curve out fixed it.
- **Windows holds files for a moment.** Saving settings sometimes failed with
  "Unable to remove the file to be replaced", because an indexer or antivirus
  had just opened the file. The save now retries briefly.

## Cross-platform

- **Windows x64:** D3D11 hardware decoding and GPU presentation, with a
  software upload path and a CPU bitmap fallback.
- **macOS arm64 and x64:** VideoToolbox decoding and IOSurface presentation,
  native fullscreen, the app menu, an `.app` bundle.
- **Linux x64:** the same app and engine run; the GPU video output is not
  written yet.

One C# codebase covers all three. Platform code is limited to the video
backends, window chrome and a few OS services (audio devices, screensaver,
proxy password storage). Builds are portable archives that bundle VLC 3.0.24.

## Completeness

Conic reaches 80% parity with VLC 3.
[Feature parity with VLC 3](FEATURE_PARITY.md) compares 183 VLC 3 features:
83 work the same, 48 work differently or in part, 22 are missing and 30 were
left out on purpose (disc menus, streaming and conversion, capture, skins,
extensions). The everyday ways in are there: VLC's hotkeys, several files or
URLs at once, its network protocols, the command line, single-instance
mode, the clipboard and drops. So are its tools: snapshots, the A-B loop,
frame stepping, programs, subtitles added while playing, and its video
effects, overlays and crop, applied live. Conic adds 13 features VLC 3 doesn't
have, such as picture in picture and a Continue watching list.

## Quality

- 667 unit tests for the engine contract, state, settings and view models.
- About 30 smoke modes drive the real app with real VLC and real media on
  Windows and macOS: every playback path, the default shortcuts, the queue,
  menus, Settings (the equalizer is measured on the audio output, network
  options against a local server), VLC 3's tools, network protocols from
  local servers, and picture in picture.
- Packages are qualified outside the repository, from the zip.

## Lessons

- Reusing the engine was the right call; rebuilding the interface around
  workflows, not widgets, kept the port tractable.
- Composition-based video is the piece that makes an Avalonia player feel
  native, and also the piece that needs the most testing.
- A setting that does nothing is worse than no setting.

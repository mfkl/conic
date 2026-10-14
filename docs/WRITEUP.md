# Porting VLC's desktop player to Avalonia

Conic is an entry in the
[Avalonia Port Challenge](https://avaloniaui.net/blog/avalonia-port-challenge):
VLC 3's desktop player, rebuilt with Avalonia 12 on top of VLC 3.0.24's own
engine. It is a one-off experiment and will not be maintained.

## Starting point

VLC 3.0.23 (December 2025) is the baseline: a C codebase whose desktop
interface is a Qt 5 module (`modules/gui/qt`) loaded by libvlccore like any
other plugin. The interface talks to the core through VLC's internal objects
and variables, not through the public LibVLC API.

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
discovery inside a portable package. Each got its own smoke
test.

## Cross-platform

- **Windows x64:** D3D11 hardware decoding and GPU presentation, with a
  software upload path and a CPU bitmap fallback.
- **macOS arm64 and x64:** VideoToolbox decoding and IOSurface presentation,
  native fullscreen, the app menu, an `.app` bundle.
- **Linux x64:** the same app and engine run; the GPU video output is not
  written yet.

One C# codebase covers all three. Platform code is limited to the video
backends, window chrome and a few OS services (audio devices, screensaver,
proxy password storage).

## Completeness

Conic reaches 80% parity with VLC 3.
[Feature parity with VLC 3](FEATURE_PARITY.md) compares 183 VLC 3 features:
83 work the same, 48 work differently or in part, 22 are missing and 30 were
left out on purpose (disc menus, streaming and conversion, capture, skins,
extensions). The everyday ways in are there: VLC's hotkeys, several files or
URLs at once, its network protocols, the command line, single-instance
mode, the clipboard and drops. So are its tools: snapshots, the A-B loop,
frame stepping, programs, subtitles added while playing, and its video
effects, overlays and crop, applied live. Conic adds 13 features VLC 3 doesn't have, such as picture in picture
and a Continue watching list.

## Quality

- 666 unit tests for the engine contract, state and view models.
- About 40 smoke tests drive the real app with real VLC and real media on
  Windows and macOS: every playback path, every default shortcut, the queue,
  menus, Settings (the equalizer is measured on the audio output, network
  options against a local server) and picture in picture.
- Packages are qualified outside the repository, from the zip.

## Lessons

- Reusing the engine was the right call; rebuilding the interface around
  workflows, not widgets, kept the port tractable.
- Composition-based video is the piece that makes an Avalonia player feel
  native, and also the piece that needs the most testing.
- A setting that does nothing is worse than no setting. Rows VLC 3 can't
  honor in this app were dropped.

AI assistance (Claude Code) was used for implementation and testing.

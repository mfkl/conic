# Conic

Conic is a modern copy of VLC 3's desktop player. Media plays through VLC
3.0.24's own engine, and a new interface, written in C# with
[Avalonia](https://avaloniaui.net/), replaces VLC's Qt one. It runs on Windows
and macOS. On Linux the engine runs, but video output isn't written yet.

> [!NOTE]
> Conic is a one-off experiment. It is not made or endorsed by VideoLAN, it
> will not be maintained, and issues and pull requests may go unanswered. To
> watch videos, use [VLC](https://www.videolan.org/vlc/).

<!-- SCREENSHOT: hero, Home and the player side by side, Dark, 1920x1080 -->

## What it is

Conic keeps VLC 3 as it is (the same engine, codecs, filters and options) and
rebuilds the desktop app around it:

- **Home**, with recent media, favorites and saved positions, in a grid or a
  list, and a floating mini player while something plays.
- **The player**, with a docked control bar and side panels for the queue,
  audio, video, subtitles, chapters and media information.
- **Picture in picture**, an always-on-top compact player.
- **Settings**, in twelve pages. Each row takes effect, and a tag says when:
  **Live**, at the **Next open**, or after a **Restart**.
- **Messages**, VLC's log, live, as in VLC.
- **Dark and Light themes**, accent colors and a compact density.

<!-- SCREENSHOT: the player with the queue panel open -->
<!-- SCREENSHOT: Settings, Audio page with the equalizer -->
<!-- SCREENSHOT: picture in picture over another window -->

Most of what you use VLC 3 for every day is there. Of 183 VLC 3 features
compared, 57 work the same, 44 work differently or in part, 52 are missing and
30 were left out on purpose. Missing are, among others, opening files from the
command line or as the default player, snapshots, and network protocols other
than HTTP(S). Disc menus, streaming and conversion,
capture devices, skins and extensions were left out.
[Feature parity with VLC 3](docs/FEATURE_PARITY.md) compares the two, feature by
feature.

## How it's built

- **Avalonia 12** draws the whole interface, from one C# codebase for Windows,
  macOS and Linux.
- **VLC 3.0.24, through libvlccore.** Conic doesn't go through the public
  LibVLC API. It loads libvlccore, VLC's core library, and starts each core
  with the same internal entry points LibVLC is built on
  (`libvlc_InternalCreate`, `libvlc_InternalInit`, `libvlc_InternalAddIntf`).
  That lets it do what VLC's own interfaces do from inside the core: pass each
  core its options, change variables on the playing input and audio output,
  and load its own plugins.
- **[VLCLR](https://github.com/mfkl/vlclr/tree/3.x)**, a C# framework for
  writing VLC 3 plugins, compiled with Native AOT. Conic's control interface
  and its video output are VLCLR plugins, so there is no native C shim.
- **Video inside Avalonia's composition.** The video output plugin hands
  decoded frames to Avalonia as GPU textures: a D3D11 shared texture on
  Windows, an IOSurface on macOS. The controls draw over the video in the same
  window, with hardware decoding on and no copy through the CPU on the
  validated paths.

## Design credit

The interface, its icons and its Settings pages follow the
[VLC Redesign](https://www.figma.com/community/file/1691465999134370980/vlc-redesign)
Figma Community file by [Juxtopposed](https://www.youtube.com/@juxtopposed),
presented in the video
[I Redesigned VLC After 25 Years](https://www.youtube.com/watch?v=ERFeOgqk324).
The design is Juxtopposed's work, shared on Figma Community, and Conic uses it
with credit. Where the app differs from the design, the difference is ours.

## Install

Preview builds are portable archives on the
[Releases](https://github.com/mfkl/conic/releases) page. Each one bundles VLC
3.0.24, so no separate VLC install is needed. The builds are not signed, so
each OS warns on the first launch.

- **Windows x64:** unzip `Conic-<version>-win-x64.zip` anywhere and run
  `Conic.exe`. If SmartScreen stops it, choose **More info**, then
  **Run anyway**.
- **macOS:** unzip `Conic-<version>-osx-<arch>.zip` (`arm64` for Apple
  silicon, `x64` for Intel) and move `Conic.app` to Applications. On the
  first launch macOS refuses to open it; open **System Settings > Privacy &
  Security** and choose **Open Anyway**.
- **Linux x64:** not available, because video output on Linux isn't written
  yet.

To remove the app, delete its folder (Windows) or the app (macOS). Settings
and history stay in your user profile.

### Reporting a problem

As in VLC, **Messages** shows the engine's log live: press **Ctrl+Shift+M**
(**Cmd+Shift+M** on macOS), or open it from the right-click menu (**More
Settings > Messages**), the macOS **Window** menu or **Settings > Advanced**.
Set **Verbosity** to **Debug**, reproduce the problem (opening the media again
if it fails to open), then **Save…** the log and attach it to your report.
Remember that Conic is not maintained.

## Platform status

| Platform | Playback | Checked on |
| --- | --- | --- |
| Windows x64 | D3D11 hardware decoding, GPU presentation, CPU fallback | Intel Iris Xe; 14-case package qualification |
| macOS arm64/x64 | VideoToolbox decoding, IOSurface presentation | M1 Pro; CI's arm64 and x64 runners |
| Linux x64 | Engine only (queue, transport, tracks, lifecycle); no video output yet, so the app reports playback as unavailable | CI, against Ubuntu's VLC 3 |

Windows checks cover H.264 (NV12) and HEVC Main 10 (P010) hardware decoding,
software decoding uploaded to the GPU, and the CPU bitmap fallback. Subtitles
are rendered by VLC and drawn above the video, so they also show with hardware
decoding. Multi-GPU switching, sleep and resume, and styled ASS or image
subtitles are not qualified.

## Avalonia Port Challenge

Conic is an entry in the
[Avalonia Port Challenge](https://avaloniaui.net/blog/avalonia-port-challenge).
The application ported is VLC's desktop player; VLC 3.0.23 (December 2025) is
the pre-contest baseline.

**What was migrated.** VLC 3's everyday desktop workflows: opening files,
folders and network streams, the queue, transport, tracks, subtitles and
delays, video adjustments, media information, history and resume, picture in
picture, the preferences that matter for playback, and VLC's log. The Qt
widgets were not translated. The workflows were rebuilt as Avalonia views over
an engine contract, with VLC 3's core underneath.

**The hard part** was video. VLC normally draws into its own native window,
which Avalonia controls can't sit on top of. Conic's video output is a C# VLC
plugin that passes decoded GPU frames to Avalonia's compositor, so video is
one more visual in the window. Proving a frame renders was only the start:
surface ownership, stale callbacks, stopping and reopening, and package
discovery each needed their own checks.

**Settings that are real.** The engine rows map to VLC 3 options or
variables, rows VLC 3 can't honor in this app were dropped rather than shown,
and smoke tests play real media to check the effect: the equalizer is measured
on the audio output, network options against a local server.

**Tests.** 557 unit tests, and about 40 smoke tests that drive the real app
with VLC on Windows and macOS: playback paths, every default shortcut, the
queue, menus, Settings and picture in picture.

AI assistance (Claude Code) was used for implementation and testing.
[The migration write-up](docs/WRITEUP.md) ·
[Before and after captures](docs/contest/COMPARISON.md) ·
[Evidence and status](docs/contest/STATUS.md)

## Building from source

### Repository layout

VLCLR is a git submodule at `external/vlclr`, tracking the `3.x` branch of
[mfkl/vlclr](https://github.com/mfkl/vlclr/tree/3.x). That branch also holds
the VLC 3 import library and the native layout probe. The pinned commit is
recorded in [`eng/versions.json`](eng/versions.json). Clone with submodules:

```
git clone --recursive https://github.com/mfkl/conic.git
# or, in an existing clone:
git submodule update --init
```

GitHub's "Download ZIP" leaves the submodule out; clone instead.

The architecture boundary is recorded in
[`docs/rfc/0001-port-boundaries.md`](docs/rfc/0001-port-boundaries.md), and the
pinned native and framework inputs in [`eng/versions.json`](eng/versions.json).

### Build and run

Windows x64 development requires PowerShell 7, .NET 10 (CI pins SDK
10.0.102), the Visual Studio C++ build tools and Windows SDK for Native AOT, and
the VLC 3 runtime pinned in `eng/versions.json`. `eng/get-vlc.ps1` downloads it
to `artifacts/dependencies/vlc-3.0.24`, where development runs find it.

```powershell
./eng/get-vlc.ps1
dotnet publish src/Conic.Vout -c Release -r win-x64

Copy-Item `
  src/Conic.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll `
  artifacts/dependencies/vlc-3.0.24/plugins/video_output/ `
  -Force

artifacts/dependencies/vlc-3.0.24/vlc-cache-gen.exe `
  artifacts/dependencies/vlc-3.0.24/plugins

dotnet run --project src/Conic
```

Smoke tests use `../vlclr/test_01.mkv` by default:

```powershell
dotnet run --project src/Conic -- `
  --smoke --expect-path=hardware-opaque-gpu
dotnet run --project src/Conic -- `
  --smoke --expect-path=software-bgra-gpu-upload
dotnet run --project src/Conic -- `
  --smoke --expect-path=cpu-bitmap
dotnet run --project src/Conic -- --ui-smoke
dotnet run --project src/Conic -- --engine-smoke --cycles=25
```

Override local paths with `VLCLR_ROOT`, `VLC_DIRECTORY`, and `CONIC_TEST_MEDIA`.
Installed applications find VLC in an adjacent `vlc` directory.
`VLC_DIRECTORY` takes precedence and must point to the directory containing the
native VLC libraries. The default plugin location is `plugins/video_output`
beneath that directory. `VLCLR_ROOT` only points development runs at a
directory containing the default smoke media (`test_01.mkv`).

On macOS, `eng/get-vlc.ps1` downloads the pinned VLC 3.0.24 DMG (an installed
`/Applications/VLC.app` also works), `eng/test-macos.ps1 -Media <clip> [-Window]`
runs the engine, IOSurface and window smokes, and `eng/package-macos.ps1`
builds an ad hoc signed `Conic.app`. See
[the cross-platform status](docs/CROSS_PLATFORM.md).

On Linux x64, `eng/test-linux-engine.ps1 -Media <clip>` builds the plugin and
app and runs the engine smokes against the installed VLC 3 packages
(`libvlccore9`, `libvlc5`, `vlc-plugin-base`).

### Packages

Build a portable Windows ZIP with the pinned VLC runtime, which
`eng/get-vlc.ps1` downloads from VideoLAN and checks against the SHA-256 in
`eng/versions.json`, then qualify it outside the repository:

```powershell
./eng/package.ps1 -VlcDirectory (./eng/get-vlc.ps1)
./eng/test-package.ps1 -Archive <path-to-zip> -Media <path-to-h264-fixture> -Cycles 25
```

CI builds the same ZIP, plus the macOS ZIPs, on pushes to `main` and on `v*`
tags, and publishes them as a GitHub release (see the
[CI guide](docs/CI.md#releases)).

### Tests

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release
./eng/test.ps1 -Scope Windows -Configuration Debug
```

Core tests need neither VLCLR nor native VLC. Desktop tests do not load native
VLC or open a window. Hosted CI runners don't qualify hardware decoding or
compositor behavior; those checks run on GPU machines. See
[CI setup](docs/CI.md), [video backends](docs/VIDEO_BACKENDS.md),
[D3D11 composition](docs/D3D11_COMPOSITION.md) and
[target qualification](docs/TARGET_QUALIFICATION.md).

## License

Conic is free software: you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software
Foundation, either version 2 of the License, or (at your option) any later
version. It is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text.

VLC is a trademark of VideoLAN.

# Building Conic

## Get the source

VLCLR is a git submodule at `external/vlclr`, tracking the `3.x` branch of
[mfkl/vlclr](https://github.com/mfkl/vlclr/tree/3.x). That branch also holds
the VLC 3 import library and the native layout probe. Clone with submodules
(GitHub's "Download ZIP" leaves the submodule out):

```
git clone --recursive https://github.com/mfkl/conic.git
# or, in an existing clone:
git submodule update --init
```

[`eng/versions.json`](../eng/versions.json) pins the native and framework
inputs, including the VLCLR commit and the VLC runtime.

## Windows

Requires PowerShell 7, .NET 10 (CI pins SDK 10.0.102), the Visual Studio C++
build tools and Windows SDK for Native AOT, and the VLC 3 runtime pinned in
`eng/versions.json`. `eng/get-vlc.ps1` downloads it to
`artifacts/dependencies/vlc-3.0.24`, where development runs find it.

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

`VLC_DIRECTORY` overrides where Conic looks for VLC and must point to the
directory containing the native VLC libraries; the video output plugin goes in
`plugins/video_output` beneath it. Installed applications find VLC in an
adjacent `vlc` directory.

## macOS

`eng/get-vlc.ps1` downloads the pinned VLC 3.0.24 DMG (an installed
`/Applications/VLC.app` also works). `eng/test-macos.ps1 -Media <clip> [-Window]`
runs the engine, IOSurface and window smokes, and `eng/package-macos.ps1`
builds an ad hoc signed `Conic.app`.

## Linux

Install VideoLAN's VLC snap (`sudo snap install vlc`) or the distribution's
VLC 3 packages (`libvlccore9`, `libvlc5`, `vlc-plugin-base`), and a VA-API
driver (`intel-media-va-driver` or `mesa-va-drivers`). Conic finds the snap
first; `VLC_DIRECTORY` overrides both. Hardware decoding needs VLC 3.0.22 or
later, which the snap provides.

`eng/test-linux.ps1 -Media <clip> [-Window] [-Headless]` builds the plugin and
app and runs the engine smokes and, with `-Window`, the video and UI smokes.
`-Headless` runs them in a private headless GNOME Shell, so no window reaches
the desktop. `eng/test-linux-engine.ps1` runs the engine smokes only, as CI
does. `eng/get-vlc.ps1` assembles the runtime the Linux package bundles from
the snap.

## Smoke tests

Smoke tests play the clip named by `CONIC_TEST_MEDIA`. The repository has no
media; `eng/make-settings-fixtures.ps1` generates synthetic clips with ffmpeg
into `artifacts/settings-fixtures`:

```powershell
./eng/make-settings-fixtures.ps1
$env:CONIC_TEST_MEDIA = "$PWD/artifacts/settings-fixtures/gop.mp4"
dotnet run --project src/Conic -- --smoke --expect-path=hardware-opaque-gpu
dotnet run --project src/Conic -- --smoke --expect-path=software-bgra-gpu-upload
dotnet run --project src/Conic -- --smoke --expect-path=cpu-bitmap
dotnet run --project src/Conic -- --ui-smoke
dotnet run --project src/Conic -- --engine-smoke --cycles=25
```

| What it checks | Smoke |
| --- | --- |
| Next Frame, Jump to Time, A-B loop, snapshots, chapters, programs, Hide UI | `--parity-smoke` |
| Live video effects, overlays, crop, deinterlacing, stereo mode, live subtitles | `--parity-smoke` |
| UDP, RTP, RTMP, FTP, SMB from local servers | `--protocols-smoke` |
| One instance: a second start hands its file over | two processes, by hand |

## Unit tests

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release
./eng/test.ps1 -Scope Windows -Configuration Debug
```

Core tests need neither VLCLR nor native VLC. Desktop tests do not load native
VLC or open a window. Hosted CI runners don't qualify hardware decoding or
compositor behavior; those checks run on GPU machines.

## Packages

Build a portable Windows ZIP with the pinned VLC runtime, which
`eng/get-vlc.ps1` downloads from VideoLAN and checks against the SHA-256 in
`eng/versions.json`, then qualify it outside the repository:

```powershell
./eng/package.ps1 -VlcDirectory (./eng/get-vlc.ps1)
./eng/test-package.ps1 -Archive <path-to-zip> -Media <path-to-h264-fixture> -Cycles 25
```

CI builds the Windows, macOS and Linux packages on pushes to `main` and on `v*`
tags, and publishes them as a GitHub release.

## Platform status

| Platform | Playback | Checked on |
| --- | --- | --- |
| Windows x64 | D3D11 hardware decoding, GPU presentation, CPU fallback | Intel Iris Xe; 14-case package qualification |
| macOS arm64/x64 | VideoToolbox decoding, IOSurface presentation | M1 Pro; CI's arm64 and x64 runners |
| Linux x64 | VA-API decoding, VA-API conversion, DMA-BUF presentation, CPU fallback; X11, and XWayland in Wayland sessions | Intel Iris Xe (iHD), GNOME 46; engine in CI |

The parity smokes run on Windows with hardware and software decoding, and on
Linux headless. Windows checks cover H.264 (NV12) and HEVC Main 10 (P010)
hardware decoding, software decoding uploaded to the GPU, and the CPU bitmap
fallback. Subtitles are rendered by VLC and drawn above the video, so they also
show with hardware decoding. Multi-GPU switching, sleep and resume, and styled
ASS or image subtitles are not qualified.

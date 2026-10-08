# Linux host TODO: GPU video output

Work to do on a native Linux machine with a GPU. The engine already runs and
is checked in CI ([CROSS_PLATFORM.md](CROSS_PLATFORM.md#linux-engine-port-october-8)).
What is missing is video: VA-API decoding, GPU conversion, and presenting the
frames through Avalonia's compositor. Nothing here may fall back to software
rendering.

Tick items as they pass and keep the evidence (logs, JSON reports, versions)
under `artifacts/` with the machine details from step 1.

## What WSL already established

- **Ubuntu 24.04's VLC (3.0.20) cannot hardware-decode on Intel.** Its
  packages contain no VA-API decoder: `vlc --list` shows only `vdpau_avcodec`,
  the VA-API filters and the `glconv_vaapi_*` OpenGL converters. Upstream VLC 3
  builds the VA-API decoder against current FFmpeg only since 3.0.22
  (`ba5dc03aec`, "Support VAAPI with latest FFmpeg"), and 3.0.24 fixed the DRM
  backend (`20fc39fc97`). Intel has no native VDPAU. **The Linux build needs the
  pinned VLC 3.0.24, built for Linux** (step 4), unless the host's distribution
  ships VLC ≥ 3.0.22 with `vaapi` in `vlc --list`.
- VLC 3 on Linux is relocatable: it loads plugins from
  `<directory of libvlccore.so.9>/vlc/plugins` (`src/linux/dirs.c`), plus
  `VLC_PLUGIN_PATH`. The runtime locator and `eng/package-linux.ps1` use that
  layout for a bundled runtime.
- In WSL, `--avcodec-hw=any` makes even the stock `cvlc` hang at input teardown
  (no real GPU). Engine smokes therefore decode in software; hardware decoding is
  qualified here, on the host.
- How VLC 3 shares VA-API pictures (from `modules/hw/vaapi/vlc_vaapi.c` and
  `modules/codec/avcodec/vaapi.c` at the pinned commit):
  - The `vaapi` decoder module does **direct rendering into the video output's
    pool**. It takes the VADisplay from the first pool picture's `picture_sys_t`
    (`vlc_vaapi_PicSysHoldInstance`) and decodes into the pool's VA surfaces.
    This is what VLC's own OpenGL output does (`glconv_vaapi` + `vlc_vaapi_PoolNew`).
  - The `vaapi_drm` module instead opens its own DRM display and only works when
    the output provides no VA pool. Our output should use direct rendering: it
    owns the display, so it can open the same GPU the compositor uses.
  - The decoder runs its own static copy of the `vlc_vaapi_*` helpers on our
    allocations: it increments and releases `vlc_vaapi_instance.pic_refcount`,
    calls `vaTerminate`, our `native_destroy_cb`, then `free()` on the last
    release. Allocate those structures with `NativeMemory.Alloc` (C `malloc`).
    The mirrors are `VLCVaapiInstance`, `VLCVaapiPoolInstance`,
    `VLCVaapiPictureSys` and `VLCVaapiPictureContext` in VLCLR.

## Prepared on branch `feature/linux-gpu-prep`

| Piece | Where | Checked |
| --- | --- | --- |
| VLC 3 VA-API picture layouts, `VAOP`/`VAO0` chromas | VLCLR `Native/VLCVaapiPicture.cs`, `VLCFourCC` | Layout probe (copies of the private structs) and managed tests |
| libva 2.x bindings: DRM display, surfaces, video processing, DRM PRIME export | `src/Shared/Linux/VaApi.cs` (linked into the Linux plugin and the app) | `tests/NativeLayout/vaapi_layout.c` against libva 2.20 headers; CI Native ABI job on Ubuntu; managed tests |
| Home thumbnails from the freedesktop thumbnail cache | `Services/FreedesktopThumbnail.cs` | Unit tests; not yet against a real desktop cache |
| Linux package (tarball; distribution or bundled VLC) | `eng/package-linux.ps1`, CI `Package / Linux x64` | Built in WSL; not launched with video |
| X11 rendering backend switch for diagnostics | `CONIC_X11_RENDERING=glx\|egl\|vulkan\|software` | Builds; probe results need a GPU |

## 1. Record the machine

- [ ] Distribution and version, kernel, desktop and session type (`echo $XDG_SESSION_TYPE`).
- [ ] GPU and drivers: `lspci -nn | grep -i vga`, Mesa version (`glxinfo -B`), VA driver (`vainfo | head`).
- [ ] .NET SDK (`dotnet --version`), compiler (`clang --version`).

## 2. Prerequisites

```sh
sudo apt install git clang zlib1g-dev libva-dev vainfo intel-media-va-driver-non-free \
  ffmpeg mesa-utils vulkan-tools
# .NET 10 SDK (CI pins 10.0.102) and PowerShell 7, then:
git clone --recursive https://github.com/mfkl/conic.git && cd conic
git switch feature/linux-gpu-prep   # or main once merged
```

## 3. Baseline (no GPU needed)

- [ ] `dotnet test conic.slnx -c Debug` passes.
- [ ] `cc -std=c11 tests/NativeLayout/vaapi_layout.c -o /tmp/vaapi-layout && /tmp/vaapi-layout` passes against the host's libva.
- [ ] `./eng/verify-native-abi.ps1 -VlcSourceRoot <vlc checkout at the pinned commit>` passes.
- [ ] With the distribution's VLC installed: `./eng/test-linux-engine.ps1 -Media <clip>` passes.

## 4. VA-API and the VLC runtime

- [ ] `vainfo` lists `VAProfileH264High`, `VAProfileHEVCMain10` with `VAEntrypointVLD`, and `VAProfileNone : VAEntrypointVideoProc`.
- [ ] GPU conversion to RGB works outside VLC:
  `ffmpeg -hwaccel vaapi -hwaccel_output_format vaapi -i clip.mkv -vf scale_vaapi=format=bgra -f null -`
- [ ] Check the distribution's VLC: `vlc --list | grep -i vaapi`. If there is no `vaapi` decoder module (expected on Ubuntu 24.04), build the pinned VLC:
  ```sh
  git clone https://code.videolan.org/videolan/vlc.git vlc-3.0.24 && cd vlc-3.0.24
  git checkout 6de05adcbaf2e8b85fe86aad4169393098628119
  sudo apt build-dep vlc            # needs deb-src entries; includes libva-dev and libavcodec-dev
  ./bootstrap
  ./configure --prefix="$PWD/install" --enable-libva --disable-qt --disable-skins2 --disable-lua
  make -j"$(nproc)" && make install
  ```
  Not tried yet; adjust the configure flags to what the host has.
- [ ] `install/bin/vlc --list | grep -E "vaapi"` shows the `vaapi` (and `vaapi_drm`) decoders.
- [ ] `install/bin/cvlc -vv --avcodec-hw=vaapi --vout=dummy clip.mkv` logs VA-API decoding (look for `Using VA-API` / `vaapi` in avcodec lines).
- [ ] `VLC_DIRECTORY="$PWD/install/lib" ./eng/test-linux-engine.ps1 -Media <clip>` still passes with the self-built runtime.
- [ ] Decide what ships: bundled 3.0.24 (`eng/package-linux.ps1 -VlcDirectory install/lib`) or a distribution with VLC ≥ 3.0.22. Record it in `eng/versions.json`.

## 5. What Avalonia can import

Run the probe in an X11 session and again in a Wayland session (Avalonia uses
X11 through XWayland there), for each backend:

```sh
dotnet build src/Conic -c Release
for mode in "" glx egl vulkan; do
  CONIC_X11_RENDERING=$mode dotnet src/Conic/bin/Release/net10.0/Conic.dll \
    --graphics-probe --report=artifacts/graphics-${XDG_SESSION_TYPE}-${mode:-default}.json
done
```

- [ ] Record `InteropAvailable`, `ImageHandleTypes`, `SemaphoreTypes` and synchronization for each.
- [ ] Pick the import route. Candidates, in order of preference:
  1. A DMA-BUF handle type, if Avalonia advertises one: import the exported RGB surface directly.
  2. `VulkanOpaquePosixFileDescriptor`: Avalonia imports Vulkan memory. VA-API exports DMA-BUF, not opaque Vulkan memory, so this needs our own Vulkan step (import the DMA-BUF with `VK_EXT_external_memory_dma_buf`, copy into an exportable image) before handing it over.
  3. Neither: write down the blocker; there is then no supported zero-copy route through Avalonia on this host.
- [ ] Spike: export one VPP-converted surface (below) and present it in a test window through the chosen route. Do this before writing the full output.

## 6. Linux video output (plugin)

In `src/Conic.Vout`, a Linux `vout display` registered from `PluginEntry`
next to `ControlInterface`, compiled only for `linux-x64`:

- [ ] Open: accept `VAOP` / `VAO0` (`VLCFourCC.IsVaapiOpaque`) and set
  `subpicture_chromas` to RGBA like the D3D11 output. Open the DRM render node
  of the compositor's GPU (`/dev/dri/renderD*`), `vaGetDisplayDRM`, `vaInitialize`.
- [ ] Pool: create NV12/P010 surfaces (`vaCreateSurfaces`), then a
  `VLCVaapiInstance`, `VLCVaapiPoolInstance` and one `VLCVaapiPictureSys` per
  picture, mirroring `vlc_vaapi_PoolNew`. Context copy/destroy callbacks hold and
  release the pool picture (`picture_Hold`/`picture_Release`).
- [ ] Display: read the surface from `pic->context` (`VLCVaapiPictureContext.Surface`),
  run VPP into one of a small ring of BGRA output surfaces
  (`VAProcPipelineParameterBuffer`, source region = visible area, BT.709/BT.601/BT.2020
  input, sRGB output), `vaSyncSurface`, export once per output surface with
  `vaExportSurfaceHandle(DRM_PRIME_2, READ_ONLY | COMPOSED_LAYERS)`, and publish
  fd, DRM fourcc, modifier, offset, pitch, size, generation and timestamp to
  the app through a new callback export (the Linux counterpart of
  `vlc_avalonia_set_gpu_frame_callback`).
- [ ] Subtitles, CPU frames, geometry and control queries: use `VoutHost`
  (shared by the D3D11 and macOS outputs since the macOS port), so the existing
  overlay control works. `VoutHost.Control` already reads VLC 3's non-Windows
  control numbering and `vout_display_cfg_t` layout.
- [ ] Bind libvlccore with `VLCCore.UseLibraryContaining(vlcSet)` in
  `PluginEntry`, as the macOS output does, before the display calls VLC.
- [ ] Close: release surfaces, contexts, the config, exported fds, and the instance reference.
- [ ] Telemetry: count VA pictures received, CPU pictures (must stay 0), pool generations, releases.

## 7. Linux video backend (app)

- [ ] `Video/Linux/VaapiVideoBackend` implementing `IVideoBackend`, created by
  `VideoBackendFactory` on Linux; import frames through the route from step 5,
  present with `CompositionSurfaceVisual`, handle resize, device loss and detach.
- [ ] Raise the same status and frame events as the D3D11 and IOSurface
  backends. `--ui-smoke` fails on any status containing `using CPU BGRA
  fallback`; `--adapter-mismatch-smoke` still matches D3D11 wording.
- [ ] Enable Linux in `NativePlaybackSupport`.

## 8. Qualification

Each in X11 and in Wayland:

- [ ] Hardware path smoke: VA-API pictures received, zero CPU pictures/uploads, a frame presented.
- [ ] `--engine-smoke --cycles=25` with the real video output: balanced releases.
- [ ] `--ui-smoke`, `--pip-smoke`, settings and video-adjustment smokes.
- [ ] H.264 and HEVC Main 10; subtitles; resize, fullscreen, PiP; suspend/resume if possible.
- [ ] Home thumbnails appear for files the desktop already thumbnailed.

## 9. Package and release

- [ ] `./eng/package-linux.ps1 [-VlcDirectory install/lib]`, then run the
  tarball from a fresh directory with no repository and no environment overrides.
- [ ] README: Linux requirements and run instructions; release notes with the
  tested distribution, GPU and session types.
- [ ] Attach the Linux tarball to the release job once qualified.

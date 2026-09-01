# Avalonia D3D11 composition path

## Verified state (2026-09-01)

The Windows path is a pure-C# Native AOT VLC 3 `vout display` module. Hardware
decoded NV12 and P010 pictures remain on the selected D3D11 device, are
converted by an `ID3D11VideoProcessor` into a keyed-mutex BGRA texture, and are
imported by Avalonia's public composition GPU interop.

```text
VLC D3D11VA decoder
  -> VLC-owned opaque NV12/P010 texture-array slice
  -> same-device D3D11 video-processor conversion
  -> shared DXGI_FORMAT_B8G8R8A8_UNORM texture
  -> keyed mutex producer 0 -> 1 / Avalonia consumer 1 -> 0
  -> CompositionSurfaceVisual with Avalonia controls above it
```

No decoder surface is mapped or read back on the hardware path. The existing
CPU BGRA paths remain available as explicit fallbacks:

- `software-bgra-gpu-upload`: software decode to VLC-owned BGRA followed by
  `UpdateSubresource` into the shared texture;
- `cpu-bitmap`: software decode to BGRA followed by a managed copy into an
  Avalonia `WriteableBitmap` when GPU sharing is unavailable.

## Conversion behavior

- The input view selects the `picture_sys_t` texture-array slice supplied by
  VLC.
- VLC's visible rectangle and offsets are used as the processor source rect;
  the shared texture uses the visible output dimensions.
- Identity, 90-degree, 180-degree, and 270-degree orientations are applied by
  the video processor. Mirrored/transposed orientations are rejected with
  machine-readable fallback telemetry because the selected processor API does
  not provide the required reflection operation.
- VLC BT.601/BT.709 matrix metadata and limited/full nominal range are passed
  to the legacy D3D11 color-space controls. BT.2020 uses the closest legacy
  matrix flag.
- PQ and HLG HDR are deliberately unsupported. The shared target is SDR BGRA8,
  and this path does not claim deterministic tone mapping or propagate HDR
  mastering metadata. Such input reports `video-processor-conversion-failed`
  rather than silently presenting incorrectly tone-mapped output.
- Processor/enumerator/output-view resources are recreated when source format,
  source dimensions, output dimensions, or shared-texture generation changes.
  Each playback reopen creates a new producer device generation.
- Producer mutex acquisition waits at most 16 ms. HRESULT failures distinguish
  processor creation/conversion from D3D11 device removal.

## Machine-readable smoke paths

`--smoke` requires `--expect-path`; invoking it without an expected path is a
validation error. Accepted values are:

```text
hardware-opaque-gpu
software-bgra-gpu-upload
cpu-bitmap
```

The hardware expectation requires an opaque picture, a successfully opened
and synchronized BGRA shared texture, and no increase in the CPU-upload
counter. The software-upload expectation requires both CPU-picture and upload
counters to increase. The bitmap expectation requires an actual CPU callback
frame.

## WP-07 qualification evidence

The following was run on adapter LUID `00000000:000196FF`, Intel Iris Xe, using
VLC `3.0.24-beta1` and VLCLR commit
`dca5e306785e389257843a9121e10bb500054d9b`.

- Generated H.264 8-bit fixture: VLC explicitly reported D3D11VA;
  source `DX11`, `DXGI_FORMAT_NV12` (103); result
  `hardware-opaque-gpu`, 640x480 BGRA shared texture, zero CPU uploads, and
  balanced opaque releases.
- Generated HEVC Main 10 fixture: VLC explicitly reported D3D11VA;
  source `DX10`, `DXGI_FORMAT_P010` (104); result
  `hardware-opaque-gpu`, 640x480 BGRA shared texture, zero CPU uploads, and
  balanced opaque releases.
- Software-forced fixture: result `software-bgra-gpu-upload`; CPU-picture and
  CPU-upload counters both advanced.
- Adapter-disabled fixture: result `cpu-bitmap`; the CPU-picture counter
  advanced and the CPU-upload counter remained zero.
- Real Avalonia UI smoke: adapter match true, overlay composition presented,
  keyed-mutex progress survived resize and fullscreen transitions, and reopen
  advanced device/resource generations on the same adapter.
- Engine smoke: pause, seek, resume, volume, stop, dispose, reopen, and 25
  additional open/stop/dispose cycles passed. Final counters were 1,120 opaque
  allocations and 1,120 releases, release balance zero, CPU uploads zero.
- Native AOT export inspection included both VLC entry points and all host
  callback/configuration exports. The regenerated VLC cache listed both the
  vout-display and interface descriptions from the same DLL.

Generated fixtures live only in the system temporary directory and are not
repository inputs. The repository's current `test_01.mkv` fixture is MPEG-4
Part 2, so H.264 and P010 qualification must continue to generate or provide a
codec-specific fixture explicitly through `CONIC_TEST_MEDIA`.

## Build and validation

```powershell
dotnet build conic.slnx -c Release
dotnet test conic.slnx --no-build -c Release
dotnet publish src/Conic.Vout -c Release -r win-x64
```

After deploying the Native AOT DLL and regenerating VLC's plugin cache, run VLC
operations from Git Bash:

```bash
timeout 45s dotnet run --no-build --project src/Conic -- \
  --smoke --expect-path=hardware-opaque-gpu
timeout 45s dotnet run --no-build --project src/Conic -- \
  --smoke --expect-path=software-bgra-gpu-upload
timeout 45s dotnet run --no-build --project src/Conic -- \
  --smoke --expect-path=cpu-bitmap
timeout 45s dotnet run --no-build --project src/Conic -- --ui-smoke
timeout 120s dotnet run --no-build --project src/Conic -- \
  --engine-smoke --cycles=25
```

## Remaining limits

- HDR tone mapping and metadata propagation are not implemented.
- Mirrored/transposed orientation variants need a reviewed shader or an
  additional transform stage.
- Codec/driver coverage beyond the validated H.264 NV12 and HEVC Main 10 P010
  fixtures is not established.
- Multi-GPU behavior has a safe mismatch fallback but has not been qualified on
  a physical multi-GPU machine.
- Subtitle/subpicture composition is not implemented by this vout.
- macOS and Linux composition work remains outside this Windows package.

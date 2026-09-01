# VLC Avalonia

An early modern Avalonia desktop UI for VLC 3. The Windows video path uses a
pure-C# Native AOT VLC `vout display` plugin and a keyed-mutex D3D11 shared
texture, allowing video and Avalonia controls to live in the same composition
tree without child-window airspace limitations.

The authoritative architecture boundary is recorded in
[`docs/rfc/0001-port-boundaries.md`](docs/rfc/0001-port-boundaries.md), and the
pinned native/framework inputs are recorded in [`eng/versions.json`](eng/versions.json).
The dispatch-ready Windows work packages, validation gates, and commit
boundaries are in [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md).

## Current state

- Pure C#; no native C shim.
- VLC 3 internal-core playback through the sibling VLCLR repository.
- D3D11 shared-texture transport from the managed vout to Avalonia.
- Public Avalonia `ICompositionGpuInterop` import and
  `CompositionSurfaceVisual` presentation.
- Same-adapter D3D11 video-processor conversion from hardware-decoded NV12 or
  P010 into the shared BGRA composition texture, with zero CPU frame uploads
  on the validated hardware paths.
- CPU BGRA fallback when the active Avalonia backend cannot import D3D11
  keyed-mutex images.
- Cross-device and real-compositor smoke modes.
- An engine-neutral command/state contract with generation-safe snapshots.
- Working open, play/pause, seek, volume, stop, dispose, and reopen plumbing.

Windows qualification covers H.264/NV12 and HEVC Main 10/P010 hardware decode
on Intel Iris Xe, plus software-BGRA GPU upload and CPU-bitmap fallback. See
the composition document for measured evidence and explicit coverage limits.

## Repository layout

This first version intentionally uses a project reference to VLCLR:

```text
videolabs/
  vlclr/
  vlc-avalonia/
```

VLCLR and the pinned VLC 3 runtime remain in `../vlclr`. NuGet package
references can replace the project references later.

## Build and run

```powershell
dotnet publish src/VLCAvalonia.Vout -c Release -r win-x64

Copy-Item `
  src/VLCAvalonia.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll `
  ../vlclr/vlc-3.0.24-beta1/plugins/video_output/ `
  -Force

../vlclr/vlc-3.0.24-beta1/vlc-cache-gen.exe `
  ../vlclr/vlc-3.0.24-beta1/plugins

dotnet run --project src/VLCAvalonia
```

Smoke tests use `../vlclr/test_01.mkv` by default:

```powershell
dotnet run --project src/VLCAvalonia -- `
  --smoke --expect-path=hardware-opaque-gpu
dotnet run --project src/VLCAvalonia -- `
  --smoke --expect-path=software-bgra-gpu-upload
dotnet run --project src/VLCAvalonia -- `
  --smoke --expect-path=cpu-bitmap
dotnet run --project src/VLCAvalonia -- --ui-smoke
dotnet run --project src/VLCAvalonia -- --engine-smoke --cycles=25
```

Override local paths with `VLCLR_ROOT`, `VLC_DIRECTORY`, and
`VLC_AVALONIA_TEST_MEDIA`.

See [docs/D3D11_COMPOSITION.md](docs/D3D11_COMPOSITION.md) for the validated
implementation, qualification commands, measured counters, and remaining work.

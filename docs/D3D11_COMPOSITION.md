# Avalonia Composition Vout - TODO

## Current State (verified 2026-08-31)

The architecture is now a custom VLC `vout display` module. The invalid
`vout_window`/shared-handle experiment has been removed.

### Working

1. **Pure-C# VLC vout display plugin** (`src/Conic.Vout`)
   - Native AOT DLL; no C shim.
   - Registers `vlc_avalonia` with capability `vout display`.
   - Implements VLC 3 `open`, `pool`, `prepare`, `display`, `control`, and
     `close` callbacks.
   - Negotiates CPU BGRA pictures through a VLC-owned picture pool.
   - Uploads each displayed picture into a pure-C# TerraFX D3D11 texture
     created with `D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX`.
   - Exports `vlc_avalonia_set_gpu_frame_callback` with the non-owning
     global shared handle, dimensions, resource generation, and VLC PTS.
   - Retains `vlc_avalonia_set_frame_callback` as a CPU fallback.

2. **ABI-accurate framework types**
   - `VLCVoutDisplay`, `VLCVoutDisplayInfo`, and `VLCVoutDisplayOwner`.
   - `vout_display_t` is validated as 504 bytes on VLC 3 win64.
   - Existing validated `VLCVideoFormat`, `VLCPicture`, and `VLCPlane` types
     are used instead of the old simplified duplicates.

3. **Avalonia D3D11 composition surface** (`src/Conic`)
   - Receives shared-texture descriptors through a Cdecl callback into the
     CoreCLR host.
   - Imports the global D3D11 handle through Avalonia 12's public
     `ICompositionGpuInterop` API.
   - Presents it with `CompositionDrawingSurface.UpdateWithKeyedMutexAsync`;
     VLC publishes key `0 -> 1` and Avalonia consumes key `1 -> 0`.
   - Uses a `CompositionSurfaceVisual`, so Avalonia overlays remain in the
     same visual tree without HWND airspace limitations.
   - Falls back to the proven `WriteableBitmap` CPU path when the current
     Avalonia graphics backend cannot import keyed-mutex D3D11 images.
   - Uses the internal VLC core lifecycle rather than public LibVLC playback.

4. **GPU transport smoke tests**
   - `dotnet run --project src/Conic -- --smoke`
   - Verified a second D3D11 device can open the exported handle, acquire key
     `1`, inspect a 640x480 `DXGI_FORMAT_B8G8R8A8_UNORM` texture, and release
     key `0`.
   - `dotnet run --project src/Conic -- --ui-smoke`
   - Verified the real Avalonia compositor imports and presents a GPU frame.

5. **Validation**
   - `dotnet test ../vlclr/src/VLCLR.Tests` passes: 326 tests.
   - `dotnet build src/Conic` passes; VLCLR still reports
     four pre-existing CA1420 warnings in the legacy delegate declarations.
   - Native AOT publish succeeds and exports:
     - `vlc_entry__3_0_0f`
     - `vlc_entry_copyright__3_0_0f`
     - `vlc_entry_license__3_0_0f`
     - `vlc_avalonia_set_frame_callback`
     - `vlc_avalonia_set_gpu_frame_callback`
   - VLC plugin cache contains `vlc_avalonia`.

## Current Pipeline

```text
VLC decoder/converter
  -> VLC-owned CPU BGRA picture pool
  -> managed vout D3D11 upload
  -> keyed-mutex shared BGRA texture
  -> exported Cdecl handle callback
  -> Avalonia GPU import + CompositionSurfaceVisual
  -> Avalonia visual tree and overlay controls
```

There is no pixel copy between the managed VLC plugin and Avalonia. The current
vout still performs one CPU-to-GPU upload because VLC is supplying CPU BGRA
pictures. This is a zero-copy shared-texture transport, not yet end-to-end
zero-copy hardware decoding.

## Next: End-to-End GPU Decode

1. Add ABI-accurate VLC 3 D3D11 picture-system/context access in C#.
2. Select the Avalonia compositor adapter by LUID when creating the producer
   device, including multi-GPU systems.
3. Make the managed vout expose a D3D11 opaque GPU picture pool so VLC can keep
   hardware-decoded frames on the GPU.
4. Convert NV12/P010 decoder surfaces into the existing shareable BGRA texture
   with a D3D11 video processor or shader.
5. Recreate resources safely on video format change, device loss, or adapter
   change.
6. Validate subtitles, transparency, animated Avalonia controls, resize,
   fullscreen, HDR/SDR behavior, and teardown.

## Playback/UI Work After GPU Proof

- Expose play/pause, seek, position, duration, volume, tracks, and playlist
  state from the internal VLC core.
- Replace the spike window with the actual Avalonia application shell.
- Add pointer coordinate mapping from the Avalonia video visual to VLC video
  coordinates.

## Build and Run

```powershell
dotnet publish src/Conic.Vout -c Release -r win-x64
Copy-Item `
  src/Conic.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll `
  ../vlclr/vlc-3.0.24-beta1/plugins/video_output/ `
  -Force
../vlclr/vlc-3.0.24-beta1/vlc-cache-gen.exe `
  ../vlclr/vlc-3.0.24-beta1/plugins

dotnet run --project src/Conic -- --smoke
dotnet run --project src/Conic -- --ui-smoke
dotnet run --project src/Conic
```

## Removed Design

- Do not put a D3D11 shared-resource handle in `vout_window_t.handle.hwnd`.
- Do not use a `vout_window` callback as a picture/display callback.
- Do not use a native child HWND for the main composition path.
- Do not reintroduce the simplified `VideoFormat`, `Plane`, or `Picture`
  layouts formerly added to `VLCCore.cs`.

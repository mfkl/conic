# VLCLR main compatibility experiment (historical)

Updated: 2026-09-10. Status: migration proposal superseded by the user's explicit
VLC 3 engine requirement. Playback result was accepted on 2026-09-10 after
qualification on the restored VLC 3 baseline.

The active [UI fidelity plan](VLC4_SCREEN_BY_SCREEN_PLAN.md) keeps Avalonia over
VLCLR/VLC 3 and uses VLC 4 only as UI reference. Resume native qualification with
the compatible framework/runtime pins in `eng/versions.json`, selected through
an isolated checkout if necessary. No additional engine-direction clarification
is needed. The experiment and failed build below remain historical evidence;
they do not block planning or require a VLC 4 migration.

Recovery has now been validated against the isolated VLC 3 pin. See
[the Playback review](PLAYBACK_REVIEW_2026-09-10.md) for Debug/Release builds,
156 tests in each configuration, native results and actual dependency paths.

The earlier request was to use latest VLCLR main instead of the VLC 3 transport
branch. `git -C ../vlclr fetch private main` resolved to
`56499bd055eef1154bfe6e6c0840f72894adaade`. The sibling working checkout is on
`feature/gpu-shader-filters` at `ad7982b`; its branch and files were preserved.
An archive of the fetched main revision is available locally under ignored
`artifacts/vlclr-main-56499bd`.

## Verified incompatibilities

Main's `eng/compatibility.json` declares VLC 4.x and plugin API 4.0.6. The current
product's `eng/versions.json` records VLC 3.0.24-beta1 and module ABI `3_0_0f`.
That manifest remains the historical product baseline until a replacement
runtime/framework combination is validated.

| Area | Current product | Latest main / required work |
|---|---|---|
| Module registration | Exports `vlc_entry__3_0_0f` and VLC 3 license/copyright entry points | Requires VLC 4 module registration and a matching runtime/import library |
| Main playlist acquisition | Borrows `VLCObjectHeader.Parent` from the interface object | VLC 4 object header has no Parent field; use the reviewed VLC 4 interface/playlist API |
| Playback and queue | `VLCTransportControl`, `VLCPlaylistQueue`, generation-safe queue and smoke contracts | Those types are absent; main has `VLCPlaylist` and `VLCPlayer`, whose behavior and ownership must be adapted and qualified |
| Tracks, delays, metadata, settings | `VLCMediaCapabilities` and an instance `VLCPlayerCapabilities` facade | Facades and their smoke contracts are absent; the same-named native static capabilities class is not a replacement |
| Video output | `VLCVoutDisplay`, `VLCPicturePoolConfiguration`, `VLCD3D11PictureSys` | Required vout/pool wrappers are absent; main uses a different D3D11 picture-context representation |
| Headless video extraction | Playback requires audio, transport, queue, and composed presentation | Main's `VLCCoreRuntime` documents sequential frame extraction independent of the playback player; it cannot replace the engine directly |

The native layouts cannot be repaired with aliases or by changing only the
module entry-point suffix. Reusable ABI declarations and native ownership
helpers belong in VLCLR, as specified in RFC 0001.

## Current validation

Executed from the product root against the fetched main archive:

```powershell
$env:VLCLR_ROOT = Join-Path $PWD 'artifacts\vlclr-main-56499bd'
dotnet build conic.slnx -c Release -m:1 -p:NuGetAudit=false
```

Result: restore succeeded; build failed with 160 errors and zero warnings.
Missing queue/media/vout types above are among the compiler errors. Full output
is in ignored `artifacts/playback-main-build-release.log`. No native Playback
result is claimed for main. `VLCLR_ROOT` now selects the build project references
and native import library as well as the existing runtime repository lookup.

The independent managed suites still pass:

```powershell
dotnet test tests/Player.Application.Tests -c Release --no-restore
dotnet test tests/Player.Engine.ContractTests -c Release --no-restore
```

Results: 55 application tests and 8 engine-contract tests passed, with no failed
or skipped tests. These suites do not load the native VLC engine and therefore
do not establish VLC 4 compatibility. `git diff --check` also passed.

## Superseded migration work (not the active Playback prerequisite)

The following was the proposed work before the user reaffirmed VLC 3. Retained
for context only; follow F0 and Playback in the active UI plan instead.

1. Resolve whether the native target moves to VLC 4 or main gains explicit VLC
   3 compatibility. The request establishes latest main as the framework target;
   the previously accepted product architecture explicitly used VLC 3.
2. Record a matching runtime, SDK/header revision, compiler ABI, import library,
   and framework commit. An installed VLC 4 bundle alone does not qualify their
   compatibility.
3. Implement and validate the required framework boundaries, then adapt the
   product engine and custom vout while preserving the existing UI contracts.
4. Run Debug/Release managed tests, Native AOT export/cache checks, CPU and
   hardware composition, queue/media controls, 0--200% volume readback,
   stop/reopen, and lifecycle smokes with bounded native execution.
5. Refresh Playback captures and present the running Release result for Gate
   P2. Home/Library, Settings, and cross-screen integration follow in order.

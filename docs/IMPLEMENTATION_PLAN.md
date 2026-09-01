# VLC Avalonia Windows implementation plan

Status: ready for delegated implementation
Planning baseline: 2026-09-01
In scope: Windows product and shared VLCLR work
Explicitly out of scope for this batch: macOS and Linux rendering spikes

## 1. Target outcome

Complete the current Windows vertical slice without weakening the repository
boundary in RFC 0001:

1. VLCLR `3.x` contains the reusable VLC 3 ABI and ownership-safe helpers.
2. `vlc-avalonia` consumes one recorded VLCLR commit.
3. The application can open, play, pause, seek, resume, change volume, stop,
   dispose, and reopen the deterministic fixture.
4. The VLC interface submodule supplies the actual main `playlist_t` instance;
   the product does not guess its location with `vlc_object_find_name`.
5. H.264 hardware decoding remains on the GPU through the custom vout, is
   converted to the shared BGRA composition texture on the same D3D11 adapter,
   and is presented by Avalonia without a CPU frame upload.
6. CPU BGRA remains a tested fallback.
7. Every work package ends with its specified validation and commit. Nothing is
   pushed automatically.

The work is not complete merely because the solution builds. The final native
smoke must prove playback controls, reopen, opaque D3D11 input, zero CPU upload
on the hardware path, Avalonia composition, and clean teardown.

## 2. Current working-tree baseline

Agents must preserve this state; it is intentional work in progress.

### VLCLR

- Repository: `../vlclr`
- Branch: `3.x`, two commits ahead of `private/3.x`
- Landed commits:
  - `46808a3 feat: add VLC 3 vout display ABI support`
  - `415e69d build: disable runtime marshalling for Native AOT`
- Uncommitted framework edits:
  - fixed-signature playlist/object imports in `src/VLCLR/Native/VLCCore.cs`;
  - float and Boolean variable accessors in `src/VLCLR/VLCVariable.cs`.
- Last verified framework result before these uncommitted edits: 326 tests pass.

### vlc-avalonia

- Repository: this repository
- Branch: `main`, equal to `origin/main` before the working-tree changes.
- Existing uncommitted work includes:
  - `Player.Contracts`, `Player.Domain`, and three contract tests;
  - playback state/control wiring and the redesigned XAML shell;
  - RFC, licence notice, version record, and fixture manifest;
  - a partially implemented `vlc_avalonia_control` interface submodule.
- Debug solution build and the three managed contract tests passed before the
  interface-submodule patch.
- Native smoke currently fails because `vlc_object_find_name(core,
  "playlist")` returns null. The partial interface submodule is intended to
  replace that failed approach, but it is not yet connected to the host bridge.

## 3. Dispatch rules for Sonnet subagents

Give each subagent exactly one work package from Section 5. Include the package
ID, repository, prerequisites, allowed files, acceptance criteria, validation
commands, and commit message in its prompt.

All agents must follow these rules:

1. Run `git status --short --branch` and inspect the relevant diff before edits.
2. Do not reset, clean, stash, overwrite, or reformat unrelated user changes.
3. Read the local `AGENTS.md` for every repository touched.
4. Do not edit the other repository unless the package explicitly lists it.
5. Do not claim a native behavior from a successful build or from an existing
   document; run the package's native validation.
6. On Windows, run VLC/cache/smoke operations through Git Bash. Bound every
   smoke with a timeout so a VLC shutdown regression cannot hang the agent.
7. Stage only the package's files with `git add -- <explicit paths>`.
8. Commit only after all required validation passes. If validation cannot pass,
   leave the changes uncommitted and report the exact command and failure.
9. Never amend, rebase, squash, push, or change branches unless the package says
   to do so.
10. Report the commit hash, validations run, results, remaining working-tree
    changes, and any follow-up risk.

Because the agents share a working tree, packages in the same wave are not safe
to run concurrently unless each agent has a separate Git worktree. The default
dispatch is sequential in the order below.

## 4. Validation conventions

Run managed commands from `vlc-avalonia` unless a package says otherwise:

```powershell
dotnet build vlc-avalonia.slnx --no-restore
dotnet test vlc-avalonia.slnx --no-build --no-restore
```

Run framework validation from `../vlclr`:

```powershell
dotnet test src/VLCLR.Tests
```

Publish and deploy the Native AOT plugin:

```powershell
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
dotnet publish src/VLCAvalonia.Vout -c Release -r win-x64
Copy-Item -LiteralPath `
  'src/VLCAvalonia.Vout/bin/Release/net10.0/win-x64/native/libvlc_avalonia_plugin.dll' `
  -Destination '../vlclr/vlc-3.0.24-beta1/plugins/video_output/libvlc_avalonia_plugin.dll' `
  -Force
```

Regenerate the cache and execute native smoke from Git Bash:

```bash
cd /c/Users/Martin/Code/videolabs/vlclr
timeout 30s ./vlc-3.0.24-beta1/vlc-cache-gen.exe \
  ./vlc-3.0.24-beta1/plugins

cd /c/Users/Martin/Code/videolabs/vlc-avalonia
timeout 30s dotnet run --no-build --project src/VLCAvalonia -- --smoke
timeout 30s dotnet run --no-build --project src/VLCAvalonia -- --ui-smoke
```

If publish reports access denied in `obj` from a sandboxed agent, rerun the same
publish with the environment's normal escalation mechanism. Do not delete build
directories to work around a lock without first identifying the exact path and
confirming it is only generated output.

## 5. Work packages

### WP-01 — Finish and commit VLC 3 control bindings

Repository: `../vlclr`
Prerequisites: none
May edit:

- `src/VLCLR/Native/VLCCore.cs`
- `src/VLCLR/VLCVariable.cs`
- focused files under `src/VLCLR.Tests/`

Tasks:

1. Review every new import against the pinned VLC 3 headers and the actual
   `libvlccore.dll` export table.
2. Keep fixed-signature functions such as `playlist_CurrentInput`,
   `playlist_Status`, volume, mute, hold, and release in VLCLR.
3. Do not add a normal P/Invoke declaration for variadic `playlist_Control` or
   `input_Control`. Those require a fixed native wrapper or an explicitly
   Windows-only, reviewed call boundary in the product.
4. Keep `vlc_object_find_name` only as a raw exported API; document that it is
   not the main-playlist acquisition mechanism.
5. Add unit tests for managed variable-value conversion and constants where no
   native instance is required. Do not call context-sensitive native APIs with
   fabricated pointers.

Acceptance:

- The code documents ownership of every returned pointer.
- The current input is explicitly described as held and requiring
  `ObjectRelease`.
- No analyzer or compiler error is added beyond the four already-known legacy
  marshalling warnings.

Validation:

```powershell
dotnet test src/VLCLR.Tests
dotnet build src/VLCLR -c Release
git diff --check
```

Commit:

```text
feat: add VLC 3 core playback control bindings
```

After commit, report the full 40-character hash. WP-03 must put that hash in
`eng/versions.json`.

### WP-02 — Complete the main-playlist interface bridge

Repository: `vlc-avalonia`
Prerequisite: WP-01 committed
May edit:

- `src/VLCAvalonia.Vout/CompositionVoutPlugin.cs`
- `src/VLCAvalonia/Interop/CompositionPluginBridge.cs`
- `src/VLCAvalonia/Interop/VLCCorePlayer.cs`
- `src/VLCAvalonia/Interop/CompositionSmoke.cs`
- focused engine tests

Tasks:

1. Complete the partially added `vlc_avalonia_control` interface submodule.
   Its open callback receives `intf_thread_t*`; use the ABI-validated
   `VLCObjectHeader.Parent` value as the borrowed main `playlist_t*`.
2. Export and consume `vlc_avalonia_set_playlist_callback`.
3. Pass a per-bridge `GCHandle` as callback userdata. Remove the process-global
   `CompositionPluginBridge.s_active` lookup for CPU, GPU, and playlist
   callbacks.
4. Make `VLCCorePlayer` start VLC with `--intf=vlc_avalonia_control`, wait for
   the playlist callback with a bounded timeout, and stop using
   `ObjectFindName(core, "playlist")`.
5. Define the playlist pointer as borrowed for the interface/core lifetime.
   Clear it before core cleanup and never release it independently.
6. Make callback disconnect and disposal idempotent. Catch exceptions at every
   unmanaged boundary.
7. Exercise open, pause, seek, resume, volume, stop, dispose, and reopen in a
   deterministic `--engine-smoke` mode. Each stage must have its own timeout and
   useful failure text.
8. Verify the plugin cache exposes both `vout display` and `interface`
   submodules from the same DLL.

Acceptance:

- Main-playlist acquisition succeeds without name lookup.
- No process-global current player/bridge remains.
- A late callback after disconnect cannot access a freed `GCHandle`.
- Stop and reopen do not hang or use a pointer after core cleanup.

Validation:

```powershell
dotnet build vlc-avalonia.slnx
dotnet test vlc-avalonia.slnx --no-build
dotnet publish src/VLCAvalonia.Vout -c Release -r win-x64
```

Then deploy/cache and run, through Git Bash:

```bash
timeout 45s dotnet run --no-build --project src/VLCAvalonia -- --engine-smoke
timeout 30s dotnet run --no-build --project src/VLCAvalonia -- --smoke
```

Commit:

```text
feat: add VLC 3 playback lifecycle controls
```

### WP-03 — Stabilize contracts, UI wiring, and repository metadata

Repository: `vlc-avalonia`
Prerequisites: WP-01 and WP-02 committed
May edit:

- `src/Player.Contracts/`
- `src/Player.Domain/`
- `tests/Player.Engine.ContractTests/`
- `src/VLCAvalonia/Controls/CompositionVideoSurface.cs`
- `src/VLCAvalonia/MainWindow.axaml`
- `src/VLCAvalonia/MainWindow.axaml.cs`
- `src/VLCAvalonia/Program.cs`
- project/solution files
- `README.md`, `LICENSE`, `eng/versions.json`, `docs/rfc/`, `test-assets/`

Tasks:

1. Preserve and finish the current redesigned XAML shell.
2. Keep native types and pointers out of `Player.Contracts`, `Player.Domain`,
   views, and view-facing state.
3. Ensure open increments the generation and late snapshots are discarded.
4. Wire play/pause, seek, volume, and stop to `IMediaEngine`; disable seek when
   `CanSeek` is false and avoid feedback loops while state updates sliders.
5. Add contract tests for open reset, stale-generation rejection, value
   clamping, fault, stop, and reopen transitions.
6. Fix Debug and Release builds; `WithDeveloperTools` must compile only when its
   diagnostics package/namespace is available.
7. Replace the VLCLR hash in `eng/versions.json` with the WP-01 commit.
8. Confirm the fixture size and SHA-256 in `test-assets/README.md`.
9. Ensure documentation calls CPU BGRA a fallback and does not claim completed
   zero-copy hardware decoding before WP-07.

Acceptance:

- Debug and Release builds pass.
- All contract controls invoke real engine commands.
- State events are marshalled to the Avalonia dispatcher before UI mutation.
- The version manifest contains the exact existing VLC and VLCLR commits.

Validation:

```powershell
dotnet build vlc-avalonia.slnx
dotnet build vlc-avalonia.slnx -c Release --no-restore
dotnet test vlc-avalonia.slnx --no-build --no-restore
git diff --check
```

Commit:

```text
feat: add engine contracts and functional player controls
```

If documentation changes remain cleanly separable after the functional commit,
commit them as:

```text
docs: record VLC Avalonia architecture and pinned inputs
```

### WP-04 — Add ABI-verified VLC 3 D3D11 picture types

Repository: `../vlclr`
Prerequisites: WP-01 committed
May edit:

- `src/VLCLR/Native/`
- `src/VLCLR.Tests/`
- an existing ABI-probe area under `tests/` or `eng/`

Tasks:

1. Use the exact pinned VLC source commit
   `5fbc6904a2e4a96e9956a5d8503ac4db4ebaf702`, not the older local
   `libvlc-nuget/vlc` checkout, as the source of truth.
2. Model only the required VLC 3 Win64 structures and callbacks:
   `picture_sys_t`, `picture_resource_t`, and
   `picture_pool_configuration_t`, plus any D3D11 format constants directly
   reachable from them.
3. Record pointer ownership and COM reference rules for texture, context,
   decoder view, processor views, resource views, and picture destruction.
4. Add a native C layout probe compiled with the same MSVC ABI as the pinned
   VLC binary. Compare size and every managed field offset used by the product.
5. Add bindings for fixed exported picture/pool functions. Classify macros,
   inlines, callbacks, and variadic APIs explicitly rather than pretending they
   are direct imports.

Acceptance:

- C probe and managed layout tests agree for x64.
- The ABI types contain no TerraFX/Avalonia/product dependencies.
- Every pointer field has an ownership comment.

Validation:

```powershell
dotnet test src/VLCLR.Tests
dotnet build src/VLCLR -c Release
```

Run the new native layout probe and include its output in the agent report.

Commit:

```text
feat: add VLC 3 D3D11 picture ABI
```

### WP-05 — Select and expose the Avalonia compositor adapter

Repository: `vlc-avalonia`
Prerequisites: WP-02, WP-03, and WP-04 committed
May edit:

- Windows-specific composition/device classes in `src/VLCAvalonia/`
- callback exports/bridge needed to exchange adapter identity
- focused tests and smoke telemetry

Tasks:

1. Obtain the Avalonia compositor's D3D11 adapter LUID through supported public
   interop. Do not choose the default hardware adapter independently.
2. Pass adapter identity to the vout before playback starts.
3. Create the producer device on that adapter with BGRA and video support and
   enable D3D11 multithread protection where VLC decoder/vout threads require
   it.
4. Fail explicitly when the requested adapter cannot be created. CPU fallback
   may activate, but the smoke must report why.
5. Add telemetry containing adapter LUID, device generation, source chroma,
   source DXGI format, and fallback reason. Do not log raw pointers/handles.
6. Cover adapter/resource recreation on detach and reopen.

Acceptance:

- Producer and Avalonia consumer report the same adapter LUID.
- A mismatched-adapter test fails safely instead of presenting corrupt frames.
- The existing CPU fallback still displays a frame.

Validation:

```powershell
dotnet build vlc-avalonia.slnx -c Release
dotnet test vlc-avalonia.slnx --no-build -c Release
dotnet publish src/VLCAvalonia.Vout -c Release -r win-x64
```

Deploy/cache and run both native smoke modes through Git Bash.

Commit:

```text
feat: align VLC vout with the Avalonia D3D11 adapter
```

### WP-06 — Implement the VLC D3D11 opaque picture pool

Repository: `vlc-avalonia`
Prerequisites: WP-04 and WP-05 committed
May edit:

- `src/VLCAvalonia.Vout/`
- focused native smoke/telemetry code

Tasks:

1. Preserve `VLC_CODEC_D3D11_OPAQUE` and
   `VLC_CODEC_D3D11_OPAQUE_10B` when offered by VLC instead of forcing BGRA.
2. Allocate the VLC-requested pool on the selected producer device with the
   correct NV12/P010 format, texture-array sizing, decoder and shader-resource
   bind flags, slice index, and per-picture `picture_sys_t`.
3. Construct pictures through VLC's resource/pool APIs and provide an unmanaged
   destroy callback that balances every COM and native allocation exactly once.
4. Keep the existing VLC-owned CPU BGRA pool for non-opaque source formats and
   fallback.
5. Make pool creation transactional: any partial failure releases all pictures,
   textures, contexts, and allocations and returns a safe failure to VLC.
6. Add counters for opaque pictures received, CPU pictures received, pool
   generations, and release balance.

Acceptance:

- Hardware H.264 playback reports an opaque D3D11 chroma at the vout.
- The opaque pool survives stop/reopen and format changes.
- Release counters balance after teardown.
- No CPU upload is performed for an opaque input picture.

Validation:

- Managed solution build and tests.
- Native AOT publish and export inspection.
- `--engine-smoke` and `--smoke` using the deterministic H.264 fixture.
- At least 25 open/stop/dispose/reopen cycles under a bounded stress mode.

Commit:

```text
feat: add VLC D3D11 opaque picture pooling
```

### WP-07 — Convert NV12/P010 to the Avalonia BGRA surface on GPU

Repository: `vlc-avalonia`
Prerequisite: WP-06 committed
May edit:

- D3D11 conversion/shared-texture code in `src/VLCAvalonia.Vout/`
- Avalonia composition import code when synchronization changes are required
- smoke telemetry and documentation

Tasks:

1. Use a D3D11 video processor or reviewed shader path to convert decoder NV12
   and P010 surfaces into the existing shareable BGRA texture on the same
   device. Do not map/read back the decoder surface through CPU memory.
2. Respect source slice, visible rectangle, color space/range, orientation, and
   output dimensions. Record unsupported HDR behavior honestly.
3. Preserve keyed-mutex sequencing: producer `0 -> 1`, Avalonia `1 -> 0`.
4. Recreate processor/output resources on format, size, adapter, or device
   generation changes. Bound all waits and handle device removal.
5. Keep the CPU BGRA upload path only for the declared fallback.
6. Extend smoke output with a machine-checkable path value:
   `hardware-opaque-gpu`, `software-bgra-gpu-upload`, or `cpu-bitmap`.
7. Make `--smoke` fail unless the expected path is supplied. For example,
   `--expect-path=hardware-opaque-gpu` must fail if any CPU upload occurs.

Acceptance:

- H.264 fixture: `hardware-opaque-gpu`, first frame within the timeout, CPU
  upload count zero.
- Software-forced fixture: `software-bgra-gpu-upload` or `cpu-bitmap`, still
  functional.
- UI smoke shows Avalonia overlay composition over the video.
- Pause, seek, resume, stop, dispose, and reopen work on the hardware path.
- Resize and fullscreen do not lose keyed-mutex progress or leak generations.

Validation:

```powershell
dotnet build vlc-avalonia.slnx -c Release
dotnet test vlc-avalonia.slnx --no-build -c Release
dotnet publish src/VLCAvalonia.Vout -c Release -r win-x64
```

After deployment/cache, run through Git Bash:

```bash
timeout 45s dotnet run --no-build --project src/VLCAvalonia -- \
  --smoke --expect-path=hardware-opaque-gpu
timeout 45s dotnet run --no-build --project src/VLCAvalonia -- --ui-smoke
timeout 120s dotnet run --no-build --project src/VLCAvalonia -- \
  --engine-smoke --cycles=25
```

Inspect Native AOT exports and confirm the plugin cache still lists both
submodules.

Commit:

```text
feat: present hardware-decoded frames through Avalonia composition
```

### WP-08 — Final Windows qualification and evidence

Repository: both, sequentially
Prerequisites: WP-01 through WP-07 committed

Tasks:

1. Update `eng/versions.json` to the final VLCLR `3.x` commit containing WP-04.
2. Run all VLCLR and product tests from clean builds.
3. Verify Debug and Release builds and Native AOT publish.
4. Verify required exports with `dumpbin /exports`.
5. Run hardware, fallback, UI, and 25-cycle lifecycle smoke tests.
6. Record actual path, adapter, source chroma/format, first-frame time, and
   teardown counts in `docs/D3D11_COMPOSITION.md`.
7. Update README claims to exactly match the observed validation. Keep known
   limitations explicit: codec coverage, HDR, multi-GPU coverage, subtitles,
   and unexecuted macOS/Linux work.
8. Run `git diff --check` and confirm no generated binaries or local media are
   staged.

Acceptance:

- All commands pass from the commits recorded in the report.
- Product version metadata points to the tested VLCLR commit.
- Documentation distinguishes verified facts from next work.
- Both repositories have clean tracked working trees after commits; unrelated
  untracked files in VLCLR are left untouched.

Commits:

VLCLR only if qualification requires a focused fix:

```text
fix: harden VLC 3 D3D11 lifecycle validation
```

Product evidence and final pin:

```text
docs: record validated Windows composition path
```

## 6. Dispatch waves and dependency graph

Dispatch sequentially unless isolated Git worktrees are prepared:

```text
Wave 1: WP-01
          |
Wave 2: WP-02
          |
Wave 3: WP-03 ---- WP-04
                    |
Wave 4:           WP-05
                    |
Wave 5:           WP-06
                    |
Wave 6:           WP-07
                    |
Wave 7:           WP-08
```

WP-03 and WP-04 touch different repositories and may run concurrently only in
separate worktrees. WP-08 is always serialized because it pins and validates
the final cross-repository state.

## 7. Subagent prompt template

Use this template verbatim, filling in one package:

```text
Implement work package <WP-ID> from docs/IMPLEMENTATION_PLAN.md.

Repository: <absolute repository path>
Prerequisite commits: <hashes>
Allowed files: <copy from package>
Out of scope: all other work packages; macOS/Linux spikes; pushing changes.

Read AGENTS.md and the complete package before editing. Preserve all unrelated
working-tree changes. Run the package validation exactly. Stage only explicit
package files and create the specified commit only if every required validation
passes. If blocked, do not commit; report the precise command, output, and the
smallest next action.

Return: summary, tests/smokes with results, commit hash, git status, and risks.
```

The dispatcher must replace prerequisite placeholders with the hashes produced
by earlier packages. Do not dispatch an agent against a stale VLCLR pin.

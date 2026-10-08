# Preparing a Linux/macOS hardware port

Three diagnostics collect different evidence before a native playback backend is
enabled. None starts a decoder or qualifies hardware playback.

## Native header layouts

From the product directory, with PowerShell 7 and a native C++ compiler:

```powershell
./eng/verify-native-abi.ps1 -FrameworkRoot ../vlclr -VlcSourceRoot ../vlc-vlc3-abi
```

Both checkouts must match `eng/versions.json` with clean tracked files. The VLC
checkout contains source headers, not an installed VLC runtime. For a new source
checkout, use the `vlc.repository` and `vlc.sourceCommit` values in that manifest.
An existing checkout at another revision should be retained; create a separate
worktree or sibling checkout for the pinned source.

The script compiles and executes VLCLR's existing
`external/vlclr/tests/AbiValidation/vlclr_abi.cpp` against the pinned source's `include`
directory. It introduces no copied native layouts or modified framework pin.
Windows uses MSVC x64 (automatically locating the Visual Studio developer shell);
Linux/macOS use `c++`, or a compiler executable supplied with `-Compiler`.

`artifacts/native-abi/` contains `report.json`, `compile.log`, `probe.log` and the
native probe executable. The report records pins, probe hash, compiler/target,
host architectures, execution stage and outcome. A pin, build or execution
failure exits nonzero and writes a failed report. A successful run means only
that the probe's existing size/offset assertions and module ABI constants match
the headers on that compiler target. It does not test runtime exports, variadic
calls, every VLC structure, decoder surfaces or GPU presentation.

The three Native ABI CI jobs perform this check on Windows, Linux and macOS.
The product, VLCLR and VLC are separate sibling checkouts on those runners.

## Native control calling conventions

The pinned VLC 3 framework includes an opt-in typed native control bridge. It
uses the target C++ compiler to construct `va_list` and call VLC's variadic
playlist export instead of passing Windows argument slots from managed code.

```powershell
../vlclr/eng/test-control-bridge.ps1 -VlcIncludeDirectory ../vlc-vlc3-abi/include
```

The executable crosses the C# / native boundary and checks a native fixture's
argument consumption: position/rate round trips, output arrays, error status,
playlist queries, locking flags and disposal. CI runs it on all three hosts,
against this application's pinned VLC 3 headers. The framework also checks
its own header pin. The fixture is not a VLC runtime and must not be deployed.

This is a reusable call boundary, not yet wired into the existing Windows
transport/queue/video owners. Real runtime export/call checks, ownership
integration and native library packaging remain necessary before enabling it
in a playback backend. Its introduction does not enable Unix playback.

The initial bridge CI run passed on Windows x64, Linux x64 and macOS ARM64,
alongside all 420 framework tests on each host. A Windows app build against
the new framework code also passed the hardware engine smoke and three stress
cycles on Intel Iris Xe: D3D11VA, zero CPU pictures/uploads, and 240 picture
allocations/releases. Local evidence: `artifacts/vlc3-controls-engine.log`.
This checks that the existing Windows path still works; it does not exercise
the new bridge against a real VLC runtime.

## Actual compositor capabilities

Build the app with the pinned VLCLR checkout, then run in a graphical session:

```powershell
$env:VLCLR_ROOT = (Resolve-Path ../vlclr).Path
dotnet build src/VLCAvalonia -c Release -p:UsedAvaloniaProducts=
dotnet src/VLCAvalonia/bin/Release/net10.0/VLCAvalonia.dll --graphics-probe --report=artifacts/graphics-probe.json
```

This opens a small diagnostic window and exits automatically. It bypasses VLC
runtime initialization, the normal player window and video backend creation;
neither a native VLC installation nor media is needed. The JSON records the OS,
architecture, Avalonia version, Linux session type when available, compositor
device identifiers, image/semaphore handle types and synchronization capabilities.

Exit 0 means a live compositor advertises at least one image handle type. Exit 2
means unavailable/lost interop, no image handle types, timeout or a query failure;
inspect the report. Exit 1 means the report could not be written. Failure before
the window can start, such as no graphical session, may prevent report creation.
`HardwarePlaybackQualified` is always false: advertised support alone does not
prove an image can be imported or presented, or that decoding is hardware-based.

On Linux, run separately in X11 and Wayland sessions and save separate reports.
On macOS, record whether the process is native Apple Silicon or Intel/Rosetta;
one architecture's report does not qualify the other. These diagnostics do not
automatically select or force a compositor backend.

## Evidence needed next

CI now exercises the graphics diagnostic in Release app jobs on all three OSes,
using Xvfb for Linux. `eng/test-graphics.ps1` rejects crashes, missing/invalid
reports and timeouts. Hosted jobs pass `-AllowUnavailable` so a valid report that
the compositor lacks interop is retained as `interop-unavailable`; it is never
recorded as a hardware-playback pass. Download the `graphics-*` artifacts to
inspect each runner's actual capabilities before planning a GPU-backed test.

Keep the native ABI report, compiler/probe logs, graphics JSON and the GPU/driver
and OS details together. Next, validate actual native exports, integrate the
tested control bridge into playback owners, then implement decoder surface ownership, GPU conversion,
synchronization and compositor import. Follow the hardware-only acceptance gates
in [CROSS_PLATFORM.md](CROSS_PLATFORM.md), including H.264/HEVC where supported,
zero CPU transfers, lifecycle, color/subtitles and installed-app checks.

Initial Windows checks on 2026-09-11 passed the pinned header probe with MSVC
x64. The graphics diagnostic reported D3D11 global/NT shared handles with keyed
mutex synchronization on Intel Iris Xe, with invalid VLC/framework paths to
confirm it does not initialize playback. These results do not qualify Unix GPUs.

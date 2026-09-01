# RFC 0001: VLC Avalonia port boundaries

- Status: accepted
- Date: 2026-09-01
- License: GPL-2.0-or-later

## Outcome

VLC Avalonia is a standalone Avalonia application that owns its process and
uses the pinned VLC 3 core through VLCLR. It is a workflow-compatible port of
the VLC desktop UI, not a translation of Qt widgets or Qt implementation code.

The October desktop target remains Windows, macOS, and Linux from one shared
product codebase. This implementation batch deliberately defers the macOS and
Linux rendering spikes; it does not remove those platforms from the release
gate. Mobile and browser targets remain bonuses and cannot become desktop
dependencies.

## Repository ownership

The `vlc-avalonia` repository owns:

- product contracts, state, UI, and platform heads;
- VLC engine adapters and platform video-surface implementations;
- packaging, end-to-end tests, release evidence, and product documentation.

The sibling `vlclr` repository owns:

- exact VLC 3 raw ABI declarations and layouts;
- generated bindings, ABI probes, compatibility shims, and ownership-safe
  reusable facades;
- framework-level tests that do not depend on this product's UI.

Product code must not add duplicate native layouts. Local development may use
the sibling project reference, but CI and releases must verify the VLCLR commit
recorded in `eng/versions.json` before building.

## Engine contract

Views consume `IMediaEngine` and immutable `MediaEngineState` snapshots. Native
callbacks and polling are reduced to one serialized state stream. Every media
open increments a generation; updates from older generations are ignored.
Pointers and VLC structures do not cross the engine boundary.

The first required lifecycle is open, play, pause, seek, resume, stop, dispose,
and reopen. Queue, track, subtitle, preferences, and diagnostics extend the
same contract after this slice is stable.

## Windows rendering decision

The Windows surface uses a managed Native AOT `vout display` plugin. A D3D11
keyed-mutex shared texture is imported through Avalonia composition, preserving
normal Avalonia z-order. CPU BGRA remains a compatibility fallback, not the
performance target. Hardware decoding is enabled; opaque D3D11 picture support
and GPU color conversion must be validated before the rendering architecture
is called complete.

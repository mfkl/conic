# Stop and Home playback lifecycle, 2026-09-10

The user reported a missing Stop button and media lingering in Home's bottom
bar after playback. Stop previously existed only in menus; Home's visibility
used `HasPlayableMedia`, which includes a stopped source that can be replayed.

Stop now has a visible square icon in the main transport and the Home mini-player,
with an accessible name and tooltip. Both use the existing Stop command and its
engine-state availability. The mini-player appears only while opening, playing
or paused. Stop, natural end, Closed and Faulted states hide it. Recent/favorite
history remains, so media can be opened again from its card.

Validation on the pinned VLCLR/VLC 3 runtime:

- Release build succeeded; all 179 product tests passed. Added coverage includes
  terminal states with a retained source/history, visibility notifications,
  paused playback and shared Stop command dispatch/readback.
- `--stop-smoke` passed native Stop, reopening and natural EOF; each ending
  clears the rendered Home bar. Stop remains visible, enabled and contained at
  1280, 900, 899 and 480 pixel widths. Recent history survives.
- `--layout-smoke` and `--playback-controls-smoke` passed; wide/compact transport
  layouts, sliders and keyboard focus remain functional.
- Inspected `artifacts/home-review/home-after-playback.png` and the refreshed
  `artifacts/playback-review/playback-controls-wide.png`. Logs are in
  `artifacts/stop-tests.log` and `artifacts/*-validation.log`.

The native smoke uses `--stop-smoke` with `VLC_DIRECTORY`,
`CONIC_TEST_MEDIA` and an isolated `CONIC_PROFILE`. It seeks near
the end of a finite local fixture and waits for VLC's actual stopped readback.

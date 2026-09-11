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
`VLC_AVALONIA_TEST_MEDIA` and an isolated `VLC_AVALONIA_PROFILE`. It seeks near
the end of a finite local fixture and waits for VLC's actual stopped readback.

## Follow-up: Play after Stop

The first smoke reopened the file explicitly; it did not test the user's
Stop → Play sequence. That exposed a separate bug: Stop tears down the core,
while the shared Play action still sent a pause toggle to the absent playlist.

The shared Play action now reopens the stopped source through the normal
generation-safe opening path, starting at zero without a resume prompt. Stop
remembers the selected queue item's source before teardown, including file-URI
decoding. After natural EOF, when VLC still owns the queue, Play restarts its
selected item without replacing the queue. Playing/paused toggle behavior stays
on the existing transport operation. Reopening validates the source and reports
an actionable error if it is unavailable.

Follow-up validation: Release build/test passed, all 182 tests. New tests cover
new-generation readback, starting without a resume prompt, the selected queue
file (including an escaped file URI), and replay using an existing EOF queue.
The native `--stop-smoke` now calls the shared Play command after both Stop and
EOF, checks pause/resume and advancing playback, and still verifies that the
Home bar clears on stop/end. Logs: `artifacts/replay-tests.log` and
`artifacts/replay-native.log`.

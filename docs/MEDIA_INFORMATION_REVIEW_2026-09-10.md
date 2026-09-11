# Media information: Windows validation, 2026-09-10

M1 now shows VLC-reported codec streams alongside metadata in More → Media
information, the Info tab and Ctrl+I. Each stream has labeled codec, language and
available video/audio fields. The drawer scrolls at both 1280×720 and 480×360;
Refresh and Copy diagnostics stay accessible. Switching tabs notifies visibility
bindings. Input changes clear previous details and reject late snapshots.

Framework: `feature/vlc3-codec-information` at
`b12127b096d80a6024c4c4940008aa31aca8e7fc`, based on the previous VLC 3 pin in an
isolated worktree at `artifacts/dependencies/vlclr-codec-info`. Set `VLCLR_ROOT`
to that worktree for builds. The unrelated sibling checkout was not switched.
VLC remains 3.0.24-beta1 / module ABI `3_0_0f`. `eng/versions.json` records the pin.

The binding uses exported, lock-protected `input_item_GetInfo` and localized
category/field names. It holds the input, copies strings and frees owned native
returns, including on failure. It avoids private structure offsets. It probes
metadata stream IDs 0–1023, preserves gaps and limits field values to 4096 UTF-16
code units. Fields absent from VLC remain absent. Decoder updates can occur
between individual field reads; Refresh obtains a newer snapshot.

Validation:

- Framework full solution Release build, including VideoOverlay and
  SubtitleRenderer: passed; 416 tests passed. Four existing CA1420 warnings.
- Product Release build and tests passed, including Info visibility notification,
  queue-input stale-state rejection, immutable fields and diagnostics redaction.
- Native AOT plugin published, VLC 3 exports verified, deployed to the pinned
  runtime and plugin cache regenerated.
- Native Info smoke reported three actual streams: XVID 640×480 video with
  frame rate, AAC 44100 Hz audio and ASS subtitles. Visual-tree assertions and
  inspected compact/wide captures confirm rendered fields and a contained drawer.
- Hardware opaque GPU, software GPU upload, CPU bitmap, Home/session, PiP and
  Settings regression checks passed using the new framework pin.

Local artifacts: `artifacts/media-information-review/media-info-{1280,480}.png`,
`artifacts/codec-framework-tests.log`, `artifacts/codec-aot-publish.log` and
`artifacts/playback-validation/codec-*.log`. Native smoke:
`VLCAvalonia.exe --media-information-smoke` with `VLC_DIRECTORY` and
`VLC_AVALONIA_TEST_MEDIA` configured.

This records implementation and Windows validation, not user visual acceptance.
macOS/Linux native qualification and remaining V1/V2/V3 capabilities stay open.

# Home thumbnails: Windows validation, 2026-09-10

The initial Home cards contained only play-icon placeholders. Local media cards
now display real thumbnails in Home, Recent, Favorites and Continue watching.
The existing grid/list layout, card actions and mini-player remain available.

Windows Shell `IShellItemImageFactory` runs on a dedicated background STA,
requesting thumbnails only (never file/application icons). Windows supplies the
frame through its installed media handlers. The provider is independent of the
VLCLR/VLC 3 playback session. Native COM, HBITMAP and DC handles are released.
No extra player, FFmpeg dependency, network lookup or engine migration is added.

File metadata and cache I/O run off the dispatcher. Requests share cached work;
detaching or reusing a card cancels its wait and disposes its bitmap. Late results
cannot replace another card's artwork. The cache key includes canonical path,
file size and modification time, so changed files receive fresh artwork.
PNG files live under the active profile's `thumbnails` directory, capped at 256
entries; memory retains at most 64 requests/results and the native queue is bounded.
Canceling one card does not cancel another card's shared request.

Fallbacks: missing files, URLs, UNC/network drives, failed handlers and platforms
without this provider retain the play icon. Windows handler availability determines
format coverage. macOS/Linux providers and native qualification remain open.

Validation:

- Release solution build passed with four existing framework CA1420 warnings;
  all 175 product tests passed. Seven thumbnail cases cover request coalescing,
  cancellation, disk reuse, file changes, remote/invalid sources and provider failure.
- `--thumbnail-smoke` passed with an owned H.264 test pattern and the real local
  video already in the user's recent history. Both rendered actual bitmap artwork.
- Search removed cards; clearing search restored cached artwork. Compact 480×360
  and wide 1280×720 captures were inspected. Playback retained the same engine,
  generation and advancing position throughout extraction and browsing.
- A native restart reused the persisted PNG without rewriting it. The smoke
  waits for a position notification because cache hits can complete before one.
- The new VLC 3 binding/plugin also passed hardware, software-upload, CPU,
  media-information, Home/session, PiP and Settings native checks.

Reproduce with `Conic.exe --thumbnail-smoke`, `VLC_DIRECTORY` set to the
pinned VLC runtime, `CONIC_PROFILE` set to an isolated directory and
`CONIC_TEST_MEDIA` pointing at a local video supported by Windows Shell.
`--thumbnail-review` runs the same checks and leaves Home open.
Local evidence: `artifacts/thumbnail-tests.log`, `artifacts/thumbnail-smoke.log`,
`artifacts/thumbnail-user-media.log` and
`artifacts/home-review/home-thumbnails-{wide,compact}.png`.

Implementation and validation are complete; visual acceptance remains with the user.

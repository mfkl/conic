# Release inputs and outstanding distribution work

The Windows package script creates a **local review artifact**. It does not
establish that every bundled dependency is ready for public redistribution.

| Input | Current source / evidence | Distribution work |
|---|---|---|
| VLC Avalonia | GPL-2.0-or-later, repository `LICENSE` contains the complete GPL v2 text; the notice is in the README | Include matching source/build instructions in a public release |
| VLCLR | Included as source in `src/VLCLR` and `src/VLCLR.Generators`, GPL-2.0-or-later with the rest of the repository; VLC 3 import library in `lib/libvlccore.lib`; upstream commit in `eng/versions.json` | Covered by the repository source; nothing separate to publish |
| VLC runtime/plugins | Official VideoLAN VLC 3.0.24 Windows x64 build, downloaded by `eng/get-vlc.ps1` and verified against the SHA-256 in `eng/versions.json`; source tag commit recorded there; runtime carries `COPYING.txt`, authors and notices | Collect applicable component notices; point to VideoLAN's corresponding source (`vlc-3.0.24.tar.xz`) in the release notes |
| VLC import library | `lib/libvlccore.lib` from the official VLC 3.0.24 win64 SDK (`vlc-3.0.24-win64.7z`) | None beyond the VLC runtime notices |
| Managed/native NuGet dependencies | Exact resolved inventory in the app's `.deps.json`; `project.assets.json` retained beside the ZIP | Collect license texts and notices from resolved packages, including Avalonia, Inter font, TerraFX and .NET native assets |
| Product icons/layout | VLC-inspired UI and repository assets | Record upstream provenance/attribution and any applicable asset/font terms |
| Test media | Existing generated H.264 test pattern for local checks | Record generation command/hash; package excludes test media and personal reference PNGs |

`package-manifest.json` records product/framework revisions, working-tree
changes, actual SDK and pinned SDK, runtime pins, and hashes for the staged
files. It excludes its own hash; `SHA256SUMS.txt` covers the final ZIP. Packaging
validates x64 PE headers and the runtime version, then generates
the staged VLC plugin cache for validation and omits it from the ZIP. VLC scans
the bundled plugins at startup, avoiding cache invalidation when ZIP timestamps
are interpreted in another time zone. Version/header checks alone do not prove the
runtime was built from the recorded source commit.

The opt-in portable native control helper is not used by the current Windows
playback owners and is not added speculatively to this package. If integration
starts using it, add the exact target helper and source/notices to staging and
qualify its loading.

The CI SDK pin is 10.0.102. Local builds may use another installed SDK; the
script warns and records the actual version rather than claiming identical
toolchains. A public release needs a clean product revision and retained
build/qualification evidence. Do not describe a dirty local artifact as a
release of its base commit.

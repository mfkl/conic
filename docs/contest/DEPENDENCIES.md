# Release inputs and outstanding distribution work

The Windows package script creates a **local review artifact**. It does not
establish that every bundled dependency is ready for public redistribution.

| Input | Current source / evidence | Distribution work |
|---|---|---|
| Conic | GPL-2.0-or-later, repository `LICENSE` includes complete GPL v2 text | Include matching source/build instructions in a public release |
| VLCLR | Private `mfkl/vlclr-private`, commit in `eng/versions.json`; no root LICENSE found at the reviewed pin | Maintainer must establish applicable license and an accessible corresponding-source route; no private source is published by the packaging script |
| VLC runtime/plugins | VLC 3.0.24-beta1; source commit recorded in `eng/versions.json`; runtime carries `COPYING.txt`, authors and notices | Record the exact binary source/build provenance and collect applicable component notices/source before distributing |
| Managed/native NuGet dependencies | Exact resolved inventory in the app's `.deps.json`; `project.assets.json` retained beside the ZIP | Collect license texts and notices from resolved packages, including Avalonia, Inter font, TerraFX and .NET native assets |
| Product icons/layout | VLC-inspired UI and repository assets | Record upstream provenance/attribution and any applicable asset/font terms |
| Test media | Existing generated H.264 test pattern for local checks | Record generation command/hash; package excludes test media and personal reference PNGs |

`package-manifest.json` records product/framework revisions, working-tree
changes, actual SDK and pinned SDK, runtime pins, and hashes for the staged
files. It excludes its own hash; `SHA256SUMS.txt` covers the final ZIP. Packaging
validates x64 PE headers, runtime version and the framework pin, then generates
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

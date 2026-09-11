# Continuous integration

The [Build and test workflow](../.github/workflows/ci.yml) runs on pushes and pull
requests. It uses the .NET SDK from `eng/versions.json`, commit-pinned actions,
read-only repository permissions and cancels superseded runs on the same ref.

## Automatic coverage

The Core matrix runs Debug and Release tests on Windows Server 2022, Ubuntu
24.04 and macOS 15. It builds and tests `Player.Application.Tests` and
`Player.Engine.ContractTests`: currently 72 cases per matrix entry. Neither
project needs VLCLR, native VLC, a display server or a GPU. TRX results are
uploaded even when a test fails and retained for 14 days.

Core checks do not build the UI or native plugin, and do not qualify playback.

## Linux/macOS app coverage

Four Desktop jobs build the real Avalonia app and run all 149 managed UI tests
on Ubuntu 24.04 and macOS 15, in Debug and Release. They use the same pinned
sibling VLCLR checkout and dependency-access settings as the Windows jobs.
The `Desktop` script scope builds the UI test project and its app references;
it excludes the Windows-only Native AOT vout project. It does not load native
VLC or require a graphical session. These tests cover presentation models,
storage, layout policies and backend lifecycle, not rendered UI or playback.

The complete workflow has twelve jobs. `VLCLR_CI_ENABLED=true` enables both app
matrices automatically; the `desktop_app` manual input can also request the
Linux/macOS matrix independently of `full_windows`.

## Full Windows coverage

The Windows matrix builds the complete solution and runs all 221
managed tests in Debug and Release. Release also publishes the Windows x64
Native AOT plugin. It verifies the dependency HEAD against `eng/versions.json`
and rejects modified tracked dependency files. Test results and native outputs
are retained for 14 days.

CI checks out the repositories side by side in the runner workspace:

```text
$GITHUB_WORKSPACE/
  vlc-avalonia/    # product checkout; build/test working directory
  vlclr/          # separate pinned dependency checkout; Desktop/Windows jobs
```

The app jobs pass `-FrameworkRoot ../vlclr`, which sets `VLCLR_ROOT` for the
build. VLCLR is neither cloned inside the product checkout nor committed to it.
No NuGet publication is needed. Artifact upload paths are relative to the runner
workspace and point into `vlc-avalonia/artifacts/ci`.

On 2026-09-11 the pinned VLCLR commit was pushed to the private repository,
a read-only deploy key was provisioned, and `VLCLR_CI_ENABLED=true` was configured.
The full Windows matrix now participates in pushes and pull requests.
For a fresh repository, reproduce the setup as follows:

1. Push the reviewed Git commit to `mfkl/vlclr-private` (the repository
   in `eng/versions.json`). Keep the product pin; do not substitute current VLCLR
   main, which targets another VLC ABI.
2. Add a read-only SSH deploy key to the private VLCLR repository and store its
   private key in the product repository secret `VLCLR_SSH_KEY`. This key grants
   read access only to that dependency repository; no personal token is needed.
   Credentials are not persisted by checkout. To rotate it, register a new key,
   replace the secret, verify a successful checkout, then remove the old deploy key.
3. Trigger **Build and test** with `full_windows` enabled. Manual dispatch becomes
   available after the workflow reaches the default branch.
4. Set repository variable `VLCLR_CI_ENABLED=true` to run full Windows checks on
   subsequent pushes and pull requests. Require the full jobs in branch protection
   only after they are enabled and passing. A missing key or inaccessible pin
   fails the requested job explicitly.

No dependency commits, secrets or repository variables are published/configured
by the workflow itself. Pull requests from forks do not receive this secret;
their Core jobs can still run, while enabled app jobs will fail their
prerequisite check. Do not use `pull_request_target` to execute untrusted PR code.

Hosted jobs do not run hardware-decoding or compositor smokes. Those require
qualified GPU machines and the pinned native VLC runtime, as described in
[CROSS_PLATFORM.md](CROSS_PLATFORM.md). Local Windows hardware results are recorded
there separately from CI coverage.

## Run the same checks locally

PowerShell 7 and .NET 10 are required. Restore is enabled by default; use
`-NoRestore` only after dependencies have been restored.

```powershell
./eng/test.ps1 -Scope Core -Configuration Release
./eng/test.ps1 -Scope Desktop -Configuration Release -FrameworkRoot ../vlclr
./eng/test.ps1 -Scope Windows -Configuration Debug -FrameworkRoot ../vlclr
./eng/test.ps1 -Scope Windows -Configuration Release -FrameworkRoot ../vlclr
```

The scripts return failure when a build, test, native publish or pin check fails.
The local sibling checkout must be at the pinned commit with clean tracked files.
They write results under `artifacts/ci`. CI creates a job-local `global.json` to
enforce the exact SDK pin; local scripts use the installed SDK selection.

Local validation on Windows: actionlint 1.7.12 passed; the Core Release script
passed 72 tests; the full Windows script passed 213 tests in both Debug and
Release. Release built with zero warnings/errors and published the AOT plugin.
These runs used the installed SDK 10.0.303 and restored dependencies.

The first hosted run on 2026-09-11 passed both full Windows jobs and both Windows
Core jobs with the exact SDK 10.0.102 pin. Linux/macOS exposed a history test that
implicitly expected Windows filename comparison; the fixture now requests that
policy explicitly. Current hosted results are available in
[GitHub Actions](https://github.com/mfkl/vlc-avalonia/actions/workflows/ci.yml).

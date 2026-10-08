#requires -Version 7.0
# Builds a self-contained macOS app bundle, Conic.app, for this Mac's
# architecture: the app, the Native AOT plugin and, with -VlcDirectory, a
# bundled VLC 3 runtime in Contents/Frameworks/vlc. Without one, the app uses
# an installed /Applications/VLC.app and carries its plugin in Contents/MacOS/plugins.
# The bundle is signed ad hoc for local testing; it is not notarized.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string] $Version = '0.1.0-preview.1',
    [string] $OutputDirectory = 'artifacts/packages',
    # The lib directory of a VLC 3 macOS runtime laid out like
    # VLC.app/Contents/MacOS (lib/, plugins/, share/), as eng/get-vlc.ps1 prints.
    [string] $VlcDirectory,
    [string] $BundleIdentifier = 'io.videolabs.conic',
    # Include the smoke harness (--*-smoke flags) for qualification.
    [switch] $Smoke
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsMacOS) { throw 'This packaging script runs on macOS; Native AOT does not cross-compile between operating systems.' }
$architecture = switch ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
    'Arm64' { 'arm64' }
    'X64' { 'x64' }
    default { throw "Unsupported macOS architecture $_." }
}
$rid = "osx-$architecture"
$productRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$pins = Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json

function Invoke-Checked([string] $Executable, [string[]] $Arguments, [string] $Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited $LASTEXITCODE; see $Log" }
}

# Mach-O magic plus the CPU type this bundle targets (arm64 or x86-64).
function Assert-MachO([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $expected = if ($architecture -eq 'arm64') { 0x0100000C } else { 0x01000007 }
    # PowerShell reads 0xFEEDFACF as a negative Int32, so compare signed values.
    if ($bytes.Length -lt 8 -or [BitConverter]::ToInt32($bytes, 0) -ne 0xFEEDFACF -or [BitConverter]::ToInt32($bytes, 4) -ne $expected) {
        throw "The bundle requires $architecture Mach-O binaries: $Path"
    }
}

$frameworkCommit = & git -C (Join-Path $productRoot 'external/vlclr') rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $frameworkCommit -ne $pins.vlclr.commit) {
    throw "external/vlclr must be at $($pins.vlclr.commit); run git submodule update --init."
}
$runtime = $null
if ($VlcDirectory) {
    $runtime = (Resolve-Path -LiteralPath $VlcDirectory).Path
    $core = Get-ChildItem -Path (Join-Path $runtime 'libvlccore*.dylib') | Select-Object -First 1
    if (-not $core) { throw "No libvlccore in $runtime." }
    $vlcRoot = Split-Path $runtime -Parent
    if (-not (Test-Path -LiteralPath (Join-Path $vlcRoot 'plugins') -PathType Container)) {
        throw 'The VLC runtime must have plugins/ beside lib/, as in VLC.app/Contents/MacOS.'
    }
}
$productCommit = & git -C $productRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve product revision.' }
$productChanges = @(& git -C $productRoot status --porcelain --untracked-files=normal)

# Every invocation owns a new directory. Never overwrite or delete a previous package.
$runRoot = Join-Path $outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$packageName = "Conic-$Version-$rid"
$bundle = Join-Path $runRoot 'Conic.app'
$contents = Join-Path $bundle 'Contents'
$executables = Join-Path $contents 'MacOS'
$logs = Join-Path $runRoot 'logs'
New-Item -ItemType Directory -Path $executables, (Join-Path $contents 'Resources'), $logs | Out-Null
$sdk = (& dotnet --version).Trim()
if ($sdk -ne $pins.dotnetSdk) { Write-Warning "Local SDK $sdk differs from CI pin $($pins.dotnetSdk); recorded in manifest." }

Write-Output "Publishing app to $executables"
$appArguments = @('publish', (Join-Path $productRoot 'src/Conic/Conic.csproj'), '-c', $Configuration, '-r', $rid,
    '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:UseAppHost=true', '-p:UsedAvaloniaProducts=',
    "-p:Version=$Version", '-o', $executables)
if ($Smoke) { $appArguments += '-p:Smoke=true' }
Invoke-Checked 'dotnet' $appArguments (Join-Path $logs 'app-publish.log')
$nativeOutput = Join-Path $runRoot 'native'
Invoke-Checked 'dotnet' @('publish', (Join-Path $productRoot 'src/Conic.Vout/Conic.Vout.csproj'), '-c', $Configuration, '-r', $rid,
    "-p:Version=$Version", '-o', $nativeOutput) (Join-Path $logs 'plugin-publish.log')
$plugin = Join-Path $nativeOutput 'libvlc_avalonia_plugin.dylib'
Assert-MachO $plugin
Assert-MachO (Join-Path $executables 'Conic')

if ($runtime) {
    # Bundled runtime, mirroring VLC.app/Contents/MacOS so VLC's libraries and
    # plugins still find each other; the plugin sits with VLC's own plugins.
    $vlcStage = Join-Path $contents 'Frameworks/vlc'
    New-Item -ItemType Directory -Path $vlcStage | Out-Null
    foreach ($directory in 'lib', 'plugins', 'share') {
        $source = Join-Path $vlcRoot $directory
        if (Test-Path -LiteralPath $source) { Invoke-Checked 'ditto' @($source, (Join-Path $vlcStage $directory)) (Join-Path $logs "ditto-$directory.log") }
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $vlcStage 'plugins/video_output') | Out-Null
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $vlcStage 'plugins/video_output/libvlc_avalonia_plugin.dylib')
}
else {
    # Installed VLC.app: the app adds this directory to VLC_PLUGIN_PATH.
    New-Item -ItemType Directory -Path (Join-Path $executables 'plugins') | Out-Null
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $executables 'plugins/libvlc_avalonia_plugin.dylib')
}
foreach ($file in @('LICENSE', 'docs/contest/DEPENDENCIES.md')) {
    Copy-Item -LiteralPath (Join-Path $productRoot $file) -Destination (Join-Path $contents 'Resources')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Destination (Join-Path $contents 'Resources')

$shortVersion = ($Version -split '-')[0]
@"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>Conic</string>
  <key>CFBundleExecutable</key><string>Conic</string>
  <key>CFBundleIdentifier</key><string>$BundleIdentifier</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Conic</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$shortVersion</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
"@ | Set-Content -LiteralPath (Join-Path $contents 'Info.plist')

# Ad hoc signature for local testing. Re-signing replaces VideoLAN's signature
# on the bundled VLC libraries; a distributed build needs a Developer ID
# signature and notarization instead.
Invoke-Checked 'codesign' @('--force', '--deep', '--sign', '-', $bundle) (Join-Path $logs 'codesign.log')
Invoke-Checked 'codesign' @('--verify', '--deep', '--strict', $bundle) (Join-Path $logs 'codesign-verify.log')

$manifest = [ordered]@{
    schemaVersion = 1; name = $packageName; version = $Version; configuration = $Configuration; rid = $rid
    createdUtc = [DateTime]::UtcNow.ToString('o'); productCommit = $productCommit; workingTreeChanges = $productChanges
    frameworkCommit = $frameworkCommit; sdk = $sdk; pinnedSdk = $pins.dotnetSdk; pins = $pins
    source = 'https://github.com/mfkl/conic'; selfContained = $true
    vlcRuntime = if ($runtime) { 'bundled' } else { 'installed VLC.app' }
    distributionStatus = 'local-review'
    distributionBlockers = @('Signed ad hoc and not notarized: Gatekeeper blocks it after a download until the quarantine attribute is removed.')
    hardwarePlaybackQualified = $false
    files = @(Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($bundle, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
# Beside the bundle, not inside it: a file added after signing would break the seal.
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot 'package-manifest.json')
$archive = Join-Path $runRoot "$packageName.zip"
# ditto keeps symbolic links, executable bits and signatures.
Invoke-Checked 'ditto' @('-c', '-k', '--sequesterRsrc', '--keepParent', $bundle, $archive) (Join-Path $logs 'ditto-archive.log')
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $runRoot 'SHA256SUMS.txt')
Write-Output "Local review package: $archive"
Write-Output "SHA-256: $hash"

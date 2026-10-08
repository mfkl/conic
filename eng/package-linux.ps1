#requires -Version 7.0
# Builds a self-contained Linux x64 package: the app, the Native AOT plugin
# and, with -VlcDirectory, a bundled VLC 3 runtime. Without one, the package
# uses the distribution's VLC 3 packages (libvlccore9, libvlc5, vlc-plugin-base).
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string] $Version = '0.1.0-preview.1',
    [string] $OutputDirectory = 'artifacts/packages',
    # The lib directory of a Linux x64 VLC 3 install (libvlccore.so.9, libvlc.so.5,
    # vlc/plugins/), bundled under vlc/. VLC finds its plugins relative to libvlccore.
    [string] $VlcDirectory,
    # Native AOT compiler driver. Defaults to clang; gcc works where clang is not installed.
    [string] $Compiler,
    # Include the smoke harness (--*-smoke flags) for qualification.
    [switch] $Smoke
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'This packaging script runs on Linux x64; Native AOT does not cross-compile between operating systems.'
}
$rid = 'linux-x64'
$productRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$pins = Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json

function Invoke-Checked([string] $Executable, [string[]] $Arguments, [string] $Log) {
    & $Executable @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited $LASTEXITCODE; see $Log" }
}

function Assert-ElfX64([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 20 -or $bytes[0] -ne 0x7F -or $bytes[1] -ne 0x45 -or $bytes[2] -ne 0x4C -or $bytes[3] -ne 0x46 -or
        $bytes[4] -ne 2 -or [BitConverter]::ToUInt16($bytes, 0x12) -ne 0x3E) {
        throw "The package requires x86-64 ELF binaries: $Path"
    }
}

$frameworkCommit = & git -C (Join-Path $productRoot 'external/vlclr') rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $frameworkCommit -ne $pins.vlclr.commit) {
    throw "external/vlclr must be at $($pins.vlclr.commit); run git submodule update --init."
}
$runtime = $null
if ($VlcDirectory) {
    $runtime = (Resolve-Path -LiteralPath $VlcDirectory).Path
    foreach ($name in @('libvlccore.so.9', 'libvlc.so.5')) {
        $library = Join-Path $runtime $name
        if (-not (Test-Path -LiteralPath $library -PathType Leaf)) { throw "Missing VLC runtime file: $name" }
        Assert-ElfX64 $library
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runtime 'vlc/plugins') -PathType Container)) { throw 'Missing VLC plugins directory (vlc/plugins).' }
}
$productCommit = & git -C $productRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve product revision.' }
$productChanges = @(& git -C $productRoot status --porcelain --untracked-files=normal)

# Every invocation owns a new directory. Never overwrite or delete a previous package.
$runRoot = Join-Path $outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$packageName = "VLC-Avalonia-$Version-$rid"
$stage = Join-Path $runRoot $packageName
$logs = Join-Path $runRoot 'logs'
New-Item -ItemType Directory -Path $stage, $logs | Out-Null
$sdk = (& dotnet --version).Trim()
if ($sdk -ne $pins.dotnetSdk) { Write-Warning "Local SDK $sdk differs from CI pin $($pins.dotnetSdk); recorded in manifest." }

Write-Output "Publishing app to $stage"
$appArguments = @('publish', (Join-Path $productRoot 'src/VLCAvalonia/VLCAvalonia.csproj'), '-c', $Configuration, '-r', $rid,
    '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:UsedAvaloniaProducts=',
    "-p:Version=$Version", '-o', $stage)
if ($Smoke) { $appArguments += '-p:Smoke=true' }
Invoke-Checked 'dotnet' $appArguments (Join-Path $logs 'app-publish.log')
$nativeOutput = Join-Path $runRoot 'native'
$pluginArguments = @('publish', (Join-Path $productRoot 'src/VLCAvalonia.Vout/VLCAvalonia.Vout.csproj'), '-c', $Configuration, '-r', $rid,
    "-p:Version=$Version", '-o', $nativeOutput)
if ($Compiler) { $pluginArguments += "-p:CppCompilerAndLinker=$Compiler" }
Invoke-Checked 'dotnet' $pluginArguments (Join-Path $logs 'plugin-publish.log')
$plugin = Join-Path $nativeOutput 'libvlc_avalonia_plugin.so'
Assert-ElfX64 $plugin
Assert-ElfX64 (Join-Path $stage 'VLCAvalonia')

if ($runtime) {
    # Bundled runtime: the plugin sits with VLC's own plugins.
    $vlcStage = Join-Path $stage 'vlc'
    Copy-Item -LiteralPath $runtime -Destination $vlcStage -Recurse
    Remove-Item -LiteralPath (Join-Path $vlcStage 'vlc/plugins/plugins.dat') -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $vlcStage 'vlc/plugins/video_output') | Out-Null
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $vlcStage 'vlc/plugins/video_output/libvlc_avalonia_plugin.so')
}
else {
    # Distribution runtime: the app adds this directory to VLC_PLUGIN_PATH.
    New-Item -ItemType Directory -Path (Join-Path $stage 'plugins') | Out-Null
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $stage 'plugins/libvlc_avalonia_plugin.so')
}
foreach ($file in @('LICENSE', 'docs/contest/DEPENDENCIES.md')) {
    Copy-Item -LiteralPath (Join-Path $productRoot $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Destination $stage

$manifest = [ordered]@{
    schemaVersion = 1; name = $packageName; version = $Version; configuration = $Configuration; rid = $rid
    createdUtc = [DateTime]::UtcNow.ToString('o'); productCommit = $productCommit; workingTreeChanges = $productChanges
    frameworkCommit = $frameworkCommit; sdk = $sdk; pinnedSdk = $pins.dotnetSdk; pins = $pins
    source = 'https://github.com/mfkl/vlc-avalonia'; selfContained = $true
    vlcRuntime = if ($runtime) { 'bundled' } else { 'distribution' }
    distributionStatus = 'local-review'
    distributionBlockers = @('Linux GPU video output is not implemented yet; the app reports playback as unavailable.')
    hardwarePlaybackQualified = $false
    files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($stage, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stage 'package-manifest.json')
$archive = Join-Path $runRoot "$packageName.tar.gz"
# tar keeps the executable bits a ZIP would lose.
Invoke-Checked 'tar' @('-czf', $archive, '-C', $runRoot, $packageName) (Join-Path $logs 'tar.log')
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $runRoot 'SHA256SUMS.txt')
Write-Output "Local review package: $archive"
Write-Output "SHA-256: $hash"

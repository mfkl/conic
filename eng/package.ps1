#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [ValidateSet('win-x64')][string] $RuntimeIdentifier = 'win-x64',
    [Parameter(Mandatory)][string] $FrameworkRoot,
    [Parameter(Mandatory)][string] $VlcDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string] $Version = '0.1.0-preview.1',
    [string] $OutputDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This packaging script requires Windows and the Windows x64 Native AOT toolchain.' }
$productRoot = Split-Path $PSScriptRoot -Parent
$framework = (Resolve-Path -LiteralPath $FrameworkRoot).Path
$runtime = (Resolve-Path -LiteralPath $VlcDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$pins = Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json

function Invoke-Checked([string] $Executable, [string[]] $Arguments, [string] $Log) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $productRoot
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Cannot start $Executable" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(1200000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Timed out: $Executable"
        }
        @($stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult()) | Set-Content -LiteralPath $Log
        if ($process.ExitCode -ne 0) { throw "$Executable exited $($process.ExitCode); see $Log" }
    }
    finally { $process.Dispose() }
}

function Assert-X64([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE image: $Path" }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset -gt $stream.Length - 6) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw "The package requires x64 native binaries: $Path"
        }
    }
    finally { $reader.Dispose() }
}

$frameworkCommit = & git -C $framework rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $frameworkCommit -ne $pins.vlclr.commit) { throw "VLCLR must be at $($pins.vlclr.commit)." }
$frameworkDirty = & git -C $framework status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $frameworkDirty) { throw 'VLCLR must have clean tracked sources.' }
foreach ($name in @('libvlc.dll', 'libvlccore.dll', 'vlc-cache-gen.exe', 'COPYING.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name) -PathType Leaf)) { throw "Missing VLC runtime file: $name" }
}
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'plugins') -PathType Container)) { throw 'Missing VLC plugins directory.' }
foreach ($name in @('libvlc.dll', 'libvlccore.dll', 'vlc-cache-gen.exe')) { Assert-X64 (Join-Path $runtime $name) }
$runtimeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runtime 'libvlccore.dll')).FileVersion
if ($runtimeVersion -ne $pins.vlc.version) { throw "Expected VLC $($pins.vlc.version); found '$runtimeVersion'." }

$productCommit = & git -C $productRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve product revision.' }
$productChanges = @(& git -C $productRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect product changes.' }
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate .NET SDK.' }
if ($sdk -ne $pins.dotnetSdk) { Write-Warning "Local SDK $sdk differs from CI pin $($pins.dotnetSdk); recorded in manifest." }

# Every invocation owns a new directory. Never overwrite or delete a previous package.
$runRoot = Join-Path $outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$packageName = "Conic-$Version-$RuntimeIdentifier"
$stage = Join-Path $runRoot $packageName
$logs = Join-Path $runRoot 'logs'
New-Item -ItemType Directory -Path $stage, $logs | Out-Null
$previousFramework = $env:VLCLR_ROOT
try {
    $env:VLCLR_ROOT = $framework
    Write-Output "Publishing app to $stage"
    Invoke-Checked 'dotnet' @('publish', 'src/Conic/Conic.csproj', '-c', $Configuration, '-r', $RuntimeIdentifier,
        '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:UsedAvaloniaProducts=',
        "-p:Version=$Version", '-o', $stage) (Join-Path $logs 'app-publish.log')
    $nativeOutput = Join-Path $runRoot 'native'
    Invoke-Checked 'dotnet' @('publish', 'src/Conic.Vout/Conic.Vout.csproj', '-c', $Configuration, '-r', $RuntimeIdentifier,
        "-p:Version=$Version", '-o', $nativeOutput) (Join-Path $logs 'vout-publish.log')
    $vlcStage = Join-Path $stage 'vlc'
    New-Item -ItemType Directory -Path $vlcStage | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $runtime -File) {
        if ($file.Extension -eq '.dll' -or $file.Name -in @('vlc-cache-gen.exe', 'COPYING.txt', 'AUTHORS.txt', 'THANKS.txt', 'README.txt')) {
            Copy-Item -LiteralPath $file.FullName -Destination $vlcStage
        }
    }
    foreach ($directory in @('plugins', 'lua', 'locale', 'hrtfs')) {
        $source = Join-Path $runtime $directory
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $vlcStage -Recurse }
    }
    $plugin = Join-Path $nativeOutput 'libvlc_avalonia_plugin.dll'
    Assert-X64 $plugin
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $vlcStage 'plugins/video_output/libvlc_avalonia_plugin.dll') -Force
    # ZIP stores DOS timestamps at two-second precision. Generate the VLC cache
    # from exactly representable times so extraction does not invalidate it.
    $pluginTimestamp = [DateTime]::new(2026, 1, 1, 12, 0, 0, [DateTimeKind]::Utc)
    Get-ChildItem -LiteralPath (Join-Path $vlcStage 'plugins') -Recurse -File | ForEach-Object { $_.LastWriteTimeUtc = $pluginTimestamp }
    Invoke-Checked (Join-Path $vlcStage 'vlc-cache-gen.exe') @((Join-Path $vlcStage 'plugins')) (Join-Path $logs 'plugin-cache.log')
    if (-not (Test-Path -LiteralPath (Join-Path $vlcStage 'plugins/plugins.dat'))) { throw 'VLC plugin cache was not generated.' }
    foreach ($file in @('LICENSE', 'docs/WINDOWS_QUICK_START.md', 'docs/contest/DEPENDENCIES.md')) {
        Copy-Item -LiteralPath (Join-Path $productRoot $file) -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'versions.json') -Destination $stage
    # Keep NuGet's exact runtime/native dependency inventory beside the package notices.
    Copy-Item -LiteralPath (Join-Path $productRoot 'src/Conic/obj/project.assets.json') -Destination (Join-Path $runRoot 'project.assets.json')
    $manifest = [ordered]@{
        schemaVersion = 1; name = $packageName; version = $Version; configuration = $Configuration; rid = $RuntimeIdentifier
        createdUtc = [DateTime]::UtcNow.ToString('o'); productCommit = $productCommit; workingTreeChanges = $productChanges
        frameworkCommit = $frameworkCommit; sdk = $sdk; pinnedSdk = $pins.dotnetSdk; pins = $pins
        source = 'https://github.com/mfkl/conic'; selfContained = $true
        distributionStatus = 'local-review'; distributionBlockers = @('VLCLR license/source delivery must be resolved before public distribution; see DEPENDENCIES.md.')
        hardwarePlaybackQualified = $false
        files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
            [ordered]@{ path = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stage 'package-manifest.json')
    $zip = Join-Path $runRoot "$packageName.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $runRoot 'SHA256SUMS.txt')
    [ordered]@{ packageDirectory = $stage; archive = $zip; sha256 = $hash; manifest = (Join-Path $stage 'package-manifest.json') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'result.json')
    Write-Output "Local review package: $zip"
    Write-Output "SHA-256: $hash"
}
finally { $env:VLCLR_ROOT = $previousFramework }

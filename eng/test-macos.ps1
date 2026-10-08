#requires -Version 7.0
# Qualifies Conic on macOS against a real VLC 3 runtime:
#  - engine: queue, transport, tracks, delays, media information, lifecycle;
#  - IOSurface video output: BGRA pixel buffers lent and returned, with
#    hardware (VideoToolbox) and software decoding;
#  - with -Window, the styled window: GPU and CPU-fallback video, video
#    adjustments, PiP and compositor reattachment.
# Window cases need a logged-in GUI session.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Media,
    # Directory holding libvlccore; defaults to the runtime eng/get-vlc.ps1
    # downloaded, then an installed VLC.app.
    [string] $VlcDirectory,
    [ValidateRange(1, 100)][int] $Cycles = 10,
    [switch] $Window,
    [string] $OutputDirectory = 'artifacts/macos',
    [ValidateRange(30, 3600)][int] $CaseTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsMacOS) { throw 'macOS qualification runs on macOS.' }
$architecture = switch ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
    'Arm64' { 'arm64' }
    'X64' { 'x64' }
    default { throw "Unsupported macOS architecture $_." }
}
$productRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$mediaPath = (Resolve-Path -LiteralPath $Media).Path

& dotnet publish (Join-Path $productRoot 'src/Conic.Vout/Conic.Vout.csproj') -c Release -r "osx-$architecture" -nologo *> (Join-Path $output 'plugin-publish.log')
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed; see $output/plugin-publish.log" }
& dotnet build (Join-Path $productRoot 'src/Conic/Conic.csproj') -c Debug -nologo *> (Join-Path $output 'app-build.log')
if ($LASTEXITCODE -ne 0) { throw "App build failed; see $output/app-build.log" }

$app = Join-Path $productRoot 'src/Conic/bin/Debug/net10.0/Conic.dll'
$environment = @{
    CONIC_TEST_MEDIA = $mediaPath
    CONIC_PROFILE = Join-Path $output 'profile'
    CONIC_ARTIFACTS = $output
}
if ($VlcDirectory) { $environment.VLC_DIRECTORY = (Resolve-Path -LiteralPath $VlcDirectory).Path }

function Invoke-Case([string] $Name, [string[]] $Arguments) {
    $log = Join-Path $output "$Name.log"
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($app)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $outcome = if (-not $process.WaitForExit($CaseTimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        'timed out'
    }
    elseif ($process.ExitCode -eq 0) { 'passed' }
    else { "failed with exit $($process.ExitCode)" }
    @($stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult()) | Set-Content -LiteralPath $log
    Write-Host ('{0} {1} ({2:0.0} s)' -f $Name, $outcome, $watch.Elapsed.TotalSeconds)
    return $outcome -eq 'passed'
}

$cases = [ordered]@{
    'queue' = @('--queue-smoke')
    'media-capabilities' = @('--media-capabilities-smoke')
    'player-capabilities' = @('--player-capabilities-smoke')
    'iosurface' = @('--iosurface-smoke')
    'engine' = @('--engine-smoke', "--cycles=$Cycles")
}
if ($Window) {
    $cases['video-gpu'] = @('--video-smoke')
    $cases['video-cpu'] = @('--video-cpu-smoke')
    $cases['compositor'] = @('--ui-smoke')
    $cases['pip'] = @('--pip-smoke')
}
$failed = [Collections.Generic.List[string]]::new()
foreach ($case in $cases.GetEnumerator()) {
    if (-not (Invoke-Case $case.Key $case.Value)) { $failed.Add($case.Key) }
}
Write-Output "Logs: $output"
if ($failed.Count -ne 0) { throw "Failed: $($failed -join ', ')." }

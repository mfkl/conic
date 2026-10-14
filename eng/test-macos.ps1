#requires -Version 7.0
# Qualifies Conic on macOS against a real VLC 3 runtime:
#  - engine: queue, transport, tracks, delays, media information, lifecycle;
#  - IOSurface video output: BGRA pixel buffers lent and returned, with
#    hardware (VideoToolbox) and software decoding;
#  - with -Window, the styled window: GPU and CPU-fallback video, video
#    adjustments, PiP, compositor reattachment, AppKit clicks and menu
#    shortcuts on the caption row, and the UI smokes (Home, drawer, menus,
#    playback controls, Settings, accessibility), including the engine and
#    audio settings against fixtures from eng/make-settings-fixtures.ps1
#    (needs ffmpeg; the screen samples need the Screen Recording permission),
#    and the Messages window against the engine's real log.
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
    CONIC_ARTIFACTS = $output
    VLCLR_ROOT = Join-Path $productRoot 'external/vlclr'
}
if ($VlcDirectory) { $environment.VLC_DIRECTORY = (Resolve-Path -LiteralPath $VlcDirectory).Path }

# Each case starts from its own profile; $Overrides replaces environment entries.
function Invoke-Case([string] $Name, [string[]] $Arguments, [hashtable] $Overrides = @{}) {
    $log = Join-Path $output "$Name.log"
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($app)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $caseProfile = Join-Path $output "profile-$Name"
    if (Test-Path -LiteralPath $caseProfile) { Remove-Item -LiteralPath $caseProfile -Recurse -Force }
    $start.Environment['CONIC_PROFILE'] = $caseProfile
    foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    foreach ($entry in $Overrides.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $stderr = $process.StandardError.ReadToEndAsync()
    $lines = [Collections.Generic.List[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds($CaseTimeoutSeconds)
    $timedOut = $false
    $next = $process.StandardOutput.ReadLineAsync()
    while ($true) {
        $remaining = ($deadline - [DateTime]::UtcNow).TotalMilliseconds
        if ($remaining -le 0) { $timedOut = $true; break }
        if (-not $next.Wait([int][Math]::Min(1000, $remaining))) { continue }
        $line = $next.Result
        if ($null -eq $line) { break }
        $lines.Add($line)
        # --video-capture pauses after each "[video-capture] <name>" line. A
        # screen capture shows what the compositor presented, including
        # imported IOSurfaces, which an in-app RenderTargetBitmap does not.
        if ($line -match '^\[video-capture\] ([\w-]+)') {
            & screencapture -x (Join-Path $output "screen-$Name-$($Matches[1]).png")
        }
        $next = $process.StandardOutput.ReadLineAsync()
    }
    $outcome = if ($timedOut -or -not $process.WaitForExit([int][Math]::Max(1000, ($deadline - [DateTime]::UtcNow).TotalMilliseconds))) {
        $process.Kill($true)
        $process.WaitForExit()
        'timed out'
    }
    elseif ($process.ExitCode -eq 0) { 'passed' }
    else { "failed with exit $($process.ExitCode)" }
    @(($lines -join [Environment]::NewLine), $stderr.GetAwaiter().GetResult()) | Set-Content -LiteralPath $log
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
    $cases['video-gpu'] = @('--video-smoke', '--video-capture')
    $cases['video-cpu'] = @('--video-cpu-smoke', '--video-capture')
    $cases['compositor'] = @('--ui-smoke')
    $cases['pip'] = @('--pip-smoke')
    $cases['caption-click'] = @('--caption-click-smoke')
    foreach ($smoke in 'home', 'home-session', 'layout', 'drawer', 'menu', 'playback-controls', 'stop', 'media-information',
        'thumbnail', 'accessibility', 'scale', 'settings', 'settings-apply', 'audio-settings', 'engine-settings', 'interactions', 'parity', 'protocols') {
        $cases[$smoke] = @("--$smoke-smoke")
    }    # The drawer's native tracks and the settings smokes play the fixtures;
    # Quick Look makes no thumbnail for Matroska, so Home's gets an MP4.
    $fixtures = Join-Path $output 'settings-fixtures'
    & (Join-Path $PSScriptRoot 'make-settings-fixtures.ps1') -Output $fixtures | Out-Null
    $cases['drawer-native'] = @('--drawer-native-smoke')
    $cases['messages'] = @('--messages-smoke', '--video-capture')
    $overrides = @{
        'drawer-native' = @{ CONIC_TEST_MEDIA = Join-Path $fixtures 'languages.mkv' }
        'thumbnail' = @{ CONIC_TEST_MEDIA = Join-Path $fixtures 'gop.mp4' }
    }
}
# The hardware pass logs VLC's module selection. Without a VideoToolbox
# hardware decoder for the clip (an Intel runner), VLC falls back to avcodec.
$iosurfaceLog = Join-Path $output 'iosurface.log'
function Get-HardwareDecoder {
    if (-not (Test-Path -LiteralPath $iosurfaceLog)) { return $null }
    Select-String -LiteralPath $iosurfaceLog -Pattern 'using video decoder module "([^"]+)"' |
        Select-Object -First 1 | ForEach-Object { $_.Matches[0].Groups[1].Value }
}
$failed = [Collections.Generic.List[string]]::new()
foreach ($case in $cases.GetEnumerator()) {
    $caseOverrides = if ($overrides -and $overrides.Contains($case.Key)) { $overrides[$case.Key] } else { @{} }
    if (-not (Invoke-Case $case.Key $case.Value $caseOverrides)) { $failed.Add($case.Key) }
    # The engine settings smoke expects the decoder this Mac has.
    if ($case.Key -eq 'iosurface' -and ($decoder = Get-HardwareDecoder)) { $environment.CONIC_HARDWARE_DECODER = $decoder }
}
if (Test-Path -LiteralPath $iosurfaceLog) { Write-Output "Hardware-decoding pass used the '$((Get-HardwareDecoder) ?? 'unknown')' decoder." }
Write-Output "Logs: $output"
if ($failed.Count -ne 0) { throw "Failed: $($failed -join ', ')." }

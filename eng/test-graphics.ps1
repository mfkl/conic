[CmdletBinding()]
param(
    [string] $Application = 'src/VLCAvalonia/bin/Release/net10.0/VLCAvalonia.dll',
    [string] $OutputDirectory = 'artifacts/graphics-ci',
    [switch] $UseXvfb,
    [switch] $AllowUnavailable
)

$ErrorActionPreference = 'Stop'
$applicationPath = (Resolve-Path -LiteralPath $Application).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
# A unique path prevents a previous report from satisfying this invocation.
$reportPath = Join-Path $output "graphics-$([Guid]::NewGuid().ToString('N')).json"
$start = [Diagnostics.ProcessStartInfo]::new()
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($UseXvfb) {
    if (-not $IsLinux) { throw '-UseXvfb requires Linux.' }
    $start.FileName = (Get-Command xvfb-run).Source
    foreach ($argument in @('--auto-servernum', '--server-args=-screen 0 1280x720x24', 'dotnet')) { $start.ArgumentList.Add($argument) }
    $start.Environment['XDG_SESSION_TYPE'] = 'x11'
}
else { $start.FileName = (Get-Command dotnet).Source }
foreach ($argument in @($applicationPath, '--graphics-probe', "--report=$reportPath")) { $start.ArgumentList.Add($argument) }
# The diagnostic must remain independent of native VLC and source discovery.
$start.Environment['VLC_DIRECTORY'] = Join-Path $output 'no-vlc-runtime'
$start.Environment['VLCLR_ROOT'] = Join-Path $output 'no-vlclr-checkout'
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    if (-not $process.Start()) { throw 'Could not start the graphics diagnostic.' }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(45000)) {
        $process.Kill($true)
        $process.WaitForExit()
        $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stdout.log')
        $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stderr.log')
        throw 'The graphics diagnostic exceeded its 45-second process timeout.'
    }
    $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stdout.log')
    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $output 'stderr.log')
    if ($process.ExitCode -notin @(0, 2)) { throw "The graphics diagnostic failed with exit code $($process.ExitCode); see stderr.log." }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw 'The graphics diagnostic did not produce a report.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.SchemaVersion -ne 1 -or $report.HardwarePlaybackQualified -isnot [bool] -or $report.HardwarePlaybackQualified -or $report.Error) {
        throw "Invalid or failed graphics report: $($report.Error)"
    }
    if ($report.InteropAvailable -isnot [bool] -or $report.DeviceLost -isnot [bool] -or $report.ImageHandleTypes -isnot [array]) {
        throw 'The graphics report is missing required capability fields.'
    }
    $available = $report.InteropAvailable -and -not $report.DeviceLost -and $report.ImageHandleTypes.Count -gt 0
    $expectedExitCode = if ($available) { 0 } else { 2 }
    if ($process.ExitCode -ne $expectedExitCode) { throw 'Graphics exit code disagrees with its report.' }
    $outcome = if ($available) { 'interop-advertised' } else { 'interop-unavailable' }
    [ordered]@{
        result = $outcome
        exitCode = $process.ExitCode
        display = $(if ($UseXvfb) { 'Xvfb X11' } else { 'runner desktop' })
        hardwarePlaybackQualified = $false
        report = [IO.Path]::GetFileName($reportPath)
    } | ConvertTo-Json | Set-Content (Join-Path $output 'result.json')
    Write-Output "Graphics diagnostic: $outcome; native playback was not started."
    if ($env:GITHUB_STEP_SUMMARY) {
        "Graphics diagnostic: **$outcome**. This checks GUI startup and capability reporting; hardware decoding/presentation is unqualified." >> $env:GITHUB_STEP_SUMMARY
    }
    if (-not $available -and -not $AllowUnavailable) { throw 'The compositor does not advertise usable image interop.' }
}
finally { $process.Dispose() }

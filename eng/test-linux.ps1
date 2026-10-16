#requires -Version 7.0
# Qualifies Conic on Linux x64 against the installed VLC 3 packages
# (libvlccore9, libvlc5, vlc-plugin-base):
#  - always: queue, media and player capabilities and the engine lifecycle,
#    with VLC's dummy video output;
#  - with -Window, the DMA-BUF video output (VA-API conversion, Avalonia EGL
#    import), its CPU fallback, compositor reattachment, PiP and the UI and
#    settings smokes, against eng/make-settings-fixtures.ps1's fixtures
#    (needs ffmpeg). Avalonia's native Wayland backend is used unless -X11.
#    The engine settings smoke reads the screen through X11, so it always
#    runs on X11 (Xwayland in a Wayland session).
#  - with -Headless, window cases run in a private headless GNOME Shell
#    (Wayland, and a nested Xwayland with its own window manager for X11),
#    so no window reaches the desktop and none takes the focus.
# Distribution VLC 3 before 3.0.22 has no VA-API decoder: video decodes in
# software and is copied into VA-API surfaces, so this qualifies the GPU
# conversion and presentation, not hardware decoding.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Media,
    [ValidateRange(1, 100)][int] $Cycles = 10,
    [switch] $Window,
    [switch] $Headless,
    [switch] $X11,
    [string] $OutputDirectory = 'artifacts/linux',
    [string[]] $Only,
    [string] $Compiler,
    [ValidateRange(30, 3600)][int] $CaseTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Linux qualification runs on Linux x64.'
}
$productRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$mediaPath = (Resolve-Path -LiteralPath $Media).Path

[string[]] $aot = if ($Compiler) { "-p:CppCompilerAndLinker=$Compiler" } else { @() }
& dotnet publish (Join-Path $productRoot 'src/Conic.Vout/Conic.Vout.csproj') -c Release -r linux-x64 -nologo @aot *> (Join-Path $output 'plugin-publish.log')
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed; see $output/plugin-publish.log" }
& dotnet build (Join-Path $productRoot 'src/Conic/Conic.csproj') -c Debug -nologo *> (Join-Path $output 'app-build.log')
if ($LASTEXITCODE -ne 0) { throw "App build failed; see $output/app-build.log" }

$app = Join-Path $productRoot 'src/Conic/bin/Debug/net10.0/Conic.dll'
$environment = @{
    CONIC_TEST_MEDIA = $mediaPath
    CONIC_ARTIFACTS = $output
    VLCLR_ROOT = Join-Path $productRoot 'external/vlclr'
}
if ($X11) { $environment.CONIC_WAYLAND = '0' }

$session = $null
function Start-Owned([string] $File, [string[]] $Arguments, [hashtable] $Environment) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($entry in $Environment.GetEnumerator()) {
        if ($null -eq $entry.Value) { $start.Environment.Remove($entry.Key) | Out-Null } else { $start.Environment[$entry.Key] = $entry.Value }
    }
    $process = [Diagnostics.Process]::Start($start)
    $null = $process.StandardOutput.ReadToEndAsync()
    $null = $process.StandardError.ReadToEndAsync()
    return $process
}

function Wait-For([scriptblock] $Condition, [int] $Seconds, [string] $What) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "$What did not start." }
        Start-Sleep -Milliseconds 100
    }
}

function New-Bus {
    $bus = @(& dbus-daemon --session --fork --print-address=1 --print-pid=1)
    if ($LASTEXITCODE -ne 0 -or $bus.Count -lt 2) { throw 'Could not start a private D-Bus session.' }
    return $bus
}

function Start-HeadlessSession {
    foreach ($tool in 'dbus-daemon', 'gnome-shell', 'Xwayland') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "-Headless needs $tool." }
    }
    $buses = [Collections.Generic.List[string]]::new()
    $processes = [Collections.Generic.List[Diagnostics.Process]]::new()
    $session = @{ Processes = $processes; Buses = $buses }
    try {
        $bus = New-Bus; $buses.Add($bus[1])
        $name = "conic-headless-$PID"
        $socket = Join-Path $env:XDG_RUNTIME_DIR $name
        $shell = Start-Owned 'gnome-shell' @('--headless', '--wayland', '--no-x11', "--wayland-display=$name", '--virtual-monitor', '1920x1080') @{
            DBUS_SESSION_BUS_ADDRESS = $bus[0]; WAYLAND_DISPLAY = $null; DISPLAY = $null }
        $processes.Add($shell)
        Wait-For { Test-Path -LiteralPath $socket } 30 'The headless GNOME Shell'

        $display = 90..199 | Where-Object { -not (Test-Path "/tmp/.X11-unix/X$_") -and -not (Test-Path "/tmp/.X$_-lock") } | Select-Object -First 1
        $xwayland = Start-Owned 'Xwayland' @(":$display", '-geometry', '1920x1080') @{ WAYLAND_DISPLAY = $name; DISPLAY = $null }
        $processes.Add($xwayland)
        Wait-For { Test-Path "/tmp/.X11-unix/X$display" } 15 'Xwayland'
        $windowBus = New-Bus; $buses.Add($windowBus[1])
        $windowManager = Start-Owned 'gnome-shell' @('--x11', '--replace') @{
            DBUS_SESSION_BUS_ADDRESS = $windowBus[0]; DISPLAY = ":$display"; WAYLAND_DISPLAY = $null }
        $processes.Add($windowManager)
        Wait-For { (& xprop -display ":$display" -root _NET_SUPPORTING_WM_CHECK 2>$null) -match 'window id' } 30 'The X11 window manager'

        $environment.DBUS_SESSION_BUS_ADDRESS = $bus[0]
        $environment.WAYLAND_DISPLAY = $name
        $environment.DISPLAY = ":$display"
        $environment.XDG_SESSION_TYPE = 'wayland'
        Write-Host "Headless session: WAYLAND_DISPLAY=$name DISPLAY=:$display"
        return $session
    }
    catch {
        Stop-HeadlessSession $session
        throw
    }
}

function Stop-HeadlessSession($Session) {
    if (-not $Session) { return }
    foreach ($process in $Session.Processes) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    }
    foreach ($busPid in $Session.Buses) { & kill $busPid 2>$null }
}

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
    'engine' = @('--engine-smoke', "--cycles=$Cycles")
}
$overrides = @{}
if ($Window) {
    $cases['video-gpu'] = @('--video-smoke')
    $cases['video-cpu'] = @('--video-cpu-smoke')
    $cases['compositor'] = @('--ui-smoke')
    $cases['pip'] = @('--pip-smoke')
    foreach ($smoke in 'home', 'home-session', 'layout', 'drawer', 'menu', 'playback-controls', 'stop', 'media-information',
        'thumbnail', 'accessibility', 'scale', 'settings', 'settings-apply', 'audio-settings', 'engine-settings', 'interactions', 'parity', 'protocols') {
        $cases[$smoke] = @("--$smoke-smoke")
    }
    $fixtures = Join-Path $output 'settings-fixtures'
    & (Join-Path $PSScriptRoot 'make-settings-fixtures.ps1') -Output $fixtures | Out-Null
    $cases['drawer-native'] = @('--drawer-native-smoke')
    $cases['messages'] = @('--messages-smoke')
    $overrides = @{
        'drawer-native' = @{ CONIC_TEST_MEDIA = Join-Path $fixtures 'languages.mkv' }
        'thumbnail' = @{ CONIC_TEST_MEDIA = Join-Path $fixtures 'gop.mp4' }
        'engine-settings' = @{ CONIC_WAYLAND = '0' }
    }
}

$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$failed = [Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases.GetEnumerator()) {
        if ($Only.Count -ne 0 -and $case.Key -notin $Only) { continue }
        if ($Headless -and $Window -and -not $session -and $case.Key -notin 'queue', 'media-capabilities', 'player-capabilities', 'engine') {
            $session = Start-HeadlessSession
        }
        $caseOverrides = if ($overrides.Contains($case.Key)) { $overrides[$case.Key] } else { @{} }
        if (-not (Invoke-Case $case.Key $case.Value $caseOverrides)) { $failed.Add($case.Key) }
    }
}
finally {
    Stop-HeadlessSession $session
}
if (-not $Window) {
    Write-Output 'Engine only: VLC used its dummy video output and software decoding. GPU presentation was not tested.'
}
Write-Output "Logs: $output"
if ($failed.Count -ne 0) { throw "Failed: $($failed -join ', ')." }

#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Archive,
    [Parameter(Mandatory)][string] $Media,
    [ValidateRange(1, 100)][int] $Cycles = 25,
    [string] $OutputDirectory = 'artifacts/package-tests'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows package qualification requires Windows with a graphical session and GPU.' }
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$mediaPath = (Resolve-Path -LiteralPath $Media).Path
$output = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
# Exercise spaces, Unicode and installation outside either source checkout.
$installation = Join-Path ([IO.Path]::GetTempPath()) ('Conic é ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output, $installation | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $installation)
$directories = @(Get-ChildItem -LiteralPath $installation -Directory)
if ($directories.Count -ne 1) { throw 'Expected one top-level package directory.' }
$appDirectory = $directories[0].FullName
$app = Join-Path $appDirectory 'Conic.exe'
$manifest = Get-Content -LiteralPath (Join-Path $appDirectory 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.rid -ne 'win-x64' -or $manifest.selfContained -ne $true) { throw 'Invalid Windows package manifest.' }
foreach ($file in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $appDirectory $file.path))
    if (-not $target.StartsWith($appDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package.' }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Package content does not match manifest: $($file.path)"
    }
}
foreach ($required in @('Conic.exe', 'coreclr.dll', 'hostfxr.dll', 'vlc/libvlccore.dll', 'vlc/plugins/video_output/libvlc_avalonia_plugin.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appDirectory $required) -PathType Leaf)) { throw "Missing self-contained package component: $required" }
}
$results = [Collections.Generic.List[object]]::new()
function Invoke-PackageCase([string] $Name, [string[]] $Arguments, [int] $ExpectedExit = 0, [bool] $InvalidRuntime = $false) {
    $start = [Diagnostics.ProcessStartInfo]::new($app)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $installation
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment.Remove('VLC_DIRECTORY') | Out-Null
    $start.Environment.Remove('VLC_PLUGIN_PATH') | Out-Null
    $start.Environment['VLCLR_ROOT'] = Join-Path $installation 'no-framework-checkout'
    $start.Environment['DOTNET_ROOT'] = Join-Path $installation 'no-dotnet-installation'
    $start.Environment['CONIC_TEST_MEDIA'] = $mediaPath
    $start.Environment['CONIC_PROFILE'] = Join-Path $output "profiles/$Name"
    $start.Environment['CONIC_ARTIFACTS'] = Join-Path $output 'captures'
    if ($InvalidRuntime) { $start.Environment['VLC_DIRECTORY'] = Join-Path $installation 'missing-runtime' }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $outcome = 'failed'
    $exitCode = $null
    try {
        if (-not $process.Start()) { throw "Could not start $Name" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timeout = if ($Name -eq 'engine') { 900000 } else { 180000 }
        $timedOut = -not $process.WaitForExit($timeout)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $out = $stdout.GetAwaiter().GetResult()
        $err = $stderr.GetAwaiter().GetResult()
        $out | Set-Content -LiteralPath (Join-Path $output "$Name.stdout.log")
        $err | Set-Content -LiteralPath (Join-Path $output "$Name.stderr.log")
        $exitCode = $process.ExitCode
        if ($timedOut) { throw "$Name timed out." }
        if ($exitCode -ne $ExpectedExit) { throw "$Name exited $exitCode; expected $ExpectedExit. See $output" }
        $pathArgument = $Arguments | Where-Object { $_.StartsWith('--expect-path=') } | Select-Object -First 1
        if ($ExpectedExit -eq 0) {
            $evidencePattern = if ($pathArgument) { '\[composition-smoke\] path=' + [regex]::Escape($pathArgument.Substring(14)) + ' ' } else { '\bPASS\b' }
            if ($out -notmatch $evidencePattern) { throw "$Name did not record the expected smoke evidence." }
            if ($err -match 'stale plugins cache') { throw "$Name found a stale packaged plugin cache." }
        }
        if ($ExpectedExit -eq 2 -and $err -notmatch '\[startup\]') { throw "$Name did not report an actionable startup failure." }
        $outcome = 'passed'
        Write-Output "$Name passed ($([math]::Round($timer.Elapsed.TotalSeconds, 1)) s)"
    }
    finally {
        $results.Add([ordered]@{ name = $Name; result = $outcome; exitCode = $exitCode; seconds = $timer.Elapsed.TotalSeconds; arguments = $Arguments })
        $process.Dispose()
    }
}

try {
    Invoke-PackageCase 'hardware' @('--smoke', '--expect-path=hardware-opaque-gpu')
    Invoke-PackageCase 'software-upload' @('--smoke', '--expect-path=software-bgra-gpu-upload')
    Invoke-PackageCase 'cpu-bitmap' @('--smoke', '--expect-path=cpu-bitmap')
    Invoke-PackageCase 'engine' @('--engine-smoke', "--cycles=$Cycles")
    Invoke-PackageCase 'queue' @('--queue-smoke')
    Invoke-PackageCase 'compositor' @('--ui-smoke')
    Invoke-PackageCase 'home-session' @('--home-session-smoke')
    Invoke-PackageCase 'pip' @('--pip-smoke')
    Invoke-PackageCase 'settings' @('--settings-smoke')
    Invoke-PackageCase 'video-adjustments' @('--video-smoke')
    Invoke-PackageCase 'missing-runtime' @('--smoke', '--expect-path=hardware-opaque-gpu') 2 $true
    # Only rename a known file inside this invocation's new temporary installation.
    $plugin = [IO.Path]::GetFullPath((Join-Path $appDirectory 'vlc/plugins/video_output/libvlc_avalonia_plugin.dll'))
    if (-not $plugin.StartsWith($installation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Plugin is outside the test installation.' }
    $disabled = "$plugin.disabled"
    Move-Item -LiteralPath $plugin -Destination $disabled
    try { Invoke-PackageCase 'missing-plugin' @('--smoke', '--expect-path=hardware-opaque-gpu') 2 }
    finally { Move-Item -LiteralPath $disabled -Destination $plugin }
}
finally {
    [ordered]@{
        schemaVersion = 1; archive = $archivePath; archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        mediaSha256 = (Get-FileHash -LiteralPath $mediaPath -Algorithm SHA256).Hash
        productCommit = $manifest.productCommit; workingTreeChanges = $manifest.workingTreeChanges
        frameworkCommit = $manifest.frameworkCommit; sdk = $manifest.sdk; installation = $installation
        os = [Environment]::OSVersion.VersionString; architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        completedUtc = [DateTime]::UtcNow.ToString('o'); results = @($results.ToArray())
        limits = @('Existing developer machine, not a fresh OS installation.', 'No subtitle, physical monitor/sleep, other GPU or 60-minute soak qualification.')
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'report.json')
    Write-Output "Report and logs: $output"
    Write-Output "Temporary installation retained for inspection: $installation"
}

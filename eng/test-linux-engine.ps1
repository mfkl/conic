#requires -Version 7.0
# Qualifies the VLC 3 engine on Linux x64: queue, transport, tracks, delays,
# media information and the open/stop/dispose lifecycle against the installed
# VLC runtime. VLC renders to its dummy video output and decodes in software,
# so this says nothing about hardware decoding or GPU presentation.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Media,
    [ValidateRange(1, 100)][int] $Cycles = 25,
    [string] $OutputDirectory = 'artifacts/linux-engine',
    # Native AOT compiler driver. Defaults to clang; gcc works where clang is not installed.
    [string] $Compiler,
    [ValidateRange(30, 3600)][int] $CaseTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Linux engine qualification runs on Linux x64.'
}
$productRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$mediaPath = (Resolve-Path -LiteralPath $Media).Path

[string[]] $aot = if ($Compiler) { "-p:CppCompilerAndLinker=$Compiler" } else { @() }
& dotnet publish (Join-Path $productRoot 'src/VLCAvalonia.Vout/VLCAvalonia.Vout.csproj') -c Release -r linux-x64 -nologo @aot *> (Join-Path $output 'plugin-publish.log')
if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed; see $output/plugin-publish.log" }
& dotnet build (Join-Path $productRoot 'src/VLCAvalonia/VLCAvalonia.csproj') -c Debug -nologo *> (Join-Path $output 'app-build.log')
if ($LASTEXITCODE -ne 0) { throw "App build failed; see $output/app-build.log" }

$app = Join-Path $productRoot 'src/VLCAvalonia/bin/Debug/net10.0/VLCAvalonia.dll'
$env:VLC_AVALONIA_TEST_MEDIA = $mediaPath
$env:VLC_AVALONIA_PROFILE = Join-Path $output 'profile'
$cases = [ordered]@{
    'queue' = @('--queue-smoke')
    'media-capabilities' = @('--media-capabilities-smoke')
    'player-capabilities' = @('--player-capabilities-smoke')
    'engine' = @('--engine-smoke', "--cycles=$Cycles")
}
$failed = [Collections.Generic.List[string]]::new()
foreach ($case in $cases.GetEnumerator()) {
    $log = Join-Path $output "$($case.Key).log"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & timeout --kill-after=10 $CaseTimeoutSeconds dotnet $app @($case.Value) *> $log
    $code = $LASTEXITCODE
    $outcome = if ($code -eq 0) { 'passed' } elseif ($code -eq 124) { 'timed out' } else { "failed with exit $code" }
    Write-Output ('{0} {1} ({2:0.0} s)' -f $case.Key, $outcome, $watch.Elapsed.TotalSeconds)
    if ($code -ne 0) { $failed.Add($case.Key) }
}
Write-Output 'Engine only: VLC used its dummy video output and software decoding. Hardware decoding and GPU presentation were not tested.'
Write-Output "Logs: $output"
if ($failed.Count -ne 0) { throw "Failed: $($failed -join ', ')." }

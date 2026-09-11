[CmdletBinding()]
param(
    [string] $FrameworkRoot = $env:VLCLR_ROOT,
    [string] $VlcSourceRoot = '../vlc',
    [string] $OutputDirectory = 'artifacts/native-abi',
    [string] $Compiler
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$productRoot = Split-Path $PSScriptRoot -Parent
$pins = Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$report = [ordered]@{
    schemaVersion = 1
    timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    vlclrCommit = $pins.vlclr.commit
    vlcSourceCommit = $pins.vlc.sourceCommit
    moduleAbi = $pins.vlc.moduleAbi
    stage = 'prerequisites'
    result = 'failed'
    qualification = 'header-layout-only'
    hardwarePlaybackQualified = $false
}

function Assert-PinnedRepository([string] $Path, [string] $Commit, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "Set the $Label checkout path." }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $actual = & git -C $resolved rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual -ne $Commit) { throw "$Label must be at $Commit; found '$actual'." }
    $dirty = & git -C $resolved status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "$Label must have clean tracked files." }
    return $resolved
}

try {
    $framework = Assert-PinnedRepository $FrameworkRoot $pins.vlclr.commit 'VLCLR'
    $vlcSource = Assert-PinnedRepository $VlcSourceRoot $pins.vlc.sourceCommit 'VLC source'
    $probe = Join-Path $framework 'tests/AbiValidation/vlclr_abi.cpp'
    $include = Join-Path $vlcSource 'include'
    if (-not (Test-Path -LiteralPath $probe) -or -not (Test-Path -LiteralPath (Join-Path $include 'vlc_common.h'))) {
        throw 'The pinned ABI probe or VLC source headers are missing.'
    }
    $report.probeSha256 = (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.productCommit = & git -C $productRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve product revision.' }
    $report.stage = 'compiler'

    if ($IsWindows) {
        if ($Compiler) { throw 'Windows uses the MSVC x64 toolchain; omit -Compiler.' }
        if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
            $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if ($LASTEXITCODE -ne 0 -or -not $installation) { throw 'Install the Visual Studio C++ x64 tools.' }
            Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
            Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
        }
        if ($env:VSCMD_ARG_TGT_ARCH -ne 'x64') { throw 'Use an x64 MSVC developer shell.' }
        $compilerPath = (Get-Command cl.exe).Source
        $report.compiler = (Get-Item -LiteralPath $compilerPath).VersionInfo.FileVersion
        $report.compilerTarget = 'windows-x64'
        $executable = Join-Path $output 'vlclr-abi.exe'
        $compilerArguments = @('/nologo', '/std:c++17', '/EHsc', '/TP', '/W0', "/I$include", $probe, "/Fe$executable", "/Fo$(Join-Path $output 'vlclr-abi.obj')")
    }
    else {
        $compilerPath = if ($Compiler) { (Get-Command $Compiler).Source } else { (Get-Command c++).Source }
        $report.compiler = (@(& $compilerPath --version) | Select-Object -First 1)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read compiler version.' }
        $report.compilerTarget = (& $compilerPath -dumpmachine)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read compiler target.' }
        $executable = Join-Path $output 'vlclr-abi'
        $compilerArguments = @('-std=c++17', '-w', "-I$include", $probe, '-o', $executable, '-pthread')
    }

    $report.stage = 'compile'
    & $compilerPath @compilerArguments *> (Join-Path $output 'compile.log')
    $report.compileExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "ABI probe compilation failed; see $output/compile.log." }
    $report.stage = 'execute'
    & $executable *> (Join-Path $output 'probe.log')
    $report.probeExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "ABI probe execution failed; see $output/probe.log." }
    $report.stage = 'complete'
    $report.result = 'passed'
    Get-Content (Join-Path $output 'probe.log')
    Write-Output 'Header layout checks passed. Native calls, decoded surfaces and hardware playback remain unqualified.'
}
catch {
    $report.error = $_.Exception.Message
    throw
}
finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'report.json') -Encoding utf8
}

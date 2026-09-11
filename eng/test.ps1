[CmdletBinding()]
param(
    [ValidateSet('Core', 'Desktop', 'Windows')]
    [string] $Scope = 'Core',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $FrameworkRoot = $env:VLCLR_ROOT,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$productRoot = Split-Path $PSScriptRoot -Parent
$results = Join-Path $productRoot "artifacts/ci/$Scope/$Configuration"

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed with exit code $LASTEXITCODE" }
}

Push-Location $productRoot
$previousFrameworkRoot = $env:VLCLR_ROOT
try {
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $restoreArguments = @()
    if ($NoRestore) { $restoreArguments = @('--no-restore') }

    if ($Scope -eq 'Core') {
        # These projects have no dependency on VLCLR, VLC or a graphical session.
        foreach ($project in @('Player.Application.Tests', 'Player.Engine.ContractTests')) {
            Invoke-Dotnet test "tests/$project/$project.csproj" -c $Configuration @restoreArguments `
                --logger "trx;LogFileName=$project.trx" --results-directory $results --verbosity minimal
        }
    }
    else {
        if ($Scope -eq 'Windows' -and -not $IsWindows) { throw 'The full Windows job requires a Windows runner.' }
        if ([string]::IsNullOrWhiteSpace($FrameworkRoot)) { throw 'Set VLCLR_ROOT to the pinned VLCLR checkout.' }
        $env:VLCLR_ROOT = (Resolve-Path -LiteralPath $FrameworkRoot).Path
        $pins = Get-Content eng/versions.json -Raw | ConvertFrom-Json
        $actual = & git -C $env:VLCLR_ROOT rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $actual -ne $pins.vlclr.commit) {
            throw "VLCLR must be at $($pins.vlclr.commit); found '$actual'."
        }
        $dirty = & git -C $env:VLCLR_ROOT status --porcelain --untracked-files=no
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'The pinned VLCLR checkout must have no modified tracked files.' }

        # Desktop builds the real app through its test project, without building
        # the Windows-only Native AOT vout or loading any native VLC library.
        $buildTarget = if ($Scope -eq 'Desktop') { 'tests/Conic.UITests/Conic.UITests.csproj' } else { 'conic.slnx' }
        Invoke-Dotnet build $buildTarget -c $Configuration @restoreArguments '-p:UsedAvaloniaProducts=' --verbosity minimal
        Invoke-Dotnet test $buildTarget -c $Configuration --no-build --no-restore `
            --logger trx --results-directory $results --verbosity minimal
        if ($Scope -eq 'Windows' -and $Configuration -eq 'Release') {
            Invoke-Dotnet publish src/Conic.Vout/Conic.Vout.csproj -c Release -r win-x64 @restoreArguments `
                -o (Join-Path $results 'native') --verbosity minimal
        }
        # Hosted runners validate compilation and managed tests. Hardware decode
        # and real-compositor smokes require a separately qualified GPU runner.
    }
}
finally {
    $env:VLCLR_ROOT = $previousFrameworkRoot
    Pop-Location
}

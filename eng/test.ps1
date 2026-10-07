[CmdletBinding()]
param(
    [ValidateSet('Core', 'Desktop', 'Windows')]
    [string] $Scope = 'Core',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
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
try {
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $restoreArguments = @()
    if ($NoRestore) { $restoreArguments = @('--no-restore') }

    if ($Scope -eq 'Core') {
        # These projects have no dependency on VLC or a graphical session.
        foreach ($project in @('Player.Application.Tests', 'Player.Engine.ContractTests')) {
            Invoke-Dotnet test "tests/$project/$project.csproj" -c $Configuration @restoreArguments `
                --logger "trx;LogFileName=$project.trx" --results-directory $results --verbosity minimal
        }
    }
    else {
        if ($Scope -eq 'Windows' -and -not $IsWindows) { throw 'The full Windows job requires a Windows runner.' }
        # Desktop builds the real app through its test project, without building
        # the Windows-only Native AOT vout or loading any native VLC library.
        $buildTarget = if ($Scope -eq 'Desktop') { 'tests/VLCAvalonia.UITests/VLCAvalonia.UITests.csproj' } else { 'vlc-avalonia.slnx' }
        Invoke-Dotnet build $buildTarget -c $Configuration @restoreArguments '-p:UsedAvaloniaProducts=' --verbosity minimal
        Invoke-Dotnet test $buildTarget -c $Configuration --no-build --no-restore `
            --logger trx --results-directory $results --verbosity minimal
        if ($Scope -eq 'Windows' -and $Configuration -eq 'Release') {
            Invoke-Dotnet publish src/VLCAvalonia.Vout/VLCAvalonia.Vout.csproj -c Release -r win-x64 @restoreArguments `
                -o (Join-Path $results 'native') --verbosity minimal
        }
        # Hosted runners validate compilation and managed tests. Hardware decode
        # and real-compositor smokes require a separately qualified GPU runner.
    }
}
finally {
    Pop-Location
}

#requires -Version 7.0
# Downloads the pinned official VLC 3 Windows x64 build, verifies its SHA-256 and
# extracts it to <OutputDirectory>/vlc-<version>. Prints the runtime directory.
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/dependencies'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Only the Windows x64 VLC runtime is pinned.' }
$pins = (Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json).vlc
$root = [IO.Path]::GetFullPath($OutputDirectory)
$destination = Join-Path $root "vlc-$($pins.version)"

if (Test-Path -LiteralPath (Join-Path $destination 'libvlccore.dll') -PathType Leaf) {
    Write-Output $destination
    return
}
if (Test-Path -LiteralPath $destination) { throw "Incomplete VLC runtime at $destination; delete it and retry." }

New-Item -ItemType Directory -Path $root -Force | Out-Null
$archive = Join-Path $root ([IO.Path]::GetFileName($pins.windows.url))
$staging = Join-Path $root ('vlc-extract-' + [Guid]::NewGuid().ToString('N'))
try {
    Invoke-WebRequest -Uri $pins.windows.url -OutFile $archive
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $pins.windows.sha256) { throw "SHA-256 mismatch for $($pins.windows.url): expected $($pins.windows.sha256), got $hash." }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $staging)
    $extracted = Join-Path $staging "vlc-$($pins.version)"
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'libvlccore.dll') -PathType Leaf)) {
        throw "The archive does not contain vlc-$($pins.version)/libvlccore.dll."
    }
    Move-Item -LiteralPath $extracted -Destination $destination
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
}
Write-Output $destination

#requires -Version 7.0
# Downloads the pinned official VLC 3 build for this machine and verifies its
# SHA-256. Windows x64: extracts the ZIP to <OutputDirectory>/vlc-<version>.
# macOS: copies lib/, plugins/ and share/ out of the DMG's VLC.app/Contents/MacOS
# to <OutputDirectory>/vlc-<version>-macos-<arch>, keeping the layout VLC's
# libraries reference each other through. Prints the directory holding
# libvlccore (the runtime directory).
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/dependencies'
)

$ErrorActionPreference = 'Stop'
$pins = (Get-Content (Join-Path $PSScriptRoot 'versions.json') -Raw | ConvertFrom-Json).vlc
$root = [IO.Path]::GetFullPath($OutputDirectory)

function Get-VerifiedDownload([string] $Url, [string] $Sha256) {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $file = Join-Path $root ([IO.Path]::GetFileName($Url))
    Invoke-WebRequest -Uri $Url -OutFile $file
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        Remove-Item -LiteralPath $file -Force
        throw "SHA-256 mismatch for ${Url}: expected $Sha256, got $hash."
    }
    return $file
}

if ($IsMacOS) {
    $architecture = switch ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        'Arm64' { 'arm64' }
        'X64' { 'x64' }
        default { throw "No VLC build is pinned for macOS $_." }
    }
    $pin = $pins.macos.$architecture
    $destination = Join-Path $root "vlc-$($pins.version)-macos-$architecture"
    $runtime = Join-Path $destination 'lib'
    if (Test-Path -Path (Join-Path $runtime 'libvlccore*.dylib')) {
        Write-Output $runtime
        return
    }
    if (Test-Path -LiteralPath $destination) { throw "Incomplete VLC runtime at $destination; delete it and retry." }

    $image = Get-VerifiedDownload $pin.url $pin.sha256
    $mount = Join-Path $root ('vlc-mount-' + [Guid]::NewGuid().ToString('N'))
    $staging = Join-Path $root ('vlc-extract-' + [Guid]::NewGuid().ToString('N'))
    try {
        & hdiutil attach -nobrowse -readonly -noautoopen -mountpoint $mount $image | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "hdiutil could not mount $image." }
        $contents = Join-Path $mount 'VLC.app/Contents/MacOS'
        New-Item -ItemType Directory -Path $staging | Out-Null
        foreach ($directory in 'lib', 'plugins', 'share') {
            # ditto keeps symbolic links and code signatures.
            & ditto (Join-Path $contents $directory) (Join-Path $staging $directory)
            if ($LASTEXITCODE -ne 0) { throw "Could not copy VLC.app's $directory directory." }
        }
        if (-not (Test-Path -Path (Join-Path $staging 'lib/libvlccore*.dylib'))) {
            throw "The disk image does not contain libvlccore in VLC.app/Contents/MacOS/lib."
        }
        Move-Item -LiteralPath $staging -Destination $destination
    }
    finally {
        if (Test-Path -LiteralPath $mount) { & hdiutil detach $mount -quiet | Out-Null }
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $image -Force -ErrorAction SilentlyContinue
    }
    Write-Output $runtime
    return
}

if (-not $IsWindows) { throw 'VLC is pinned for Windows x64 and macOS. On Linux, install the distribution''s VLC 3 packages.' }
$destination = Join-Path $root "vlc-$($pins.version)"
if (Test-Path -LiteralPath (Join-Path $destination 'libvlccore.dll') -PathType Leaf) {
    Write-Output $destination
    return
}
if (Test-Path -LiteralPath $destination) { throw "Incomplete VLC runtime at $destination; delete it and retry." }

$archive = Get-VerifiedDownload $pins.windows.url $pins.windows.sha256
$staging = Join-Path $root ('vlc-extract-' + [Guid]::NewGuid().ToString('N'))
try {
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

#requires -Version 7.0
# Downloads the pinned official VLC 3 build for this machine and verifies its
# SHA-256. Windows x64: extracts the ZIP to <OutputDirectory>/vlc-<version>.
# macOS: copies lib/, plugins/ and share/ out of the DMG's VLC.app/Contents/MacOS
# to <OutputDirectory>/vlc-<version>-macos-<arch>, keeping the layout VLC's
# libraries reference each other through. Linux x64: downloads VideoLAN's VLC
# snap at the pinned revision and assembles <OutputDirectory>/vlc-<version>-linux:
# libvlccore and libvlc, vlc/ (plugins and their helper libraries), share/vlc,
# and deps/ with the libraries those need that a desktop does not provide,
# listed in deps/load-order.txt in the order to load them. Graphics, audio and
# core system libraries are left to the host, so its drivers are used.
# Prints the directory holding libvlccore (the runtime directory).
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

if ($IsLinux) {
    if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'No VLC build is pinned for Linux on this architecture.' }
    $pin = $pins.linux
    $destination = Join-Path $root "vlc-$($pins.version)-linux"
    if (Test-Path -LiteralPath (Join-Path $destination 'deps/load-order.txt') -PathType Leaf) {
        Write-Output $destination
        return
    }
    if (Test-Path -LiteralPath $destination) { throw "Incomplete VLC runtime at $destination; delete it and retry." }
    foreach ($tool in 'snap', 'unsquashfs', 'readelf') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Assembling the Linux VLC runtime needs $tool." }
    }
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $work = Join-Path $root ('vlc-snap-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $basename = "$($pin.snap)-$($pin.revision)"
        & snap download $pin.snap "--revision=$($pin.revision)" "--target-directory=$work" "--basename=$basename" *> (Join-Path $work 'download.log')
        if ($LASTEXITCODE -ne 0) { throw "snap download failed: $(Get-Content -Raw (Join-Path $work 'download.log'))" }
        $package = Join-Path $work "$basename.snap"
        $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $pin.sha256) { throw "SHA-256 mismatch for snap ${basename}: expected $($pin.sha256), got $hash." }
        $tree = Join-Path $work 'tree'
        & unsquashfs -no-progress -quiet -d $tree $package usr/lib usr/share/vlc *> (Join-Path $work 'unsquashfs.log')
        if ($LASTEXITCODE -ne 0) { throw "unsquashfs failed: $(Get-Content -Raw (Join-Path $work 'unsquashfs.log'))" }
        $lib = Join-Path $tree 'usr/lib'
        $staging = Join-Path $work 'runtime'
        New-Item -ItemType Directory -Path $staging | Out-Null
        foreach ($name in 'libvlccore.so.9', 'libvlc.so.5') {
            if (-not (Test-Path -LiteralPath (Join-Path $lib $name))) { throw "The snap has no usr/lib/$name." }
            Copy-Item -LiteralPath (Join-Path $lib $name) -Destination (Join-Path $staging $name)
        }
        Copy-Item -LiteralPath (Join-Path $lib 'vlc') -Destination (Join-Path $staging 'vlc') -Recurse
        foreach ($unused in 'vlc/plugins/gui', 'vlc/libcompat.a', 'vlc/plugins/plugins.dat') {
            Remove-Item -LiteralPath (Join-Path $staging $unused) -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path (Join-Path $staging 'share') | Out-Null
        Copy-Item -LiteralPath (Join-Path $tree 'usr/share/vlc') -Destination (Join-Path $staging 'share/vlc') -Recurse

        $hostPatterns = @(
            'ld-linux*', 'libc.so*', 'libm.so*', 'libdl.so*', 'libpthread.so*', 'librt.so*', 'libresolv.so*', 'libutil.so*',
            'libstdc++.so*', 'libgcc_s.so*', 'libatomic.so*', 'libcrypt.so*',
            'libGL*', 'libEGL*', 'libOpenGL*', 'libOpenCL*', 'libgbm.so*', 'libdrm*', 'libva*', 'libvdpau*', 'libvulkan*',
            'libX*', 'libxcb*', 'libxkb*', 'libwayland*', 'libxshmfence*', 'libdecor*', 'libLLVM*', 'libgallium*', 'libsensors*',
            'libasound*', 'libpulse*', 'libpipewire*', 'libjack*', 'libsndio*',
            'libdbus-1*', 'libsystemd*', 'libudev*', 'libglib-2.0*', 'libgobject-2.0*', 'libgio-2.0*', 'libgmodule-2.0*', 'libgthread-2.0*',
            'libgtk*', 'libgdk*', 'libcairo*', 'libpango*', 'libatk*', 'libatspi*', 'libharfbuzz*', 'libfontconfig*', 'libfreetype*', 'libepoxy*',
            'libexpat*', 'libz.so*', 'libuuid*', 'libmount*', 'libblkid*', 'libselinux*', 'libffi*', 'libpcre2*',
            'libgnutls*', 'libnettle*', 'libhogweed*', 'libgmp*', 'libp11-kit*', 'libtasn1*', 'libidn2*', 'libunistring*',
            'libssl.so*', 'libcrypto.so*', 'libgcrypt*', 'libgpg-error*', 'libcom_err*', 'libkrb5*', 'libgssapi*', 'libk5crypto*', 'libkeyutils*',
            'liblzma*', 'libzstd*', 'libbz2*', 'liblz4*', 'libbsd*', 'libmd.so*', 'libcap*', 'libtinfo*', 'libncurses*')
        function Test-HostLibrary([string] $Name) { foreach ($pattern in $hostPatterns) { if ($Name -like $pattern) { return $true } } return $false }
        function Get-Needed([string] $Path) {
            foreach ($line in (& readelf -d $Path 2>$null)) { if ($line -match 'Shared library: \[(.+)\]') { $Matches[1] } }
        }
        $provided = [Collections.Generic.HashSet[string]]::new([string[]]@('libvlccore.so.9', 'libvlc.so.5'))
        Get-ChildItem -Path (Join-Path $staging 'vlc') -Filter 'libvlc_*.so.*' -File | ForEach-Object { [void]$provided.Add($_.Name) }
        $sources = @(Join-Path $lib 'x86_64-linux-gnu'; $lib)
        $dependencies = [ordered]@{}
        $missing = [Collections.Generic.SortedSet[string]]::new()
        $order = [Collections.Generic.List[string]]::new()
        $visiting = [Collections.Generic.HashSet[string]]::new()
        function Add-Dependencies([string] $Path) {
            foreach ($name in (Get-Needed $Path)) {
                if ($provided.Contains($name) -or (Test-HostLibrary $name) -or $dependencies.Contains($name)) { continue }
                $source = $sources | ForEach-Object { Join-Path $_ $name } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
                if (-not $source) { [void]$missing.Add($name); continue }
                if (-not $visiting.Add($name)) { continue }
                $dependencies[$name] = $source
                Add-Dependencies $source
                $order.Add($name)
            }
        }
        $roots = @(Get-ChildItem -LiteralPath $staging -Recurse -File -Filter '*.so*' | Where-Object { $_.FullName -notlike '*/share/*' })
        foreach ($root in $roots) { Add-Dependencies $root.FullName }
        $deps = Join-Path $staging 'deps'
        New-Item -ItemType Directory -Path $deps | Out-Null
        foreach ($name in $order) { Copy-Item -LiteralPath $dependencies[$name] -Destination (Join-Path $deps $name) }
        $order | Set-Content -LiteralPath (Join-Path $deps 'load-order.txt')
        $missing | Set-Content -LiteralPath (Join-Path $deps 'host-libraries.txt')
        Move-Item -LiteralPath $staging -Destination $destination
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Output $destination
    return
}

if (-not $IsWindows) { throw 'VLC is pinned for Windows x64, macOS and Linux x64.' }
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

# Writes the media --engine-settings-smoke plays (phase 9b) to
# artifacts/settings-fixtures, with ffmpeg from PATH. Every file is
# synthetic: testsrc video, sine audio, two tiny subtitle files.
[CmdletBinding()]
param([string] $Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/settings-fixtures'))

$ErrorActionPreference = 'Stop'
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is required on PATH.' }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Push-Location $Output
try {
    function Invoke-Ffmpeg { & ffmpeg -hide_banner -loglevel error -y @args; if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $args" } }
    # A still frame at a low quality: strong blocking, so deblocking and
    # post-processing visibly change it; every frame is a key frame, so every
    # frame decodes to the same picture.
    Invoke-Ffmpeg -f lavfi -i testsrc=size=1280x720:rate=1 '-frames:v' 1 still.png
    Invoke-Ffmpeg -loop 1 -i still.png -t 20 -r 25 '-c:v' libx264 -preset veryfast -crf 46 -pix_fmt yuv420p -g 1 still-lowq.mp4
    # Key frames only every 10 s: a fast seek lands on one, a precise one does
    # not. The index comes first, so it also plays from the smoke's HTTP server.
    Invoke-Ffmpeg -f lavfi -i testsrc=size=640x360:rate=25 -t 30 '-c:v' libx264 -preset veryfast -x264-params keyint=250:min-keyint=250:scenecut=0 -pix_fmt yuv420p -movflags +faststart gop.mp4
    foreach ($n in 1, 2) {
        Invoke-Ffmpeg -f lavfi -i testsrc=size=640x360:rate=25 -f lavfi -i sine=frequency=440:duration=2 -t 2 '-c:v' libx264 -preset veryfast -pix_fmt yuv420p '-c:a' aac "short-$n.mp4"
    }
    # Heavy 4K 60 fps video: one software decoding thread cannot keep up at
    # 4x, so the video output has late pictures to drop.
    Invoke-Ffmpeg -f lavfi -i testsrc2=size=3840x2160:rate=60 -t 4 '-c:v' libx264 -preset ultrafast -crf 16 -pix_fmt yuv420p heavy.mp4
    # Two audio and two subtitle tracks, English first.
    Set-Content -NoNewline -Path en.srt -Value "1`n00:00:00,000 --> 00:00:30,000`nEnglish subtitle`n"
    Set-Content -NoNewline -Path fr.srt -Value "1`n00:00:00,000 --> 00:00:30,000`nSous-titre francais`n"
    Invoke-Ffmpeg -f lavfi -i testsrc=size=640x360:rate=25 -f lavfi -i sine=frequency=440:duration=10 -f lavfi -i sine=frequency=880:duration=10 -i en.srt -i fr.srt -t 10 `
        -map 0:v -map 1:a -map 2:a -map 3 -map 4 '-c:v' libx264 -preset veryfast -pix_fmt yuv420p '-c:a' aac '-c:s' srt `
        '-metadata:s:a:0' language=eng '-metadata:s:a:1' language=fre '-metadata:s:s:0' language=eng '-metadata:s:s:1' language=fre languages.mkv
    # An AVI without its index (idx1 cut off): VLC plays it as is unless told to repair it.
    Invoke-Ffmpeg -f lavfi -i testsrc=size=640x360:rate=25 -t 10 '-c:v' mpeg4 '-q:v' 5 -f avi indexed.avi
    [byte[]] $bytes = [IO.File]::ReadAllBytes((Join-Path $Output 'indexed.avi'))
    $text = [Text.Encoding]::ASCII.GetString($bytes)
    $index = $text.LastIndexOf('idx1')
    if ($index -lt 0) { throw 'indexed.avi has no idx1 chunk.' }
    [byte[]] $broken = $bytes[0..($index - 1)]
    [BitConverter]::GetBytes([int]($broken.Length - 8)).CopyTo($broken, 4)
    [IO.File]::WriteAllBytes((Join-Path $Output 'broken.avi'), $broken)
    Write-Output "Fixtures written to $Output"
}
finally { Pop-Location }

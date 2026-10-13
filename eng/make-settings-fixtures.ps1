# Writes the media --engine-settings-smoke (phase 9b),
# --audio-settings-smoke (phase 9c) and --interface-settings-smoke (phase
# 9d) play to artifacts/settings-fixtures, with ffmpeg from PATH. Every
# file is synthetic: testsrc and black video, sine audio, small subtitle
# files.
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
    # Audio (--audio-settings-smoke, phase 9c), 20 s at 48 kHz unless said.
    # Three tones (80 Hz, 1 kHz, 10 kHz) in the center: equalizer, karaoke, spatial audio.
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.2*sin(2*PI*80*t)+0.2*sin(2*PI*1000*t)+0.2*sin(2*PI*10000*t)|0.2*sin(2*PI*80*t)+0.2*sin(2*PI*1000*t)+0.2*sin(2*PI*10000*t):s=48000:d=20' '-c:a' pcm_s16le audio-mix.wav
    # 440 Hz on the left, 660 Hz on the right: stereo widening, pitch.
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.3*sin(2*PI*440*t)|0.3*sin(2*PI*660*t):s=48000:d=20' '-c:a' pcm_s16le audio-lr.wav
    # A loud second, then a quiet one: compressor, normalization.
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=if(lt(mod(t\,2)\,1)\,0.8\,0.02)*sin(2*PI*440*t)|if(lt(mod(t\,2)\,1)\,0.8\,0.02)*sin(2*PI*440*t):s=48000:d=20' '-c:a' pcm_s16le audio-dynamics.wav
    # Replay gain tags: track -10 dB, album -4 dB. Homebrew's ffmpeg has no
    # libvorbis; FFmpeg's own Vorbis encoder is experimental but enough here.
    $vorbis = if ((& ffmpeg -hide_banner -encoders 2>$null) -match '\blibvorbis\b') { @('-c:a', 'libvorbis', '-q:a', '4') } else { @('-c:a', 'vorbis', '-strict', 'experimental') }
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.3*sin(2*PI*440*t)|0.3*sin(2*PI*440*t):s=48000:d=20' @vorbis `
        -metadata 'REPLAYGAIN_TRACK_GAIN=-10.00 dB' -metadata 'REPLAYGAIN_TRACK_PEAK=0.3' -metadata 'REPLAYGAIN_ALBUM_GAIN=-4.00 dB' -metadata 'REPLAYGAIN_ALBUM_PEAK=0.3' audio-replaygain.ogg
    # AC-3 with the Dolby Surround flag set.
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.3*sin(2*PI*440*t)|0.3*sin(2*PI*660*t):s=48000:d=20' '-c:a' ac3 -dsur_mode on audio-dolby.ac3
    # Tones for the resampler: 6 kHz (images) and 20 kHz (the top of the band).
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.4*sin(2*PI*6000*t)|0.4*sin(2*PI*6000*t):s=48000:d=20' '-c:a' pcm_s16le audio-6k.wav
    Invoke-Ffmpeg -f lavfi -i 'aevalsrc=0.4*sin(2*PI*20000*t)|0.4*sin(2*PI*20000*t):s=48000:d=20' '-c:a' pcm_s16le audio-20k.wav
    # Subtitles (--interface-settings-smoke, phase 9d): black video, so the
    # text is all that shows. dark.srt (loaded with dark.mp4) has a new line
    # every second, so a style changed while playing shows within one.
    Invoke-Ffmpeg -f lavfi -i color=c=black:s=640x360:r=25 -t 20 '-c:v' libx264 -preset veryfast -pix_fmt yuv420p dark.mp4
    $lines = foreach ($i in 0..19) { "{0}`n00:00:{1:00},000 --> 00:00:{2:00},000`nSUBTITLE LINE {0}`n" -f ($i + 1), $i, ($i + 1) }
    Set-Content -NoNewline -Path dark.srt -Value ($lines -join "`n")
    # Cyrillic in Windows-1251 (not UTF-8): read right only with that encoding.
    Copy-Item dark.mp4 cyrillic.mp4
    [byte[]] $cyrillic = [Text.Encoding]::ASCII.GetBytes("1`n00:00:00,000 --> 00:00:20,000`n") + [byte[]](0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2) + [byte[]](0x0A)
    [IO.File]::WriteAllBytes((Join-Path $Output 'cyrillic.srt'), $cyrillic)
    # A subtitle file whose name has nothing of the media's: only "any subtitle file" matching loads it.
    New-Item -ItemType Directory -Force -Path matching | Out-Null
    Copy-Item dark.mp4 matching/film.mp4
    Set-Content -NoNewline -Path matching/unrelated.srt -Value "1`n00:00:00,000 --> 00:00:20,000`nUnrelated`n"
    Write-Output "Fixtures written to $Output"
}
finally { Pop-Location }

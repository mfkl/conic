# Feature parity with VLC 3

This document compares **Conic** at current `main` with the desktop (Qt)
interface of **VLC 3.0.24** on Windows and Linux. Conic runs the same VLC
3.0.24 engine (libvlccore through VLCLR), so codec, container and filter
support is identical wherever Conic's UI exposes it. The differences are in
what the UI lets you reach, and how.

Every row was checked against the code (`src/Conic`, `src/Player.Application`),
not only against older docs. Where the code did not settle a question, the
Notes say so.

Platform scope:

- **Windows x64**: full playback (D3D11 hardware decoding, GPU presentation).
- **macOS arm64/x64**: full playback (VideoToolbox, IOSurface). A native menu
  bar replaces the in-window menus. Shortcuts use Cmd instead of Ctrl. Note
  that VLC on macOS ships a Cocoa interface, not Qt; the VLC 3 column always
  describes the Qt interface.
- **Linux x64**: full playback (VA-API, DMA-BUF) on X11, and XWayland in
  Wayland sessions.

## Status legend

| Status | Meaning |
|---|---|
| Same | Works as in VLC 3 (possibly with a different look or place). |
| Different | Works, but the behavior, scope or place differs in a way that matters. |
| Partial | Some of VLC 3's feature is there, some is not. |
| Missing | Not in Conic. |
| Out of scope | Deliberately left out of Conic. |
| Conic only | Not in VLC 3's Qt interface (used only in the last section). |

**Parity** counts Same and Different as 1, Partial as 1/2 and Missing as 0,
over the features in scope (Out of scope is not counted).

Apply timing in Conic's Settings: **Live** applies at once, **Next open**
applies from the next media opened (each open starts a new VLC core with the
settings as options), **Restart** needs an app restart. Each Settings row
carries its tag.

## Summary

| Section | Same | Different | Partial | Missing | Out of scope | Total | Parity |
|---|---|---|---|---|---|---|---|
| Media menu | 3 | 4 | 3 | 0 | 4 | 14 | 85% |
| Playback menu | 10 | 1 | 0 | 1 | 2 | 14 | 92% |
| Audio menu | 3 | 2 | 0 | 1 | 0 | 6 | 83% |
| Video menu | 4 | 3 | 0 | 3 | 0 | 10 | 70% |
| Subtitle menu | 2 | 0 | 0 | 0 | 0 | 2 | 100% |
| Tools menu | 3 | 7 | 5 | 3 | 4 | 22 | 69% |
| View menu | 3 | 4 | 0 | 1 | 1 | 9 | 88% |
| Help menu | 1 | 1 | 0 | 0 | 1 | 3 | 100% |
| Preferences | 33 | 2 | 5 | 3 | 6 | 49 | 87% |
| Playlist and queue | 7 | 3 | 2 | 1 | 0 | 13 | 85% |
| Library | 0 | 1 | 0 | 1 | 3 | 5 | 50% |
| Hotkeys and mouse | 5 | 1 | 0 | 2 | 0 | 8 | 75% |
| Command line and OS integration | 5 | 1 | 2 | 3 | 2 | 13 | 64% |
| Network and streaming | 4 | 1 | 0 | 3 | 7 | 15 | 63% |
| **Total** | **83** | **31** | **17** | **22** | **30** | **183** | **80%** |

The Conic only section lists 13 features VLC 3's Qt interface does not have; they are not counted above.

### What's missing, and why

| Why | Missing features |
| --- | --- |
| Not in the redesign | Bookmarks, the Video Track menu, library folders (My Videos…), status bar, system title bar |
| Broken in VLC 3.0.24 | Post-processing (no visible effect), stereo widener (refuses the usual output chains) |
| Need VLC's own video window | Set as wallpaper, 360° video navigation |
| Need TV input or extra hardware | Program guide (EPG), Teletext, AtmoLight |
| Refused on purpose | Credentials in URLs (they would be kept in the history) |
| Not done | Visualizations, global hotkeys, the OS media controls, taskbar buttons, VLC options on the command line, mouse gestures, nested playlists |

The 17 partial features mostly have fewer options than VLC (some video
filters, playlist formats, queue sort) or work on Windows only (screensaver,
HTTP proxy). Disc menus, streaming and conversion, capture devices, skins and
extensions were left out from the start.

## Media menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Open File | Ctrl+O, multiple selection | Ctrl+O, multiple selection | Same | More menu, right-click Open Media, Home, macOS File menu. Several files open as a queue. The filter lists common video and audio formats plus All files. |
| Open Multiple Files | Ctrl+Shift+O, file list, subtitle file, options | Open File with several files | Different | No separate dialog, subtitle file or options field: Open File takes several files, and so does a drop. Ctrl+Shift+O opens a folder in Conic. |
| Open Folder | Ctrl+F, opens the directory | Ctrl+Shift+O | Different | Flattened into the queue, sorted by name, at most 2,000 items. Settings › Library: subfolders or not, hidden files or not (Next open). |
| Open Disc (DVD, Blu-ray, audio CD, VCD) | Yes | None | Out of scope | Conic opens local files and network URLs only. |
| Open Network Stream | Ctrl+N, any VLC protocol, options | Ctrl+L, one URL per line | Partial | http, https, ftp, ftps, sftp, smb, rtsp, rtp, udp, mms, mmsh and rtmp, passed to VLC as typed; several lines open as a queue. URLs with user:password are rejected, and there is no `:option` field. --protocols-smoke plays UDP, RTP, RTMP, FTP and SMB from local servers; RTSP, SFTP and MMS were not checked (no local server). |
| Open Capture Device | Yes | None | Out of scope | Left out in the menus redesign. |
| Open Location from clipboard | Ctrl+V | Ctrl+V, right-click Open Media, macOS File menu | Same | Each line that is a URL or a path opens; several as a queue. On macOS the File menu shows no Cmd+V, so text fields keep Paste. |
| Open Recent Media | Submenu, clear | More menu › Open Recent page, Home › Recent | Same | 50 entries, per-entry remove, Clear recent. Settings › Privacy turns history off (clears at once). |
| Open a playlist file | Through Open File (XSPF, M3U, PLS, and more) | Load Playlist (M3U, M3U8, XSPF, PLS) | Partial | Flat, at most 2,000 entries, no nested playlists. Not verified what happens when a playlist file is opened with Open File (it is handed to VLC as a file). |
| Save Playlist to File | Ctrl+Y, XSPF, M3U, M3U8, HTML | Save Playlist (M3U8, XSPF) | Partial | No HTML or plain M3U. In the queue panel and the macOS File menu. |
| Convert / Save | Yes | None | Out of scope | Transcoding is a non-goal. |
| Stream | Yes | None | Out of scope | Streaming output is a non-goal. |
| Quit at the end of playlist | Menu toggle | Settings › Playback › When playback finishes | Different | Stop, Go to Home or Quit, remembered. A persistent setting, not a per-session toggle. |
| Quit | Ctrl+Q | Close the window | Different | No Ctrl+Q command found. macOS: File › Close Window (Cmd+W); the application menu is Avalonia's default (not checked in detail). |

## Playback menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Title | Submenu | Queue panel › Chapters tab, "Titles and chapters" | Same | Selection is read back from VLC. Disc menus are out of scope (no disc input). |
| Chapter | Submenu, prev/next chapter | Chapters tab, right-click Playback, Shift+P / Shift+N | Same | Pick from the list, or step with VLC's keys; Previous restarts the chapter after 1.5 s, as in VLC. |
| Program | Submenu (DVB, TS programs) | X (Next Program), right-click Playback, macOS Playback menu | Different | Steps through the programs of a multi-program stream (DVB, MPEG-TS); no list to pick from. Shown when the media has more than one. |
| Custom Bookmarks | Ctrl+B, manage | None | Missing | Hidden on purpose (redesign decision D2). |
| Renderer (Chromecast) | Submenu, discovery | None | Out of scope | Casting is out of scope. |
| Speed | Faster, slower, normal, fine steps, 0.25x to 4x slider | Menu 0.5x to 2x, faster, slower, normal; panel slider 0.25x to 2x | Same | "]" and "[" take VLC's fine 0.1 steps and "=" resets; VLC's coarse "+" and "-" keys are not available (a shortcut cannot use them). Default speed in Settings › Playback (Next open). |
| Jump forward / backward | 4 sizes, Shift/Alt/Ctrl/Ctrl+Alt with arrows | Same 4 sizes and keys | Same | Defaults 3, 10, 60, 300 s as in VLC; Settings › Controls › Skip Amounts (Live). Short skip in bar, right-click menu, macOS menu. |
| Jump to specific time | Ctrl+T dialog | Ctrl+T dialog | Same | h:mm:ss, m:ss or seconds. Right-click Playback, macOS Playback menu. |
| Play / Pause | Space | Space | Same | Bar, right-click menu, macOS Playback menu. |
| Stop | S | S, bar, right-click menu, More menu | Same | |
| Previous / Next | P / N | P / N, bar, right-click menu, macOS menu | Same | |
| Record | Advanced controls | None | Out of scope | Recording is out of scope. |
| A-B loop | Advanced controls | Shift+L, right-click Playback, macOS Playback menu | Same | Set A, set B, off, as VLC; it follows the input's time, as VLC's does. |
| Frame by frame | E, advanced controls | E, right-click Playback, macOS menu | Same | VLC's own frame-next: the first press pauses, as in VLC. |

## Audio menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Audio Track | Submenu, cycle (B) | Tracks panel › Audio, right-click Audio › Audio Tracks | Same | Includes VLC's Disable choice. B cycles the tracks, skipping Disable, as in VLC. |
| Audio Device | Submenu, live | Settings › Audio › Output device | Different | Windows only (MMDevice endpoints), Live. Not in a menu. macOS and Linux use the system default. |
| Stereo Mode | Stereo, reverse, left, right, mono, Dolby Surround | Settings › Audio › Stereo mode | Different | VLC's modes (Stereo, Reverse stereo, Left, Right, Dolby Surround, Headphones, Mono), Live. In Settings, not a menu. |
| Visualizations | Spectrometer, scope, VU meter, goom, projectM | None | Missing | Not built (phase 9c scope). |
| Increase / Decrease Volume | Ctrl+Up / Ctrl+Down, menu | Ctrl+Up / Ctrl+Down, right-click Audio, slider, scroll wheel | Same | By the volume step (Settings › Audio). Maximum volume 100 to 300% (default 200%). |
| Mute | M | M | Same | Bar, right-click menu, macOS menu. |

## Video menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Video Track | Submenu | None | Missing | Hidden on purpose (D2). |
| Fullscreen | F, double-click, Esc to leave | F11, double-click, button, menu, Esc to leave | Different | F11 in place of F. Double-click action is configurable. macOS: Ctrl+Cmd+F in the View menu. |
| Always Fit Window | Toggle | Zoom › Fit to window | Different | Video panel (live) and Settings › Video › Framing (Next open). |
| Set as Wallpaper | Windows, DirectX output | None | Missing | Skipped in phase 9b. |
| Zoom | 1:4, 1:2, 1:1, 2:1 | Same choices | Same | Playback panel › Video tab (live), default in Settings (Next open). |
| Aspect Ratio | VLC's list | VLC's list | Same | Choices read from the output; live; default in Settings. No custom ratios. |
| Crop | VLC's ratio list | VLC's ratio list | Same | Live in the Video tab; default in Settings. Pixel crop is in Tools below. |
| Deinterlace and mode | Live, 10 modes | Settings › Video › Deinterlacing | Different | Live. Automatic/On/Off and 5 methods (Blend, Bob, X, Yadif 2x, Film NTSC IVTC), the ones VLC's D3D11 deinterlacer implements. In Settings, not a menu. |
| Post processing | Levels 0 to 6 | None | Missing | Removed: VLC 3.0.24's postproc had no visible effect in testing. |
| Take Snapshot | Shift+S, saved file | Shift+S, macOS menu | Same | VLC writes the file and shows its name over the video. Folder and format in Settings › Video › Snapshots. |

## Subtitle menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Add Subtitle File | Live, while playing | Tracks panel › Subtitles › add file, or a drop | Same | Live while media plays: the track is added and selected. srt, ass, ssa, vtt, sub. Same for external audio. |
| Sub Track | Submenu, cycle (V) | Tracks panel › Subtitles, right-click Subtitle › Subtitle Tracks | Same | Includes Disable. VLC renders the subtitles, Conic composes them over the video. V cycles the tracks, Disable included, as in VLC. |

## Tools menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Audio equalizer | 10 bands, preamp, presets, 2-pass | Settings › Audio › Effects | Different | Live. 10 bands, preamp, VLC's 18 presets. No 2-pass. In Settings, not a Tools window. |
| Compressor | 7 parameters | On/off and its 7 parameters | Same | Live: RMS/peak, attack, release, threshold, ratio, knee radius, makeup gain (Settings › Audio). |
| Spatializer | Room size, width, wet, dry, damp | Spatial audio › Room and its 5 parameters | Same | Live: room size, width, wet, dry, damping. Also Headphone virtualization (VLC's spatialaudio binaural mode). |
| Stereo widener | Yes | None | Missing | Dropped: VLC 3.0.24's filter refuses the usual output chains. |
| Advanced audio (pitch shift) | Pitch in semitones | Settings › Audio › Pitch shift | Different | Live, -12 to +12. Karaoke (voice removal) also on this page. |
| Video essential (image adjust) | Hue, brightness, contrast, saturation, gamma, live | Settings › Video › Picture and Effects | Different | Live. Hue, brightness, contrast, saturation, gamma, sharpen, banding removal, film grain. Gamma needs hardware decoding off. In Settings, not a Tools window. |
| Video crop (pixels) | Top, bottom, left, right | Settings › Video › Framing › Crop (pixels) | Different | Live; replaces the crop ratio. In Settings, not a Tools window. |
| Video colors | Color extraction, threshold, invert, posterize, sepia, gradient | Settings › Video › Effects | Partial | Live: invert, sepia (intensity), posterize (levels), color extraction, color threshold (color). No gradient, no saturation or similarity thresholds. |
| Video geometry | Transform, rotate, zoom, wall, puzzle | Settings › Video › Effects › Rotate or flip | Partial | Live: 90, 180, 270, flips, transpose. No free angle, wall, puzzle or interactive zoom. |
| Video overlay | Logo, marquee | Settings › Video › Overlay | Different | Live: logo (PNG file, position, opacity) and marquee (text, position). Fewer options than VLC's tab. |
| AtmoLight | Yes | None | Missing | |
| Video advanced | Denoiser, anaglyph, mirror, blur, antiflicker, and others | Settings › Video › Effects | Partial | Live: noise reduction, anaglyph 3D, mirror, motion blur, anti-flicker. No clone, water, waves, psychedelic, motion detection or spatial blur. |
| Synchronization | Audio delay, subtitle delay, subtitle speed and duration | Tracks panel delay fields | Partial | Audio and subtitle delay, live, ±100 ms buttons, typed ms, reset, ±10 s range; VLC's J/K and G/H keys move them by 50 ms. Defaults in Settings (Next open). No subtitle speed or duration factor. |
| Media Information | Ctrl+I, general (editable metadata), metadata, codec, statistics | Ctrl+I, Playback panel › Information tab | Partial | Read-only metadata and codec streams, refresh, Copy diagnostics (redacted). No metadata editing, no statistics tab. |
| Codec Information | Ctrl+J | Information tab, codec streams | Different | Merged with Media Information. Ctrl+J opens the queue in Conic. |
| VLM Configuration | Yes | None | Out of scope | Broadcasting is a non-goal. |
| Program Guide (EPG) | Yes | None | Missing | No DVB or EPG input. |
| Messages | Ctrl+M, verbosity, filter, save, modules tree | Ctrl+Shift+M, separate window | Same | Verbosity, filter, clear, Save. No modules tree tab. Also from right-click More Settings, Settings › Advanced, macOS Window menu. |
| Plugins and extensions | Yes | None | Out of scope | Extensions are out of scope. |
| Customize Interface | Toolbar editor | None | Out of scope | Customize player deferred (D4). |
| Preferences (simple) | Ctrl+P | Ctrl+, Settings dialog | Different | 12 pages with search and Live/Next open/Restart tags. See Preferences below. |
| Preferences (all, advanced tree) | Yes | None | Out of scope | Expert variable tree is a non-goal. |

## View menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Playlist | Ctrl+L, separate or docked | Ctrl+J queue panel | Different | A side drawer over the player. See Playlist and queue. |
| Docked playlist | Toggle | Always docked (drawer) | Different | |
| Status bar | Toggle | None | Missing | |
| Always on top | Toggle | More menu toggle, Settings › Interface | Same | Remembered for the next start. |
| Minimal interface | Ctrl+H | Hide UI (Ctrl+H), right-click menu, macOS View menu | Same | The bars overlay the video and hide while it plays. The design lists Ctrl+\; Conic uses VLC's key. |
| Fullscreen interface (controls in fullscreen) | Toggle | Settings › Interface › Show controls in fullscreen | Same | Plus auto-hide on/off and timeout. |
| Advanced controls | Record, snapshot, A-B, frame step | Next Frame, Jump to Time, A-B loop, snapshot in menus and keys | Different | No record button: recording is out of scope. |
| Media library / playlist sidebar | Toggle | Home screen, Library panel (Ctrl+Shift+L) | Different | History only. See Library. |
| Extensions view | Yes | None | Out of scope | |

## Help menu

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Help | F1 | F1, right-click More Settings, macOS Help menu | Different | Opens Conic's README online; Settings rows also have help tooltips. |
| Check for updates | Yes | None | Out of scope | Update checks are out of scope. |
| About | Yes | Shift+F1, More menu, right-click More Settings, macOS Help menu | Same | Version, VLC engine version, credits and license. |

## Preferences

Conic's Settings has 12 pages: General, Interface, Privacy, Controls
(App); Playback, Video, Audio, Subtitles, Library (Media); Network,
Performance, Advanced (System). The rows below follow VLC 3's simple
preferences categories.

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Interface language | Yes | None | Out of scope | English only. |
| Look and feel, skins | Native or skins | Theme, accent, density | Different | Skins are out of scope. Conic has Dark, Light, System themes and 9 accent colors (General, Interface). |
| Resize interface to video size | Yes | Interface › Resize window to fit video | Same | Next open. |
| Pause when minimized | Yes | Interface › Pause video when window is minimized | Same | Live. |
| Show controls in fullscreen | Yes | Interface › Show controls in fullscreen | Same | Live. |
| Remember window position and size | Yes | Interface › Remember window size and position | Same | Restart. |
| Show media title in window title | Yes | Interface › Show the file name in the title bar | Same | Live. |
| Allow only one instance | Yes | Interface › Allow only one instance | Same | Restart. A second start hands its media to the open Conic, which comes forward. |
| Systray, notifications | Yes | None | Out of scope | |
| Save recently played items | Yes | Privacy › Remember recent media | Same | Off clears the history at once. |
| Continue playback | Ask, always, never | Playback › Continue where you left off | Same | Next open. Also resume thresholds and Remember resume history (Privacy). |
| Use the media library | Yes | None | Out of scope | |
| Enable audio | Yes | Audio › Enable audio | Same | Next open. |
| Default / startup volume | Yes | Audio › Startup volume (or remember last) | Same | Restart. |
| Audio output module | Yes | Audio › Output module | Partial | Windows: Default, MMDevice, DirectSound, WaveOut. macOS and Linux: automatic only. Next open. |
| S/PDIF passthrough | Yes | Audio › Pass compressed audio to the receiver | Same | Next open. Not checked with a real receiver. |
| Force Dolby Surround detection | Yes | Audio › Detect Dolby Surround | Same | Next open. |
| Volume normalizer | Yes | Audio › Normalize loudness, Target level | Same | Live. |
| Replay gain | Yes | Audio › Replay gain, preamp | Same | Live. |
| Time-stretching audio | Yes | Audio › Keep the pitch when the speed changes | Same | On by default, as in VLC. Next open. |
| Preferred audio language | Yes | Playback and Audio › Preferred audio language | Same | Next open. |
| Visualization | Yes | None | Missing | |
| Last.fm | Yes | None | Out of scope | |
| Enable video | Yes | Video › Enable video | Same | Next open. |
| Fullscreen on start | Yes | Video › Start videos in fullscreen | Same | Next open. |
| Window decorations | Yes | None | Missing | Conic draws its own title bar. |
| Video output module | Yes | None (fixed) | Different | Conic's own output: D3D11 on Windows, IOSurface on macOS. |
| Disable screensaver | Yes | Video › Stop the screensaver during playback | Partial | Windows only. Live. |
| Default deinterlace and mode | Yes | Video › Deinterlacing | Same | Next open. Fewer methods (see Video menu). |
| Force aspect ratio | Yes | Video › Framing › Aspect ratio, Crop, Zoom | Same | Next open. |
| Snapshot directory, format | Yes | Video › Snapshots › Folder, Format | Same | PNG, JPEG or TIFF. Next open. |
| On-screen display, title on start | Yes | Subtitles › On-Screen Messages | Same | Show file name when a video starts (Next open, 1 to 10 s). Shortcut feedback is Conic's own overlay (Live). |
| Enable subtitles | Yes | Subtitles › Enable subtitles | Same | Next open. |
| Preferred subtitle language | Yes | Playback and Subtitles › Preferred language | Same | Next open. |
| Default subtitle encoding | Yes | Subtitles › Text encoding | Same | Automatic plus 21 encodings. Next open. |
| Autodetect subtitle files, fuzziness | Yes | Subtitles › Loading Subtitles | Same | Next open. |
| Font, size, color, outline, shadow, background | Yes | Subtitles › Appearance | Same | Size is relative (50 to 300%). Size, color, outline thickness and background are Live; the rest Next open. |
| Subtitle position | Yes | Subtitles › Distance from the bottom | Same | Next open. |
| Hardware-accelerated decoding | Automatic, DXVA2, D3D11, disable | Performance › Automatic, Direct3D 11, Off | Partial | No DXVA2 (its surfaces cannot reach the D3D11 output without a copy). macOS: Automatic or Off. Next open. |
| Skip H.264 loop filter, threads | Yes | Performance | Same | Next open. |
| Drop late frames | Yes | Performance › Drop frames when the system can't keep up | Same | Next open. |
| Fast seek | Yes | Playback › Seek precisely | Same | Next open. |
| Repair AVI files | Ask, always, never, fix when needed | Playback › Repair damaged AVI files | Partial | No Ask (no dialog provider). Next open. |
| Network caching policy | Yes | Network › Buffering presets or Custom | Same | Next open. |
| HTTP proxy | Yes | Network › HTTP proxy, password | Partial | Windows only (VLC 3 reads it only there). Password stored with DPAPI. |
| Record directory, default optical device | Yes | None | Out of scope | No recording, no discs. |
| Post-processing quality | Yes | None | Missing | See Video menu. |
| Reset preferences | Yes | Per page reset, Reset all | Same | |
| Show all (advanced) | Yes | None | Out of scope | |

## Playlist and queue

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Playlist view | List or tree, columns | Queue panel (Ctrl+J), titles only | Different | No durations or metadata columns. |
| Play an item | Double-click | Click | Same | |
| Reorder | Drag and drop | Move earlier / later buttons | Different | The drag handle is visual only. |
| Remove, clear | Yes | Remove per item, Clear queue | Same | |
| Search / filter | Yes | Queue panel search field | Same | Shown with more than one item; matches the names. |
| Sort | By title, artist, duration, and more | Queue panel › Sort by name | Partial | Natural name order, then reversed; no other keys (artist, duration). |
| Repeat one / all | Yes | Repeat cycle in queue panel and right-click menu | Same | Remembered across runs (Settings › Playback › Order). |
| Random | Yes | Shuffle | Same | Remembered. |
| Play and stop | Option | Settings › Playback › Stop after each item | Same | Next open. |
| Enqueue instead of play | Option, Enqueue action | Settings › Library › Add to the queue instead of replacing it; Home "Add to Queue" | Same | Next open. |
| Item information | Per item | Current media only | Partial | |
| Tree / nested playlists | Yes | Flat only | Missing | |
| Size limit | None | 2,000 items per expansion or playlist file | Different | |

## Library

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Media library (videos, music, scanning) | Yes | None | Out of scope | Decision D1: history only, no scanning. |
| Services discovery (UPnP, SAP, local network, podcasts) | Yes | None | Out of scope | |
| Cover art and metadata fetching | Yes | None | Out of scope | |
| Recent media | Playlist sidebar | Home › Recent, Library panel | Different | See Conic only for Home. |
| My Videos / My Music / My Pictures folders | Sidebar | None | Missing | Open Folder instead. |

## Hotkeys and mouse

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Hotkeys editor | Preferences › Hotkeys, search | Settings › Controls › Keyboard Shortcuts | Same | Search, per-row save and reset, Reset All, conflict checks. Live. |
| Number of bindable actions | About 90 actions | About 70 bindable commands | Same | The editor lists the commands a key can run; the ones that need a value (seek to, select a track) are left out. |
| Global hotkeys | Yes | None | Missing | |
| Default keys | VLC's set | Mostly VLC's | Different | Same: Space, S, P, N, M, E, B, V, G, H, J, K, X, [, ], =, Shift+L, Shift+P, Shift+N, Shift+S, Ctrl+Up, Ctrl+Down, Ctrl+H, Ctrl+O, Ctrl+V, Ctrl+T, Ctrl+I, F1, Esc, skip keys. Different: F11 (fullscreen), Ctrl+L (network), Ctrl+J (queue), Ctrl+, (settings), Ctrl+Shift+M (messages); no + and -. Cmd on macOS. |
| Volume, speed, delay, track cycle, chapter keys | Yes | VLC's keys | Same | Ctrl+Up/Down, [ ] =, J/K, G/H, B, V, Shift+P/N; each can be changed in the editor and shows its feedback. |
| Mouse wheel action | Volume or position, vertical and horizontal | Settings › Controls › Mouse | Same | Live; reverse direction option. |
| Double-click | Fullscreen | Configurable (fullscreen default) | Same | Live. |
| Mouse gestures interface | Yes | None | Missing | |

## Command line and OS integration

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Open a file from the command line | `vlc file` | `Conic file` (several files, URLs) | Same | Arguments that are not options open, several as a queue. Options (`--…`) are ignored. |
| VLC options on the command line | Yes | None | Missing | Options come from Settings only. |
| File associations, "Open with" | Installer, macOS document types | "Open with" by choosing Conic; macOS open-file events | Partial | Windows: Open with by choosing Conic.exe; the portable archive registers no association. macOS: the bundle declares movie and audio documents (as an alternate handler), so Open With and dropping on the Dock icon reach Conic; not the default player. |
| Single instance, enqueue from file manager | Option | Interface › Allow only one instance | Same | Media opened from the file manager or the command line goes to the open Conic; with Library › Add to the queue it joins the queue. |
| Tray icon | Yes | None | Out of scope | |
| System notifications | Yes | None | Out of scope | |
| Taskbar thumbnail buttons and progress (Windows) | Yes | None | Missing | |
| Media keys | Global | Play/Pause, Stop, Previous, Next, window focus only | Partial | Work while the window has the focus (unless a shortcut uses the key); no global capture. |
| OS media session (MPRIS on Linux) | Yes | None | Missing | |
| Drag and drop files and folders | Yes | Yes | Same | Several dropped files become the queue (or join it with Add to the queue); folders follow Settings › Library. |
| Drop a subtitle file onto the video | Adds the subtitle | Added to the playing media | Same | Live, as Add Subtitle File. With nothing playing, the file opens as media. |
| Drop a URL or text | Yes | Yes | Same | A dragged link, or text with one URL or path per line; several open as a queue. |
| macOS menu bar | Cocoa interface (not Qt) | File, Playback, View, Window, Help | Different | Help has About Conic. |

## Network and streaming

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| HTTP / HTTPS streams | Yes | Yes | Same | Includes HLS and DASH over http(s) as far as the engine handles them. |
| RTSP, RTP, UDP, MMS | Yes | Yes | Same | Passed to VLC as typed, including udp://@:port. UDP and RTP play in --protocols-smoke; RTSP and MMS were not checked (no local server). |
| FTP, SMB, SFTP | Yes | Yes | Same | Passed to VLC as typed, without credentials in the URL. FTP and SMB play in --protocols-smoke; SFTP was not checked (no local server). |
| Credentials in the URL | Yes | None | Missing | Rejected on purpose. |
| Streaming output, transcoding | Yes | None | Out of scope | |
| Web, telnet, HTTP remote interfaces | Yes | None | Out of scope | |
| Chromecast renderer | Yes | None | Out of scope | |
| Subtitle download (VLsub) | Extension | None | Out of scope | Extensions are out of scope. |
| Lua extensions and playlist scripts | Yes | None (extensions) | Out of scope | Not verified whether VLC's Lua playlist parsers run for http(s) URLs in Conic's core. |
| Network caching | Yes | Network › Buffering | Same | |
| TLS options | Advanced preferences | Network › Security | Different | Verify certificates and cipher preference. Hidden on macOS (VLC uses securetransport there). |
| Disc playback with menus | DVD, Blu-ray, audio CD | None | Out of scope | |
| Capture devices | DirectShow, V4L2, screen | None | Out of scope | |
| 360 video navigation | Mouse drag on supported outputs | None | Missing | Conic's output has no viewpoint control. Not verified how 360 media looks. |
| Teletext | Yes | None | Missing | |

## Conic only

| Feature | VLC 3 | Conic | Status | Notes |
|---|---|---|---|---|
| Home screen | No | Continue watching, Recent, Favorites sections | Conic only | Alt+Home. Search, grid or list, thumbnail cards with saved progress. |
| Favorites | No | Add from the player or Home; Library panel | Conic only | User-curated, 50 entries. |
| Saved positions list | Resume bar only, no list | Continue watching list with progress, per-item remove | Conic only | The resume prompt itself (Enter / Esc) matches VLC's Continue playback. Thresholds in Settings › Playback; the Privacy toggle clears at once. |
| Library panel | No | Ctrl+Shift+L, Recent / Favorites / Resume tabs, Clear library | Conic only | |
| Thumbnails | No | From the OS (Windows shell, Quick Look, freedesktop) | Conic only | |
| Mini player | No | Floating card on Home while media plays | Conic only | Thumbnail, progress, title, folder, previous / play-pause / next / stop, return to player. Not a second video surface. |
| Picture in picture | No | Ctrl+Shift+P, compact topmost window | Conic only | The main window shrinks; Esc or double-click returns. |
| Apply timing tags | No | Live / Next open / Restart on each Settings row | Conic only | |
| Settings search | No | Search across pages | Conic only | |
| Light theme, accent colors, density, reduce motion | No | Settings › General and Interface | Conic only | Dark, Light, System; 9 accents; Comfortable or Compact. |
| Single-click action on the video | No | Settings › Controls › Mouse | Conic only | Nothing, play/pause or fullscreen. |
| When playback finishes: Go to Home | No | Settings › Playback | Conic only | |
| Copy diagnostics | No | Information tab | Conic only | URL query and user info redacted. |

# VLC 3 and Conic, side by side

Native window captures taken October 14, 2026 on one Windows display (Intel
Iris Xe). Before: the installed VLC 3.0.24 with its default Qt
interface. After: Conic built from `feature/vlc3-parity`, on the same VLC
3.0.24 engine. Main windows are 1280×800; VLC's dialogs and menus are shown
at their own size.

| Workflow | VLC 3.0.24 | Conic |
|---|---|---|
| Start screen | ![VLC 3 empty player](images/vlc-home.png) | ![Conic Home, empty](images/conic-start.png) |
| Playback with controls | ![VLC 3 playback](images/vlc-playback.png) | ![Conic playback](images/conic-playback.png) |
| Playlist / queue | ![VLC 3 playlist window](images/vlc-playlist.png) | ![Conic queue panel](../images/queue.png) |
| Right-click menu | ![VLC 3 context menu](images/vlc-menu.png) | ![Conic context menu, Playback submenu](../images/context-menu.png) |
| Preferences | ![VLC 3 Simple Preferences](images/vlc-preferences.png) | ![Conic Settings, Audio](images/conic-preferences.png) |
| Equalizer | ![VLC 3 Audio Effects, Equalizer](images/vlc-effects.png) | ![Conic Settings, Audio, equalizer](../images/settings-equalizer.png) |
| Picture adjustments | ![VLC 3 Video Effects, Essential](images/vlc-video-effects.png) | ![Conic Settings, Video, Picture](../images/settings-video.png) |

Both players show the same generated H.264 test pattern at different
timestamps, so the frames illustrate layout, not color or performance. The
Conic captures contain real GPU video: the frame is composed by Avalonia, under
the controls, in the same window. VLC's floating playlist window is a setting of
this machine's VLC; VLC can also dock it in the main window.

Equivalents, not one-to-one copies: VLC's Effects and Filters dialog becomes
rows of Settings > Audio and Settings > Video in Conic, and Conic's context
menu is the redesign's, with VLC 3's entries.

## How the captures were taken

`eng/capture-window.ps1 -ProcessId <id> -OutputFile <png>` sizes a process's
main window and captures its native pixels (PrintWindow, DPI aware). VLC's
dialogs and Conic's panels were opened with clicks posted to each app's own
window, so nothing took the foreground. Conic's context menu comes from
`--menu-review=context-playback`. Inspect each PNG before using it as video
evidence: a successful capture call alone doesn't prove the video frame was
captured.

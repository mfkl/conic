# Conic for Windows

This preview runs on Windows x64. Extract the whole ZIP to a folder, then open
`Conic.exe`. Keep the adjacent `vlc` folder intact. The portable package
includes .NET and VLC; no SDK or separate VLC installation is needed.

- Open a file with **Ctrl+O**, a folder with **Ctrl+Shift+O**, or an HTTP(S) URL
  with **Ctrl+L**. Local files can also be dropped into the player.
- **Space** plays/pauses; **Alt+Left/Right** skips; **M** mutes; **F11** toggles
  fullscreen. The timeline and volume slider also accept keyboard input.
- **Ctrl+J** opens the queue. Home shows recent media, favorites and resume
  positions; **Alt+Home** returns there while playback continues.
- **Ctrl+Shift+P** enters picture in picture. **Escape** restores the window.
  Closing the PiP window exits the application.
- **Ctrl+,** opens Settings. Changes save automatically. Settings/history live
  under `%LOCALAPPDATA%\Conic`, separately from the extracted application.

Track selection and delay controls exist, but rendered subtitles are not yet
qualified on the custom video path. External track attachments currently apply
on the next open. This preview does not promise full VLC feature parity.

To update, close the app and extract the new ZIP to a new folder. Settings and
history are retained. Settings lets you clear recent/resume history; the Library
can clear all saved library entries.

If the app reports missing playback files, extract the complete ZIP again.
Developer `VLC_DIRECTORY` overrides take precedence over the bundled runtime;
remove an obsolete override if one was configured. For playback failures, try
another local file and use Media information (**Ctrl+I**) to copy diagnostics.

These are local review builds pending the source/distribution work described
in `DEPENDENCIES.md`. Public release downloads will be listed in the project
README when available.

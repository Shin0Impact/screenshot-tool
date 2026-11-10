# Screenshot

Tiny Windows tray app for native-quality screenshots on mixed-resolution monitors.
One C# file, no dependencies, no installer, no sound.

## Build and run

Double-click `build.bat`, or grab `Screenshot.exe` from the **latest** release (built by GitHub Actions).
A neutral icon appears in the tray (white on a dark taskbar, dark on a light one). The capture bar follows the Windows light/dark setting.

## Hotkeys

| Key | Action |
|-----|--------|
| `PrtScn` or `Win+Shift+S` | opens the **capture bar** (below) in the mode you used last |
| `Alt+PrtScn` | capture bar in window mode |
| `Ctrl+PrtScn` | open the capture bar in Monitor mode: hover a monitor, click to capture it (with *Follow mouse* on: captures the monitor under the mouse instantly) |
| `Shift+PrtScn` | instantly capture all monitors side by side, scaled to the same height (no black gap) |
| `Win+PrtScn` | untouched, normal Windows behaviour |

While the app runs it takes over `PrtScn` and `Win+Shift+S`, so Windows' Snipping Tool no longer opens.

## Capture bar

The screen freezes and a bar appears at the top, like Snipping Tool.

- **Region**: drag a rectangle. A plain click (no drag) takes the whole monitor under the mouse.
- **Window**: hover a window to highlight it, click to capture it (works on any monitor, focused or not).
- **Monitor**: hover a monitor to highlight it, click to capture it (pick the left or right one, regardless of where you started). **All monitors**: captures immediately.
- Clicking outside an existing selection only deselects it; it no longer captures the whole monitor.
- **Edit** (off by default): after you draw a region it stays adjustable (handles, arrow keys, `Enter`), and after capturing an editor opens with pen, arrow, box, highlight and text, six colours, four sizes and undo. `Enter` keeps the result, `Esc` discards it.
- **Follow mouse** (off by default, tray menu only): Monitor and Window capture (the bar buttons, `Ctrl+PrtScn` and `Alt+PrtScn`) take the monitor or window under the mouse straight away instead of asking you to pick one.
- Pressing the shortcut again while the bar is open closes it and opens a fresh one.
- With **Edit** on, after drawing a region you can drag its handles, drag inside to move it,
  nudge with the arrow keys (Shift = 10 px, Ctrl+arrows = resize), then press **Enter**, double-click, or click *Capture*.
  With it off, the region is captured as soon as you release the mouse.
- **Save** / **Copy** (toggles): write a PNG file and/or put the shot on the clipboard. At least one stays on.
- `Esc` closes. Right-click clears the selection, or closes if there is none.

All toggles are remembered. They are also in the tray menu, together with the **click tolerance**
(how many pixels the mouse may move during a click and still count as a click; default 12 px).

## Where files go

`Pictures\Screenshots\Screenshot_<date>_<time>.png`

With *Copy* on, the clipboard gets the image **and** the file, so pasting into a chat or Explorer sends
the original PNG instead of a re-encoded copy.

## Why quality holds up

The app is per-monitor DPI aware (see `app.manifest`), so every capture is a 1:1 copy of the
screen's real pixels on each monitor. Nothing is resized, except when you ask for
*All monitors*, where the shorter monitor is scaled up to match the taller one.

## Limits

- Games in **exclusive fullscreen** can come out black. Use borderless windowed.
- If the foreground app runs **as administrator**, Windows hides the key from a normal app.
  Run `Screenshot.exe` as administrator too.
- Some anti-cheat systems dislike any keyboard hook. Quit the app before playing those.

`screenshot-tool.log` next to the exe records start-up and capture timings, useful for debugging.

## Hotkeys over games that run as administrator

Windows does not let a normal program see keys typed into an elevated window (a game started as
administrator, Task Manager, ...), so ordinary hotkeys do nothing there and Windows' own Snipping
Tool opens instead. A "UIAccess" program is allowed to, without being elevated itself, but Windows
only starts one that is signed with a trusted certificate and installed in Program Files.

`Screenshot.exe` takes care of that by itself:

1. Double-click it. The first time it asks to do a one-time setup and Windows shows **one**
   permission prompt.
2. It creates a local self-signed certificate (trusted on this PC, only used for this exe), installs
   a signed copy of the app to `C:\Program Files\ScreenshotTool`, and sets it to start at logon, not
   elevated.
3. From then on, double-clicking `Screenshot.exe` just starts the installed copy. A newer
   `Screenshot.exe` updates it the same way (one prompt again).
4. `Screenshot.exe --uninstall` removes the app, the startup task and the certificate.

Choosing *No* at the first prompt runs the app without the setup: everything works except over
administrator windows. The installed app's log is in `%LOCALAPPDATA%\ScreenshotTool` (the Program
Files folder is read-only); the setup's own output is in `%TEMP%\screenshot-setup.log`.
`build.bat` makes a plain build with no setup step, for development.

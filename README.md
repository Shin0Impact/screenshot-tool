# Screenshot

A small Windows tray app for screenshots at full resolution on monitors with different sizes.
C#, no dependencies, no sound.

## Build

Run `build.bat` (uses the C# compiler that comes with Windows), or take `Screenshot-Installer.exe`
from the latest release, which GitHub Actions builds from every push to `main`.
The source is in `src/`.

## Shortcuts

| Key | Action |
|-----|--------|
| `PrtScn` or `Win+Shift+S` | capture bar, in the mode you used last |
| `Alt+PrtScn` | capture bar in window mode |
| `Ctrl+PrtScn` | capture bar in monitor mode (with *Follow mouse* on, captures the monitor under the mouse right away) |
| `Shift+PrtScn` | all monitors side by side, scaled to the same height |
| `Win+PrtScn` | left to Windows |

While it runs, the app takes over `PrtScn` and `Win+Shift+S`, so Snipping Tool does not open.
Pressing a shortcut while the bar is open closes it and opens a new one.

## Capture bar

The screen freezes and a bar slides in at the top.

- **Region**: drag a rectangle. A click without dragging takes the monitor under the mouse.
- **Window**: hover to highlight a window, click to capture it.
- **Monitor**: hover to highlight a monitor, click to capture it.
- **All monitors**: captures right away.
- **Edit** (off by default): the region stays adjustable after you draw it (handles, arrow keys, `Enter`),
  and after the capture an editor opens with pen, arrow, box, highlight and text.
  `Enter` keeps the result, `Esc` discards it.
- **Follow mouse** (off by default, tray menu): monitor and window captures use whatever is under the
  mouse instead of asking you to pick.
- **Save** and **Copy**: write a PNG and/or copy the shot to the clipboard. One of them stays on.
- `Esc` closes. Right-click clears the selection, or closes the bar if there is none.

With Edit on, drag the handles to resize, drag inside to move, nudge with the arrow keys
(Shift = 10 px, Ctrl+arrows = resize), then press `Enter`, double-click, or click *Capture*.

Settings are remembered. They are also in the tray menu, along with the click tolerance
(how far the mouse can move during a click, default 12 px).

## Files

Shots go to `Pictures\Screenshots\Screenshot_<date>_<time>.png`. With Copy on, the clipboard gets
the image and the file, so pasting into a chat or Explorer sends the original PNG.

The app is per-monitor DPI aware (`app.manifest`), so each capture is a 1:1 copy of the screen's
pixels. Nothing is resized except in *All monitors*, where shorter monitors are scaled up.

## Limits

- Exclusive-fullscreen games can come out black. Use borderless windowed.
- Windows hides keys typed into an administrator window from a normal app. Use the administrator
  install below, or run `Screenshot.exe` as administrator.
- Some anti-cheat software dislikes keyboard hooks. Quit the app before those games.
- A debug log is kept in `%LOCALAPPDATA%\ScreenshotTool` for now. It only records shortcut keys.

## Installer

Windows only lets a UIAccess program see keys typed into administrator windows (a game started as
administrator, Task Manager). That needs a signed build in Program Files, which `Screenshot-Installer.exe` sets up.
Double-click it and pick one:

- **With administrator support** (default): one permission prompt, creates a local self-signed
  certificate (trusted on this PC, used only for this app), installs a signed copy into
  `C:\Program Files\ScreenshotTool` and starts it at logon, not elevated.
- **Without administrator support**: no prompt, installs the normal build into a folder you choose
  (default `%LOCALAPPDATA%\Programs\ScreenshotTool`). Hotkeys do not work over administrator windows.

Run the installer again to add or remove administrator support, update, start or uninstall.
You can also uninstall from Settings > Apps, the tray menu, or `Uninstall.exe` in the install folder.
Uninstalling removes the app, the startup entry and (for the administrator install) the certificate.

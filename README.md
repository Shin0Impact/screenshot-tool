# Screenshot

Tiny Windows tray app for native-quality screenshots on mixed-resolution monitors.
One C# file, no dependencies, no installer, no sound.

## Build and run

Double-click `build.bat`. It compiles `Screenshot.cs` with the C# compiler built into
Windows and starts `Screenshot.exe`. A blue icon appears in the tray.

## Hotkeys

| Key           | Action                                   |
|---------------|------------------------------------------|
| `PrtScn` or `Win+Shift+S` | drag a region (Esc / right-click cancels)|
| `Ctrl+PrtScn` | whole monitor the mouse is on            |
| `Alt+PrtScn`  | active window                            |
| `Win+PrtScn`  | untouched, normal Windows behaviour      |

While the app runs it takes over `PrtScn` and `Win+Shift+S`, so Windows' Snipping Tool no longer opens.
Right-click the tray icon to open the folder, or to start with Windows.

## Where files go

`Pictures\Screenshots\Screenshot_<date>_<time>.png`

The clipboard gets the image **and** the file, so pasting into a chat or Explorer sends
the original PNG instead of a re-encoded copy.

## Why quality holds up

The app is per-monitor DPI aware (see `app.manifest`), so every capture is a 1:1 copy of the
screen's real pixels on each monitor. Nothing is resized.

## Limits

- Games in **exclusive fullscreen** can come out black. Use borderless windowed.
- If the game is running **as administrator**, Windows hides the key from a normal app.
  Run `Screenshot.exe` as administrator too, or use `Win+PrtScn` in that game.
- Some anti-cheat systems dislike any keyboard hook. Quit the app before playing those.

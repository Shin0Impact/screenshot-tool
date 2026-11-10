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
| `Ctrl+PrtScn` | instantly capture the monitor the mouse is on |
| `Shift+PrtScn` | instantly capture all monitors side by side, scaled to the same height (no black gap) |
| `Win+PrtScn` | untouched, normal Windows behaviour |

While the app runs it takes over `PrtScn` and `Win+Shift+S`, so Windows' Snipping Tool no longer opens.

## Capture bar

The screen freezes and a bar appears at the top, like Snipping Tool.

- **Region**: drag a rectangle. A plain click (no drag) takes the whole monitor under the mouse.
- **Window**: hover a window to highlight it, click to capture it (works on any monitor, focused or not).
- **Monitor**: hover a monitor to highlight it, click to capture it (pick the left or right one, regardless of where you started). **All monitors**: captures immediately.
- Clicking outside an existing selection only deselects it; it no longer captures the whole monitor.
- **Delay 3s / 5s / 10s**: closes the bar, counts down, then freezes the screen again, so you can open a menu or tooltip first.
- **Adjust** (toggle): after drawing a region you can drag its handles, drag inside to move it,
  nudge with the arrow keys (Shift = 10 px, Ctrl+arrows = resize), then press **Enter**, double-click, or click *Capture*.
  Turn it off to capture as soon as you release the mouse.
- **Save** / **Copy** (toggles): write a PNG file and/or put the shot on the clipboard. At least one stays on.
- `Esc` or **X** closes. Right-click clears the selection, or closes if there is none.

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

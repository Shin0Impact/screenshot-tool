// Screenshot - tiny native-pixel screenshot tool for Windows.
// No dependencies: compiles with the C# compiler that ships with Windows (see build.bat).
//
// Hotkeys (PrtScn is taken over from Windows' Snipping Tool while this runs):
//   PrtScn        capture bar: region / window / monitor / all monitors, delay, toggles
//                 (Win+Shift+S does the same)
//   Ctrl+PrtScn   whole monitor the mouse is on
//   Alt+PrtScn    pick a window (hover to highlight, click to capture)
//   Shift+PrtScn  all monitors side by side, scaled to the same height
//   Win+PrtScn    left alone (normal Windows behaviour)
//
// Every capture is a straight copy of the screen's real pixels (per-monitor DPI aware),
// saved as PNG, and put on the clipboard as both an image and a file.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenshotTool
{
    static class Native
    {
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_DPICHANGED = 0x02E0;
        public const int VK_SNAPSHOT = 0x2C;
        public const int VK_S = 0x53;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public const uint SRCCOPY = 0x00CC0020;
        public const uint CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        public const int DWMWA_CLOAKED = 14;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        public static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public static Rectangle ToRectangle(RECT r)
        {
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    // Persistent settings (HKCU\Software\ScreenshotTool).
    static class Settings
    {
        private const string KeyPath = @"Software\ScreenshotTool";

        public static bool SaveFile = true;        // write a PNG file
        public static bool CopyClipboard = true;   // put the shot on the clipboard
        public static bool Adjust = true;          // let the selection be adjusted before capturing
        public static int LastMode;                // last overlay mode: 0 = region, 1 = window, 2 = monitor
        public static int ClickTolerance = 12;     // px the mouse may move and still count as a click

        public static void Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                {
                    if (k == null) return;
                    SaveFile = GetBool(k, "SaveFile", true);
                    CopyClipboard = GetBool(k, "CopyClipboard", true);
                    Adjust = GetBool(k, "Adjust", true);
                    object lm = k.GetValue("LastMode");
                    if (lm is int && (int)lm >= 0 && (int)lm <= 2) LastMode = (int)lm;
                    else if (GetBool(k, "WindowModeLast", false)) LastMode = 1;
                    object v = k.GetValue("ClickTolerance");
                    if (v is int && (int)v >= 0 && (int)v <= 200) ClickTolerance = (int)v;
                    if (!SaveFile && !CopyClipboard) CopyClipboard = true;
                }
            }
            catch (Exception) { }
        }

        private static bool GetBool(RegistryKey k, string name, bool fallback)
        {
            object v = k.GetValue(name);
            if (v is int) return ((int)v) != 0;
            return fallback;
        }

        public static void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("SaveFile", SaveFile ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("CopyClipboard", CopyClipboard ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Adjust", Adjust ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("LastMode", LastMode, RegistryValueKind.DWord);
                    k.SetValue("ClickTolerance", ClickTolerance, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }

        // At least one output (file or clipboard) always stays on.
        public static void ToggleSave()
        {
            SaveFile = !SaveFile;
            if (!SaveFile && !CopyClipboard) CopyClipboard = true;
            Save();
        }

        public static void ToggleCopy()
        {
            CopyClipboard = !CopyClipboard;
            if (!CopyClipboard && !SaveFile) SaveFile = true;
            Save();
        }
    }

    // Neutral colours that follow the Windows light/dark setting.
    static class Theme
    {
        public static bool Light;
        public static Color Bar, BarBorder, Text, Muted, OnFill, OnText, Hover, Accent, AccentText;

        private static bool ReadFlag(string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (k != null)
                    {
                        object v = k.GetValue(name);
                        if (v is int) return ((int)v) != 0;
                    }
                }
            }
            catch (Exception) { }
            return false;   // default: dark
        }

        public static bool TaskbarLight { get { return ReadFlag("SystemUsesLightTheme"); } }

        public static void Refresh()
        {
            Light = ReadFlag("AppsUseLightTheme");
            if (Light)
            {
                Bar = Color.FromArgb(246, 243, 243, 243);
                BarBorder = Color.FromArgb(70, 0, 0, 0);
                Text = Color.FromArgb(255, 32, 32, 32);
                Muted = Color.FromArgb(255, 110, 110, 110);
                OnFill = Color.FromArgb(255, 40, 40, 40);
                OnText = Color.White;
                Hover = Color.FromArgb(40, 0, 0, 0);
                Accent = Color.FromArgb(255, 40, 40, 40);
                AccentText = Color.White;
            }
            else
            {
                Bar = Color.FromArgb(244, 32, 32, 32);
                BarBorder = Color.FromArgb(80, 255, 255, 255);
                Text = Color.White;
                Muted = Color.FromArgb(255, 160, 160, 160);
                OnFill = Color.FromArgb(255, 235, 235, 235);
                OnText = Color.FromArgb(255, 25, 25, 25);
                Hover = Color.FromArgb(55, 255, 255, 255);
                Accent = Color.FromArgb(255, 235, 235, 235);
                AccentText = Color.FromArgb(255, 25, 25, 25);
            }
        }
    }

    enum OverlayOutcome { Cancelled, Rect, AllMonitors, DelayRestart }

    class OverlayResult
    {
        public Bitmap Image;
        public bool Restart;
        public int RestartDelay;
        public int RestartMode;
    }

    class ToolbarButton
    {
        public string Text;
        public Rectangle Rect;
        public bool IsSeparator;
        public bool IsLabel;
        public Func<bool> IsOn;   // toggle / mode state (null = plain button)
        public Action Click;
    }

    // Full-screen overlay: a frozen 1:1 copy of the desktop (one image pixel = one screen pixel)
    // with a capture bar at the top, like Snipping Tool. Region mode: drag a rectangle, then adjust
    // it with handles / arrow keys and confirm. Window mode: hover a window and click.
    class SelectionForm : Form
    {
        private readonly Bitmap shot;
        private readonly Bitmap dimmed;
        private readonly Rectangle bounds;             // virtual screen, screen coordinates
        private readonly List<Rectangle> windows;      // screen coordinates, top-most first
        private readonly Rectangle monitorClient;      // monitor that carries the bar, client coordinates
        private readonly float scale;
        private readonly Font uiFont;
        private readonly Font labelFont;
        private readonly StringFormat textFormat = new StringFormat();
        private readonly List<ToolbarButton> buttons = new List<ToolbarButton>();
        private Rectangle toolbarRect;
        private ToolbarButton hoverButton;

        private int mode;            // 0 = region, 1 = pick a window, 2 = pick a monitor
        private bool windowMode { get { return mode != 0; } }   // hover-pick modes
        private readonly List<Rectangle> monitors;  // screen coordinates
        private bool clickedAway;    // this press only deselected an existing selection
        private bool dragging;       // drawing a new selection
        private bool exceeded;       // pointer left the click-tolerance zone during this drag
        private Point start;
        private Rectangle sel = Rectangle.Empty;   // region selection, or the hovered window
        private bool hasSelection;   // a finished region that can still be adjusted
        private int activeHandle = -1;             // 0-7 = resize handle, 8 = move, -1 = none
        private Rectangle dragOrig;
        private Point dragOrigPoint;
        private bool firstPaintLogged;

        public OverlayOutcome Outcome = OverlayOutcome.Cancelled;
        public Rectangle Result = Rectangle.Empty;   // bitmap pixels
        public int DelaySeconds;
        public int RestartMode;
        public string CloseReason = "";

        public SelectionForm(Bitmap desktopShot, Rectangle screenBounds, List<Rectangle> windowRects,
            List<Rectangle> monitorRects, Rectangle toolbarMonitorScreen, int startMode)
        {
            Theme.Refresh();
            monitors = monitorRects;
            shot = desktopShot;
            bounds = screenBounds;
            windows = windowRects;
            mode = startMode;
            monitorClient = new Rectangle(toolbarMonitorScreen.X - screenBounds.X,
                toolbarMonitorScreen.Y - screenBounds.Y, toolbarMonitorScreen.Width, toolbarMonitorScreen.Height);
            scale = MonitorScale(toolbarMonitorScreen);
            uiFont = new Font("Segoe UI", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            labelFont = new Font("Segoe UI", 14f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            textFormat.Alignment = StringAlignment.Center;
            textFormat.LineAlignment = StringAlignment.Center;
            textFormat.FormatFlags = StringFormatFlags.NoWrap;
            dimmed = MakeDimmed(desktopShot);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            Bounds = bounds;

            BuildToolbar();
        }

        private static float MonitorScale(Rectangle monitorScreen)
        {
            try
            {
                Native.POINT pt;
                pt.X = monitorScreen.X + monitorScreen.Width / 2;
                pt.Y = monitorScreen.Y + monitorScreen.Height / 2;
                IntPtr mon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch (Exception) { }
            return 1f;
        }

        // Darkened copy made once, so painting never needs a slow full-screen alpha blend.
        private static Bitmap MakeDimmed(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            Rectangle full = new Rectangle(0, 0, w, h);
            BitmapData sd = src.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData dd = dst.LockBits(full, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int[] row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride), row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        int px = row[x];
                        // ~62% brightness per channel
                        row[x] = ((px >> 1) & 0x007F7F7F) + ((px >> 3) & 0x001F1F1F);
                    }
                    Marshal.Copy(row, 0, new IntPtr(dd.Scan0.ToInt64() + (long)y * dd.Stride), w);
                }
            }
            finally
            {
                src.UnlockBits(sd);
                dst.UnlockBits(dd);
            }
            return dst;
        }

        // ---- capture bar ----------------------------------------------------------------

        private void AddButton(string text, Func<bool> isOn, Action click)
        {
            ToolbarButton b = new ToolbarButton();
            b.Text = text;
            b.IsOn = isOn;
            b.Click = click;
            buttons.Add(b);
        }

        private void AddLabel(string text)
        {
            ToolbarButton b = new ToolbarButton();
            b.Text = text;
            b.IsLabel = true;
            buttons.Add(b);
        }

        private void AddSeparator()
        {
            ToolbarButton b = new ToolbarButton();
            b.IsSeparator = true;
            buttons.Add(b);
        }

        private void BuildToolbar()
        {
            AddButton("Region", delegate { return mode == 0; }, delegate { SetMode(0); });
            AddButton("Window", delegate { return mode == 1; }, delegate { SetMode(1); });
            AddButton("Monitor", delegate { return mode == 2; }, delegate { SetMode(2); });
            AddButton("All monitors", null, delegate { Outcome = OverlayOutcome.AllMonitors; Close(); });
            AddSeparator();
            AddLabel("Delay");
            AddButton("3s", null, delegate { FinishDelay(3); });
            AddButton("5s", null, delegate { FinishDelay(5); });
            AddButton("10s", null, delegate { FinishDelay(10); });
            AddSeparator();
            AddButton("Adjust", delegate { return Settings.Adjust; },
                delegate { Settings.Adjust = !Settings.Adjust; Settings.Save(); });
            AddButton("Save", delegate { return Settings.SaveFile; }, delegate { Settings.ToggleSave(); });
            AddButton("Copy", delegate { return Settings.CopyClipboard; }, delegate { Settings.ToggleCopy(); });
            AddSeparator();
            AddButton("X", null, delegate { Cancel("X button"); });

            int pad = (int)(14 * scale);
            int btnH = (int)(36 * scale);
            int gap = (int)(4 * scale);
            int outer = (int)(8 * scale);
            int sepW = (int)(14 * scale);

            int[] widths = new int[buttons.Count];
            int total = outer * 2 - gap;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    ToolbarButton b = buttons[i];
                    if (b.IsSeparator)
                    {
                        widths[i] = sepW;
                    }
                    else
                    {
                        int tw = (int)Math.Ceiling(g.MeasureString(b.Text, uiFont).Width);
                        widths[i] = tw + (b.IsLabel ? pad : pad * 2);
                        if (!b.IsLabel && widths[i] < btnH) widths[i] = btnH;
                    }
                    total += widths[i] + gap;
                }
            }

            int tx = monitorClient.X + (monitorClient.Width - total) / 2;
            if (tx < monitorClient.X) tx = monitorClient.X;
            int ty = monitorClient.Y + (int)(16 * scale);
            toolbarRect = new Rectangle(tx, ty, total, btnH + outer * 2);

            int x = tx + outer;
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Rect = new Rectangle(x, ty + outer, widths[i], btnH);
                x += widths[i] + gap;
            }
        }

        private ToolbarButton HitButton(Point p)
        {
            if (!toolbarRect.Contains(p)) return null;
            foreach (ToolbarButton b in buttons)
            {
                if (b.Click != null && b.Rect.Contains(p)) return b;
            }
            return null;
        }

        private void InvalidateToolbar()
        {
            Invalidate(Rectangle.Inflate(toolbarRect, 3, 3));
        }

        // ---- finishing --------------------------------------------------------------------

        private void FinishRect(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
            if (r.Width < 1 || r.Height < 1) return;
            Outcome = OverlayOutcome.Rect;
            Result = r;
            Close();
        }

        private void FinishDelay(int seconds)
        {
            Outcome = OverlayOutcome.DelayRestart;
            DelaySeconds = seconds;
            RestartMode = mode;
            Close();
        }

        private void Confirm()
        {
            if (hasSelection) FinishRect(sel);
        }

        private void Cancel(string why)
        {
            CloseReason = why;
            Outcome = OverlayOutcome.Cancelled;
            Close();
        }

        private void SetMode(int value)
        {
            if (mode == value) return;
            mode = value;
            Settings.LastMode = value;
            Settings.Save();

            Rectangle old = sel;
            sel = Rectangle.Empty;
            hasSelection = false;
            dragging = false;
            activeHandle = -1;
            AdjustDirty(old, Rectangle.Empty);

            if (windowMode)
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                UpdateHover(new Point(pt.X - bounds.X, pt.Y - bounds.Y));
            }
            InvalidateToolbar();
        }

        // ---- window / form plumbing -------------------------------------------------------

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Force the exact physical-pixel rectangle, whatever DPI the window thinks it has.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_SHOWWINDOW);
            Native.SetForegroundWindow(Handle);
            Activate();
            Focus();

            if (windowMode)
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                UpdateHover(new Point(pt.X - bounds.X, pt.Y - bounds.Y));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Anything that did not go through Cancel()/Finish*() was closed from outside.
            if (CloseReason.Length == 0 && Outcome == OverlayOutcome.Cancelled)
                CloseReason = "external (" + e.CloseReason + ")";
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            // Ignore DPI-change resizing: the overlay must stay exactly the virtual-screen rectangle.
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            TrayApp.WriteLog("overlay key: " + keyData);
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;

            if (key == Keys.Escape)
            {
                Cancel("Esc");
                return true;
            }

            if (hasSelection && !windowMode)
            {
                if (key == Keys.Enter)
                {
                    Confirm();
                    return true;
                }
                int step = shift ? 10 : 1;
                int dx = 0, dy = 0;
                if (key == Keys.Left) dx = -step;
                else if (key == Keys.Right) dx = step;
                else if (key == Keys.Up) dy = -step;
                else if (key == Keys.Down) dy = step;
                if (dx != 0 || dy != 0)
                {
                    NudgeSelection(dx, dy, ctrl);   // Ctrl+arrows resize, arrows move
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void NudgeSelection(int dx, int dy, bool resize)
        {
            Rectangle old = sel;
            int W = shot.Width, H = shot.Height;
            if (resize)
            {
                int w = Math.Max(1, Math.Min(W - sel.X, sel.Width + dx));
                int h = Math.Max(1, Math.Min(H - sel.Y, sel.Height + dy));
                sel = new Rectangle(sel.X, sel.Y, w, h);
            }
            else
            {
                int x = Clamp(sel.X + dx, 0, W - sel.Width);
                int y = Clamp(sel.Y + dy, 0, H - sel.Height);
                sel = new Rectangle(x, y, sel.Width, sel.Height);
            }
            AdjustDirty(old, sel);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        // ---- window mode: highlight the top-most window under the cursor --------------------

        private void UpdateHover(Point p)
        {
            List<Rectangle> candidates = (mode == 2) ? monitors : windows;
            if (candidates == null || toolbarRect.Contains(p)) return;
            Rectangle found = Rectangle.Empty;
            int sx = p.X + bounds.X;
            int sy = p.Y + bounds.Y;
            foreach (Rectangle w in candidates)
            {
                if (w.Contains(sx, sy)) { found = w; break; }
            }
            if (!found.IsEmpty)
            {
                found.Offset(-bounds.X, -bounds.Y);
                found.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
            }
            if (found != sel)
            {
                Rectangle old = sel;
                sel = found;
                AdjustDirty(old, found);
            }
        }

        // ---- selection geometry -------------------------------------------------------------

        private Rectangle[] HandleRects()
        {
            int hs = Math.Max(8, (int)(9 * scale));
            int l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom;
            int cx = (l + r) / 2, cy = (t + b) / 2;
            int[] xs = new int[] { l, cx, r, r, r, cx, l, l };
            int[] ys = new int[] { t, t, t, cy, b, b, b, cy };
            Rectangle[] hr = new Rectangle[8];
            for (int i = 0; i < 8; i++)
            {
                hr[i] = new Rectangle(xs[i] - hs / 2, ys[i] - hs / 2, hs, hs);
            }
            return hr;
        }

        // 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L (corners are tested first)
        private int HitHandle(Point p)
        {
            Rectangle[] hr = HandleRects();
            int slop = Math.Max(3, (int)(4 * scale));
            int[] order = new int[] { 0, 2, 4, 6, 1, 3, 5, 7 };
            foreach (int i in order)
            {
                Rectangle r = hr[i];
                r.Inflate(slop, slop);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        private Rectangle ConfirmRect()
        {
            int w = (int)(150 * scale), h = (int)(34 * scale), margin = (int)(10 * scale);
            int x = sel.Right - w;
            int y = sel.Bottom + margin;
            if (y + h > shot.Height) y = sel.Top - h - margin;
            if (y < 0) y = sel.Bottom - h - margin;
            if (x + w > shot.Width) x = shot.Width - w;
            if (x < 0) x = 0;
            return new Rectangle(x, y, w, h);
        }

        private Rectangle ApplyDrag(Rectangle orig, int handle, int dx, int dy)
        {
            int W = shot.Width, H = shot.Height;
            int l = orig.Left, t = orig.Top, r = orig.Right, b = orig.Bottom;
            if (handle == 8)
            {
                dx = Clamp(dx, -l, W - r);
                dy = Clamp(dy, -t, H - b);
                l += dx; r += dx; t += dy; b += dy;
            }
            else
            {
                switch (handle)
                {
                    case 0: l += dx; t += dy; break;
                    case 1: t += dy; break;
                    case 2: r += dx; t += dy; break;
                    case 3: r += dx; break;
                    case 4: r += dx; b += dy; break;
                    case 5: b += dy; break;
                    case 6: l += dx; b += dy; break;
                    case 7: l += dx; break;
                }
                l = Clamp(l, 0, W); r = Clamp(r, 0, W);
                t = Clamp(t, 0, H); b = Clamp(b, 0, H);
            }
            if (l > r) { int tmp = l; l = r; r = tmp; }
            if (t > b) { int tmp = t; t = b; b = tmp; }
            if (r - l < 1) r = l + 1;
            if (b - t < 1) b = t + 1;
            return Rectangle.FromLTRB(l, t, r, b);
        }

        // Bounds of the monitor under a client-area point, in bitmap pixels.
        private Rectangle MonitorAt(Point clientPoint)
        {
            Rectangle full = new Rectangle(0, 0, shot.Width, shot.Height);
            Native.POINT pt;
            pt.X = clientPoint.X + bounds.X;
            pt.Y = clientPoint.Y + bounds.Y;
            IntPtr mon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            Native.MONITORINFO mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(mon, ref mi)) return full;
            Rectangle r = Native.ToRectangle(mi.rcMonitor);
            r.Offset(-bounds.X, -bounds.Y);
            r.Intersect(full);
            return r;
        }

        private static Rectangle MakeRect(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        // Repaint only the union of two selections (plus room for handles, label and button).
        private void AdjustDirty(Rectangle a, Rectangle b)
        {
            Rectangle d;
            if (a.IsEmpty) d = b;
            else if (b.IsEmpty) d = a;
            else d = Rectangle.Union(a, b);
            if (d.IsEmpty) return;
            int pad = (int)(170 * scale);
            d.Inflate(pad, pad);
            Invalidate(d);
        }

        // ---- mouse --------------------------------------------------------------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Point pt = e.Location;

            if (e.Button == MouseButtons.Right)
            {
                if (hasSelection)
                {
                    Rectangle old = sel;
                    hasSelection = false;
                    sel = Rectangle.Empty;
                    AdjustDirty(old, Rectangle.Empty);
                }
                else
                {
                    Cancel("right-click");
                }
                return;
            }
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseDown(e);
                return;
            }

            clickedAway = false;
            if (toolbarRect.Contains(pt))
            {
                ToolbarButton b = HitButton(pt);
                if (b != null)
                {
                    b.Click();
                    if (!IsDisposed) InvalidateToolbar();
                }
                return;
            }

            if (windowMode)
            {
                if (!sel.IsEmpty) FinishRect(sel);
                return;
            }

            if (hasSelection)
            {
                if (ConfirmRect().Contains(pt))
                {
                    Confirm();
                    return;
                }
                int h = HitHandle(pt);
                if (h >= 0)
                {
                    activeHandle = h;
                    dragOrig = sel;
                    dragOrigPoint = pt;
                    return;
                }
                if (sel.Contains(pt))
                {
                    activeHandle = 8;
                    dragOrig = sel;
                    dragOrigPoint = pt;
                    return;
                }
                // click elsewhere: start over with a fresh selection (a bare click only deselects)
                clickedAway = true;
                Rectangle prev = sel;
                hasSelection = false;
                sel = Rectangle.Empty;
                AdjustDirty(prev, Rectangle.Empty);
            }

            dragging = true;
            exceeded = false;
            start = pt;
            sel = Rectangle.Empty;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point pt = e.Location;

            ToolbarButton hb = HitButton(pt);
            if (hb != hoverButton)
            {
                hoverButton = hb;
                InvalidateToolbar();
            }

            if (activeHandle >= 0)
            {
                Rectangle old = sel;
                sel = ApplyDrag(dragOrig, activeHandle, pt.X - dragOrigPoint.X, pt.Y - dragOrigPoint.Y);
                AdjustDirty(old, sel);
            }
            else if (dragging)
            {
                // Inside the click-tolerance zone nothing is drawn: it still counts as a click.
                if (!exceeded && (Math.Abs(pt.X - start.X) > Settings.ClickTolerance
                    || Math.Abs(pt.Y - start.Y) > Settings.ClickTolerance))
                {
                    exceeded = true;
                }
                if (exceeded)
                {
                    Rectangle old = sel;
                    sel = MakeRect(start, pt);
                    AdjustDirty(old, sel);
                }
            }
            else if (windowMode)
            {
                UpdateHover(pt);
            }

            UpdateCursor(pt);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                base.OnMouseUp(e);
                return;
            }
            if (activeHandle >= 0)
            {
                activeHandle = -1;
                return;
            }
            if (dragging)
            {
                dragging = false;
                if (exceeded)
                {
                    Rectangle r = MakeRect(start, e.Location);
                    r.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                    if (r.Width < 2 || r.Height < 2)
                    {
                        Rectangle old = sel;
                        sel = Rectangle.Empty;
                        AdjustDirty(old, Rectangle.Empty);
                        return;
                    }
                    sel = r;
                    if (Settings.Adjust)
                    {
                        hasSelection = true;
                        AdjustDirty(sel, sel);
                    }
                    else
                    {
                        FinishRect(sel);
                    }
                }
                else if (clickedAway)
                {
                    TrayApp.WriteLog("click outside selection: deselected only");
                }
                else
                {
                    // A click (pointer stayed inside the tolerance zone) selects the whole monitor.
                    FinishRect(MonitorAt(e.Location));
                }
                clickedAway = false;
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (hasSelection && !windowMode && sel.Contains(e.Location) && !toolbarRect.Contains(e.Location))
            {
                Confirm();
            }
            base.OnMouseDoubleClick(e);
        }

        private void UpdateCursor(Point pt)
        {
            Cursor c = Cursors.Cross;
            if (toolbarRect.Contains(pt))
            {
                c = (HitButton(pt) != null) ? Cursors.Hand : Cursors.Default;
            }
            else if (!windowMode && hasSelection)
            {
                if (ConfirmRect().Contains(pt))
                {
                    c = Cursors.Hand;
                }
                else
                {
                    int h = HitHandle(pt);
                    if (h == 0 || h == 4) c = Cursors.SizeNWSE;
                    else if (h == 2 || h == 6) c = Cursors.SizeNESW;
                    else if (h == 1 || h == 5) c = Cursors.SizeNS;
                    else if (h == 3 || h == 7) c = Cursors.SizeWE;
                    else if (sel.Contains(pt)) c = Cursors.SizeAll;
                }
            }
            if (Cursor != c) Cursor = c;
        }

        // ---- painting -----------------------------------------------------------------------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // painted entirely in OnPaint
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!firstPaintLogged)
            {
                firstPaintLogged = true;
                TrayApp.WriteLog("overlay first paint");
            }

            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            // Explicit source/dest rectangles in pixels => no DPI based scaling of the bitmap.
            g.DrawImage(dimmed, clip, clip.X, clip.Y, clip.Width, clip.Height, GraphicsUnit.Pixel);

            if (sel.Width > 0 && sel.Height > 0)
            {
                Rectangle bright = Rectangle.Intersect(sel, clip);
                if (!bright.IsEmpty)
                {
                    g.DrawImage(shot, bright, bright.X, bright.Y, bright.Width, bright.Height, GraphicsUnit.Pixel);
                }

                float bw = windowMode ? Math.Max(3f, 3f * scale) : Math.Max(1f, scale);
                using (Pen p = new Pen(Color.White, bw))
                {
                    g.DrawRectangle(p, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
                }

                if (hasSelection && !windowMode) DrawHandles(g);
                DrawSizeLabel(g);
                if (hasSelection && !windowMode) DrawConfirm(g);
            }

            DrawToolbar(g, clip);
        }

        private void DrawHandles(Graphics g)
        {
            Rectangle[] hr = HandleRects();
            using (SolidBrush fill = new SolidBrush(Color.White))
            using (Pen border = new Pen(Color.FromArgb(255, 110, 110, 110), 1f))
            {
                foreach (Rectangle r in hr)
                {
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }
        }

        private void DrawSizeLabel(Graphics g)
        {
            string text = sel.Width.ToString() + " x " + sel.Height.ToString();
            SizeF ts = g.MeasureString(text, labelFont);
            float lx = sel.X;
            float ly = sel.Y - ts.Height - 4 * scale;
            if (ly < 0) ly = sel.Y + 4 * scale;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
            using (SolidBrush fg = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, lx, ly, ts.Width + 6, ts.Height + 2);
                g.DrawString(text, labelFont, fg, lx + 3, ly + 1);
            }
        }

        private void DrawConfirm(Graphics g)
        {
            Rectangle r = ConfirmRect();
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath gp = RoundedRect(r, (int)(8 * scale)))
            using (SolidBrush bg = new SolidBrush(Theme.Accent))
            {
                g.FillPath(bg, gp);
            }
            g.SmoothingMode = oldSm;
            using (SolidBrush tb = new SolidBrush(Theme.AccentText))
            {
                g.DrawString("Capture  (Enter)", uiFont, tb, r, textFormat);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void DrawToolbar(Graphics g, Rectangle clip)
        {
            if (!toolbarRect.IntersectsWith(clip)) return;
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath gp = RoundedRect(toolbarRect, (int)(12 * scale)))
            using (SolidBrush bg = new SolidBrush(Theme.Bar))
            using (Pen border = new Pen(Theme.BarBorder, 1f))
            {
                g.FillPath(bg, gp);
                g.DrawPath(border, gp);
            }

            foreach (ToolbarButton b in buttons)
            {
                if (b.IsSeparator)
                {
                    using (Pen sp = new Pen(Theme.BarBorder, 1f))
                    {
                        int x = b.Rect.X + b.Rect.Width / 2;
                        int m = (int)(6 * scale);
                        g.DrawLine(sp, x, b.Rect.Y + m, x, b.Rect.Bottom - m);
                    }
                    continue;
                }
                if (b.IsLabel)
                {
                    using (SolidBrush lb = new SolidBrush(Theme.Muted))
                    {
                        g.DrawString(b.Text, uiFont, lb, b.Rect, textFormat);
                    }
                    continue;
                }

                bool on = (b.IsOn != null && b.IsOn());
                if (on || b == hoverButton)
                {
                    Color fillColor = on ? Theme.OnFill : Theme.Hover;
                    using (GraphicsPath bp = RoundedRect(b.Rect, (int)(8 * scale)))
                    using (SolidBrush fill = new SolidBrush(fillColor))
                    {
                        g.FillPath(fill, bp);
                    }
                }
                using (SolidBrush tb = new SolidBrush(on ? Theme.OnText : Theme.Text))
                {
                    g.DrawString(b.Text, uiFont, tb, b.Rect, textFormat);
                }
            }
            g.SmoothingMode = oldSm;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                uiFont.Dispose();
                labelFont.Dispose();
                textFormat.Dispose();
                dimmed.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Small "saved" popup that never takes focus and makes no sound.
    class Toast : Form
    {
        private string text;
        private readonly bool autoClose;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        public Toast(string message, Rectangle monitor, int autoCloseMs)
        {
            text = message;
            autoClose = autoCloseMs > 0;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            int w = 320, h = 56;
            Bounds = new Rectangle(monitor.Right - w - 24, monitor.Bottom - h - 64, w, h);
            timer.Interval = autoClose ? autoCloseMs : 1000;
            timer.Tick += delegate { timer.Stop(); Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (autoClose) timer.Start();
        }

        public void SetText(string message)
        {
            text = message;
            Invalidate();
            Update();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Font f = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush b = new SolidBrush(Color.White))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(text, f, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // Overlay = capture bar in the last-used mode (PrtScn); Window = capture bar in window mode (Alt+PrtScn)
    enum CaptureMode { Overlay, Window, Monitor, AllMonitors }

    class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ScreenshotTool";

        private readonly NotifyIcon tray;
        private readonly Control sync;
        private readonly Native.LowLevelKeyboardProc hookProc;
        private IntPtr hookHandle = IntPtr.Zero;
        private readonly System.Windows.Forms.Timer hookTimer = new System.Windows.Forms.Timer();
        private bool busy;
        private bool swallowKeyUp;
        private bool swallowSKeyUp;
        private bool snapshotPassed;
        private int modifierLogBudget = 12;
        private readonly bool[] keys = new bool[256];
        private readonly string saveDir;
        private readonly ToolStripMenuItem startupItem;

        public TrayApp()
        {
            saveDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

            Settings.Load();

            sync = new Control();
            IntPtr force = sync.Handle; // create the handle so BeginInvoke works from the hook
            GC.KeepAlive(force);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Capture bar  (PrtScn / Win+Shift+S)", null, delegate { PostDelayed(CaptureMode.Overlay); });
            menu.Items.Add("Capture this monitor  (Ctrl+PrtScn)", null, delegate { PostDelayed(CaptureMode.Monitor); });
            menu.Items.Add("Capture a window  (Alt+PrtScn)", null, delegate { PostDelayed(CaptureMode.Window); });
            menu.Items.Add("Capture all monitors, same height  (Shift+PrtScn)", null, delegate { PostDelayed(CaptureMode.AllMonitors); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem adjustItem = new ToolStripMenuItem("Adjust selection before capturing");
            adjustItem.Click += delegate { Settings.Adjust = !Settings.Adjust; Settings.Save(); };
            ToolStripMenuItem saveItem = new ToolStripMenuItem("Save to file");
            saveItem.Click += delegate { Settings.ToggleSave(); };
            ToolStripMenuItem copyItem = new ToolStripMenuItem("Copy to clipboard");
            copyItem.Click += delegate { Settings.ToggleCopy(); };
            menu.Items.Add(adjustItem);
            menu.Items.Add(saveItem);
            menu.Items.Add(copyItem);

            ToolStripMenuItem tolItem = new ToolStripMenuItem("Click tolerance (click = whole monitor)");
            int[] tolChoices = new int[] { 0, 5, 12, 25, 50 };
            foreach (int choice in tolChoices)
            {
                int value = choice;
                ToolStripMenuItem item = new ToolStripMenuItem(value == 0 ? "Off (any movement selects a region)" : value + " px");
                item.Tag = value;
                item.Click += delegate
                {
                    Settings.ClickTolerance = value;
                    Settings.Save();
                };
                tolItem.DropDownItems.Add(item);
            }
            menu.Items.Add(tolItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open screenshots folder", null, delegate { OpenFolder(); });
            startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(startupItem);

            // keep the check marks in sync with the settings (they can also change from the capture bar)
            menu.Opening += delegate
            {
                adjustItem.Checked = Settings.Adjust;
                saveItem.Checked = Settings.SaveFile;
                copyItem.Checked = Settings.CopyClipboard;
                foreach (ToolStripMenuItem t in tolItem.DropDownItems)
                {
                    t.Checked = ((int)t.Tag == Settings.ClickTolerance);
                }
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Screenshot - PrtScn to capture";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) PostDelayed(CaptureMode.Overlay);
            };

            hookProc = HookCallback;
            InstallHook();
            Log("started; hook handle = " + hookHandle);
            // Windows silently drops a low-level hook if it ever stalls (sleep, lock, display change,
            // a slow callback). Re-install it regularly so the hotkeys never fall back to Windows.
            hookTimer.Interval = 15000;
            hookTimer.Tick += delegate { InstallHook(); };
            hookTimer.Start();
            SystemEvents.DisplaySettingsChanged += delegate { InstallHook(); Log("display change: hook re-installed"); };
            SystemEvents.SessionSwitch += delegate { InstallHook(); Log("session switch: hook re-installed"); };
            SystemEvents.PowerModeChanged += delegate { InstallHook(); Log("power change: hook re-installed"); };
            sync.BeginInvoke(new Action(WarmUp));
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Could not install the PrtScn hook. Use the tray icon to capture instead.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- keyboard hook -------------------------------------------------------------

        // Installs a fresh hook, then removes the previous one, so there is never a gap.
        private void InstallHook()
        {
            IntPtr fresh;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
            {
                fresh = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
                    Native.GetModuleHandle(m.ModuleName), 0);
            }
            if (fresh == IntPtr.Zero)
            {
                Log("hook install failed, error = " + Marshal.GetLastWin32Error());
                return;
            }
            IntPtr old = hookHandle;
            hookHandle = fresh;
            if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    bool down = (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN);
                    bool up = (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP);
                    int vk = (int)k.vkCode;

                    // Track modifier state ourselves; GetAsyncKeyState is unreliable inside LL hooks.
                    if (vk >= 0 && vk < 256)
                    {
                        if (down) keys[vk] = true;
                        else if (up) keys[vk] = false;
                    }
                    // Debug: log the first few modifier events (never ordinary typing) to see if the hook fires.
                    if (modifierLogBudget > 0 && (vk == Native.VK_LWIN || vk == Native.VK_RWIN
                        || vk == 0xA0 || vk == 0xA1))
                    {
                        modifierLogBudget--;
                        Log("modifier key vk=0x" + vk.ToString("X") + (down ? " down" : " up"));
                    }
                    bool win = keys[Native.VK_LWIN] || keys[Native.VK_RWIN];
                    bool shift = keys[0xA0] || keys[0xA1] || keys[Native.VK_SHIFT];
                    bool ctrl = keys[0xA2] || keys[0xA3] || keys[Native.VK_CONTROL];
                    bool alt = keys[0xA4] || keys[0xA5] || keys[Native.VK_MENU];

                    if (vk == Native.VK_S)
                    {
                        // Win+Shift+S: take over Windows' own snip shortcut.
                        if (down && win && shift && !ctrl && !alt)
                        {
                            Log("Win+Shift+S down -> capture");
                            if (!swallowSKeyUp)
                            {
                                swallowSKeyUp = true;
                                Post(CaptureMode.Overlay);
                            }
                            return (IntPtr)1;
                        }
                        if (up && swallowSKeyUp)
                        {
                            swallowSKeyUp = false;
                            return (IntPtr)1;
                        }
                    }
                    else if (vk == Native.VK_SNAPSHOT)
                    {
                        Log("PrtScn " + (down ? "down" : "up") + " win=" + win + " ctrl=" + ctrl + " alt=" + alt);
                        CaptureMode mode = CaptureMode.Overlay;
                        if (ctrl) mode = CaptureMode.Monitor;
                        else if (alt) mode = CaptureMode.Window;
                        else if (shift) mode = CaptureMode.AllMonitors;

                        if (down)
                        {
                            if (win)
                            {
                                snapshotPassed = true; // Win+PrtScn: leave it to Windows
                            }
                            else
                            {
                                // Ignore key auto-repeat; only the first press captures.
                                if (!swallowKeyUp)
                                {
                                    swallowKeyUp = true;
                                    Post(mode);
                                }
                                return (IntPtr)1; // swallow so Windows' Snipping Tool does not open
                            }
                        }
                        else if (up)
                        {
                            if (snapshotPassed)
                            {
                                snapshotPassed = false;
                            }
                            else if (swallowKeyUp)
                            {
                                swallowKeyUp = false;
                                return (IntPtr)1;
                            }
                            else if (!win)
                            {
                                // Some keyboards/drivers only report the key-up for PrtScn.
                                Post(mode);
                                return (IntPtr)1;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("hook error: " + ex.Message);
                }
            }
            return Native.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        public static void WriteLog(string text) { Log(text); }

        private static void Log(string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot-tool.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n");
            }
            catch (Exception) { }
        }

        private void Post(CaptureMode mode)
        {
            // Never do real work inside the hook; hand it to the UI thread.
            sync.BeginInvoke(new Action<CaptureMode>(Capture), new object[] { mode });
        }

        // For tray-menu / tray-click captures: wait a moment so the menu is gone before the shot.
        private void PostDelayed(CaptureMode mode)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 250;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                Capture(mode);
            };
            t.Start();
        }

        // ---- capture ------------------------------------------------------------------

        private void Capture(CaptureMode mode)
        {
            Log("Capture called, mode=" + mode + ", busy=" + busy);
            if (busy) return;
            busy = true;
            try
            {
                Bitmap result = null;
                Rectangle toastMonitor = CurrentMonitor();

                if (mode == CaptureMode.Monitor)
                {
                    result = Grab(toastMonitor, PixelFormat.Format24bppRgb);
                }
                else if (mode == CaptureMode.AllMonitors)
                {
                    result = GrabAllMonitors();
                }
                else
                {
                    int startMode = (mode == CaptureMode.Window) ? 1
                        : (mode == CaptureMode.Overlay ? Settings.LastMode : 0);
                    for (;;)
                    {
                        OverlayResult o = RunOverlay(startMode);
                        if (o.Restart)
                        {
                            // "Delay" on the capture bar: close, count down, then freeze the screen again.
                            startMode = o.RestartMode;
                            Countdown(o.RestartDelay, toastMonitor);
                            continue;
                        }
                        result = o.Image;
                        break;
                    }
                }

                if (result != null)
                {
                    using (result)
                    {
                        Save(result, toastMonitor);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("capture error: " + ex.ToString());
                try
                {
                    tray.ShowBalloonTip(3000, "Screenshot failed", ex.Message, ToolTipIcon.Error);
                }
                catch (Exception) { }
            }
            finally
            {
                busy = false;
            }
        }

        // Shows "Capturing in N..." and waits, keeping the UI responsive. The popup is gone
        // before this returns, so it never ends up in the screenshot.
        private static void Countdown(int seconds, Rectangle monitor)
        {
            Toast t = null;
            try
            {
                t = new Toast("Capturing in " + seconds + "...", monitor, 0);
                t.Show();
                t.Refresh();
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                int shown = seconds;
                for (;;)
                {
                    double left = (end - DateTime.UtcNow).TotalSeconds;
                    if (left <= 0) break;
                    int whole = (int)Math.Ceiling(left);
                    if (whole != shown)
                    {
                        shown = whole;
                        t.SetText("Capturing in " + whole + "...");
                    }
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
            }
            finally
            {
                if (t != null) t.Close();
            }
            DateTime until = DateTime.UtcNow.AddMilliseconds(250);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        private static Rectangle VirtualScreen()
        {
            return new Rectangle(
                Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        }

        private static Rectangle CurrentMonitor()
        {
            Native.POINT pt;
            Native.GetCursorPos(out pt);
            IntPtr mon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
            Native.MONITORINFO mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (Native.GetMonitorInfo(mon, ref mi)) return Native.ToRectangle(mi.rcMonitor);
            return VirtualScreen();
        }

        // Visible top-level windows in z-order (top first), with their real on-screen bounds.
        private static List<Rectangle> CollectWindows()
        {
            List<Rectangle> list = new List<Rectangle>();
            uint ownPid = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == ownPid) return true;

                int cloaked;
                if (Native.DwmGetWindowAttributeInt(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                    return true;

                if ((Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0) return true;

                StringBuilder cls = new StringBuilder(64);
                Native.GetClassName(h, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW") return true;

                Native.RECT r;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r,
                    Marshal.SizeOf(typeof(Native.RECT))) != 0)
                {
                    if (!Native.GetWindowRect(h, out r)) return true;
                }
                Rectangle rect = Native.ToRectangle(r);
                if (rect.Width < 20 || rect.Height < 20) return true;
                list.Add(rect);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Straight copy of screen pixels. The process is per-monitor DPI aware, so these
        // coordinates are real device pixels on every monitor: no scaling, no resampling.
        private static Bitmap Grab(Rectangle area, PixelFormat format)
        {
            // BitBlt directly (SRCCOPY | CAPTUREBLT) so layered windows are included.
            // Graphics.CopyFromScreen rejects that flag combination, hence the P/Invoke.
            Bitmap bmp = new Bitmap(area.Width, area.Height, format);
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr destDc = g.GetHdc();
                    try
                    {
                        bool ok = Native.BitBlt(destDc, 0, 0, area.Width, area.Height,
                            screenDc, area.X, area.Y, Native.SRCCOPY | Native.CAPTUREBLT);
                        if (!ok) throw new InvalidOperationException("BitBlt failed");
                    }
                    finally
                    {
                        g.ReleaseHdc(destDc);
                    }
                }
            }
            catch (Exception)
            {
                bmp.Dispose();
                throw;
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
            return bmp;
        }

        private static List<Rectangle> MonitorRects()
        {
            List<Rectangle> list = new List<Rectangle>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr hMon, IntPtr hdc, ref Native.RECT rc, IntPtr data)
                {
                    Native.MONITORINFO mi = new Native.MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                    if (Native.GetMonitorInfo(hMon, ref mi)) list.Add(Native.ToRectangle(mi.rcMonitor));
                    return true;
                }, IntPtr.Zero);
            // left to right (then top to bottom)
            list.Sort(delegate(Rectangle a, Rectangle b)
            {
                int c = a.X.CompareTo(b.X);
                return c != 0 ? c : a.Y.CompareTo(b.Y);
            });
            return list;
        }

        private static Bitmap GrabAllMonitors()
        {
            return ComposeMonitors(delegate(Rectangle r) { return Grab(r, PixelFormat.Format24bppRgb); });
        }

        // Every monitor placed side by side and scaled to the same height (the tallest one), so there
        // is no black gap under a shorter monitor. A monitor that already has the target height is
        // copied 1:1 without any resampling. getPart returns one monitor's pixels.
        private static Bitmap ComposeMonitors(Func<Rectangle, Bitmap> getPart)
        {
            List<Rectangle> mons = MonitorRects();
            if (mons.Count == 0) return getPart(VirtualScreen());
            if (mons.Count == 1) return getPart(mons[0]);

            int targetH = 0;
            foreach (Rectangle m in mons)
            {
                if (m.Height > targetH) targetH = m.Height;
            }

            int[] widths = new int[mons.Count];
            int totalW = 0;
            for (int i = 0; i < mons.Count; i++)
            {
                widths[i] = (int)Math.Round(mons[i].Width * (double)targetH / mons[i].Height);
                totalW += widths[i];
            }
            Log("all monitors: " + mons.Count + " monitors, output " + totalW + " x " + targetH);

            Bitmap canvas = new Bitmap(totalW, targetH, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(canvas))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // no blurry/dark edges when scaling
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    int x = 0;
                    for (int i = 0; i < mons.Count; i++)
                    {
                        using (Bitmap part = getPart(mons[i]))
                        {
                            bool sameSize = (part.Width == widths[i] && part.Height == targetH);
                            g.InterpolationMode = sameSize
                                ? InterpolationMode.NearestNeighbor
                                : InterpolationMode.HighQualityBicubic;
                            g.DrawImage(part, new Rectangle(x, 0, widths[i], targetH),
                                0, 0, part.Width, part.Height, GraphicsUnit.Pixel, ia);
                        }
                        x += widths[i];
                    }
                }
            }
            catch (Exception)
            {
                canvas.Dispose();
                throw;
            }
            return canvas;
        }

        private static OverlayResult RunOverlay(int startMode)
        {
            Rectangle vs = VirtualScreen();
            Rectangle mon = CurrentMonitor();
            List<Rectangle> windows = CollectWindows();
            Log("virtual screen = " + vs + ", monitor = " + mon + ", mode = " + startMode
                + ", windows = " + windows.Count);

            OverlayResult res = new OverlayResult();
            using (Bitmap desktop = Grab(vs, PixelFormat.Format32bppRgb))
            using (SelectionForm form = new SelectionForm(desktop, vs, windows, MonitorRects(), mon, startMode))
            {
                Log("grabbed desktop, showing overlay");
                form.ShowDialog();
                Log("overlay closed, outcome = " + form.Outcome + ", selection = " + form.Result
                    + (form.CloseReason.Length > 0 ? ", reason = " + form.CloseReason : ""));

                if (form.Outcome == OverlayOutcome.Rect)
                {
                    res.Image = desktop.Clone(form.Result, PixelFormat.Format24bppRgb);
                }
                else if (form.Outcome == OverlayOutcome.AllMonitors)
                {
                    Rectangle origin = vs;
                    Bitmap source = desktop;
                    res.Image = ComposeMonitors(delegate(Rectangle r)
                    {
                        Rectangle local = r;
                        local.Offset(-origin.X, -origin.Y);
                        return source.Clone(local, PixelFormat.Format24bppRgb);
                    });
                }
                else if (form.Outcome == OverlayOutcome.DelayRestart)
                {
                    res.Restart = true;
                    res.RestartDelay = form.DelaySeconds;
                    res.RestartMode = form.RestartMode;
                }
            }
            return res;
        }

        private void Save(Bitmap bmp, Rectangle toastMonitor)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string path = null;
            if (Settings.SaveFile)
            {
                Directory.CreateDirectory(saveDir);
                string name = "Screenshot_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".png";
                path = Path.Combine(saveDir, name);
                bmp.Save(path, ImageFormat.Png);
                Log("png saved in " + sw.ElapsedMilliseconds + " ms (" + bmp.Width + "x" + bmp.Height + "): " + path);
            }

            // Feedback first, so it feels instant; the clipboard work follows.
            try
            {
                string verb = (path != null) ? "Saved" : "Copied";
                Toast t = new Toast(verb + "  " + bmp.Width + " x " + bmp.Height, toastMonitor, 1400);
                t.Show();
                t.Refresh();
            }
            catch (Exception) { }

            if (Settings.CopyClipboard)
            {
                sw.Reset();
                sw.Start();
                try
                {
                    // Image for Paint/Word/etc., plus the file (when saved) for chat apps and Explorer,
                    // so pasting into a chat sends the untouched PNG instead of a re-encoded bitmap.
                    DataObject data = new DataObject();
                    data.SetImage(bmp);
                    if (path != null)
                    {
                        StringCollection files = new StringCollection();
                        files.Add(path);
                        data.SetFileDropList(files);
                    }
                    Clipboard.SetDataObject(data, true, 5, 50);
                    Log("clipboard set in " + sw.ElapsedMilliseconds + " ms");
                }
                catch (Exception) { /* clipboard can be locked by another app; the file is saved anyway */ }
            }
        }

        // Pre-loads the slow first-use code paths so the first real capture is not sluggish.
        private void WarmUp()
        {
            try
            {
                Rectangle r = new Rectangle(0, 0, 64, 64);
                using (Bitmap b = Grab(r, PixelFormat.Format32bppRgb))
                using (SelectionForm f = new SelectionForm(b, r, new List<Rectangle>(), new List<Rectangle>(), r, 0))
                using (Bitmap t = new Bitmap(64, 64, PixelFormat.Format32bppRgb))
                using (Graphics g = Graphics.FromImage(t))
                {
                    IntPtr h = f.Handle;
                    GC.KeepAlive(h);
                    g.DrawImage(b, r, 0, 0, 64, 64, GraphicsUnit.Pixel);
                }
                CollectWindows();
                Log("warm-up done");
            }
            catch (Exception ex)
            {
                Log("warm-up failed: " + ex.Message);
            }
        }

        // ---- tray helpers -------------------------------------------------------------

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(saveDir);
                Process.Start("explorer.exe", "\"" + saveDir + "\"");
            }
            catch (Exception) { }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return k != null && k.GetValue(RunValueName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    if (k.GetValue(RunValueName) != null)
                    {
                        k.DeleteValue(RunValueName, false);
                    }
                    else
                    {
                        k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    }
                }
            }
            catch (Exception) { }
            startupItem.Checked = IsStartupEnabled();
        }

        private static Icon MakeIcon()
        {
            // White on a dark taskbar, near-black on a light one.
            Color fg = Theme.TaskbarLight ? Color.FromArgb(32, 32, 32) : Color.White;
            using (Bitmap b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(fg, 3.5f))
                {
                    g.DrawLine(p, 5, 12, 5, 5); g.DrawLine(p, 5, 5, 12, 5);
                    g.DrawLine(p, 20, 5, 27, 5); g.DrawLine(p, 27, 5, 27, 12);
                    g.DrawLine(p, 27, 20, 27, 27); g.DrawLine(p, 27, 27, 20, 27);
                    g.DrawLine(p, 12, 27, 5, 27); g.DrawLine(p, 5, 27, 5, 20);
                }
                IntPtr hIcon = b.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(hIcon);
                }
            }
        }

        protected override void ExitThreadCore()
        {
            if (hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
            tray.Visible = false;
            tray.Dispose();
            sync.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ScreenshotTool_SingleInstance", out created))
            {
                if (!created) return;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    TrayApp.WriteLog("UI exception: " + e.Exception.ToString());
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}

// Screenshot - tiny native-pixel screenshot tool for Windows.
// No dependencies: compiles with the C# compiler that ships with Windows (see build.bat).
//
// Hotkeys (PrtScn is taken over from Windows' Snipping Tool while this runs):
//   PrtScn        select a region (Win+Shift+S does the same)
//   Ctrl+PrtScn   whole monitor the mouse is on
//   Alt+PrtScn    active window
//   Win+PrtScn    left alone (normal Windows behaviour)
//
// Every capture is a straight copy of the screen's real pixels (per-monitor DPI aware),
// saved as PNG, and put on the clipboard as both an image and a file.

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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

    // Full-screen overlay used to drag out a region. It shows a frozen copy of the
    // desktop 1:1 (one image pixel = one screen pixel) so nothing is rescaled.
    class SelectionForm : Form
    {
        private readonly Bitmap shot;
        private readonly Rectangle bounds;
        private bool dragging;
        private Point start;
        private Rectangle sel = Rectangle.Empty;
        private Rectangle lastInvalid = Rectangle.Empty;
        private readonly Font labelFont = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);

        public Rectangle Result = Rectangle.Empty; // in bitmap pixels

        public SelectionForm(Bitmap desktopShot, Rectangle screenBounds)
        {
            shot = desktopShot;
            bounds = screenBounds;
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
        }

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
        }

        protected override void WndProc(ref Message m)
        {
            // Ignore DPI-change resizing: the overlay must stay exactly the virtual-screen rectangle.
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Result = Rectangle.Empty;
                Close();
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Result = Rectangle.Empty;
                Close();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                start = e.Location;
                sel = new Rectangle(e.Location, Size.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                Rectangle old = sel;
                sel = MakeRect(start, e.Location);
                Rectangle dirty = Rectangle.Union(old, sel);
                dirty.Inflate(60, 60);
                Invalidate(dirty);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging && e.Button == MouseButtons.Left)
            {
                dragging = false;
                sel = MakeRect(start, e.Location);
                sel.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                Result = (sel.Width >= 2 && sel.Height >= 2) ? sel : Rectangle.Empty;
                Close();
                return;
            }
            base.OnMouseUp(e);
        }

        private static Rectangle MakeRect(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // painted entirely in OnPaint
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            // Explicit source/dest rectangles in pixels => no DPI based scaling of the bitmap.
            g.DrawImage(shot, clip, clip.X, clip.Y, clip.Width, clip.Height, GraphicsUnit.Pixel);

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                GraphicsState st = g.Save();
                if (!sel.IsEmpty && sel.Width > 0 && sel.Height > 0)
                {
                    g.SetClip(sel, CombineMode.Exclude);
                }
                g.FillRectangle(dim, clip);
                g.Restore(st);
            }

            if (sel.Width > 0 && sel.Height > 0)
            {
                using (Pen p = new Pen(Color.FromArgb(255, 0, 160, 255), 1f))
                {
                    g.DrawRectangle(p, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
                }

                string text = sel.Width.ToString() + " x " + sel.Height.ToString();
                SizeF ts = g.MeasureString(text, labelFont);
                float lx = sel.X;
                float ly = sel.Y - ts.Height - 4;
                if (ly < 0) ly = sel.Y + 4;
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    g.FillRectangle(bg, lx, ly, ts.Width + 6, ts.Height + 2);
                    g.DrawString(text, labelFont, fg, lx + 3, ly + 1);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) labelFont.Dispose();
            base.Dispose(disposing);
        }
    }

    // Small "saved" popup that never takes focus and makes no sound.
    class Toast : Form
    {
        private readonly string text;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        public Toast(string message, Rectangle monitor)
        {
            text = message;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            int w = 320, h = 56;
            Bounds = new Rectangle(monitor.Right - w - 24, monitor.Bottom - h - 64, w, h);
            timer.Interval = 1400;
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
            timer.Start();
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

    enum CaptureMode { Region, Monitor, Window }

    class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ScreenshotTool";

        private readonly NotifyIcon tray;
        private readonly Control sync;
        private readonly Native.LowLevelKeyboardProc hookProc;
        private IntPtr hookHandle = IntPtr.Zero;
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

            sync = new Control();
            IntPtr force = sync.Handle; // create the handle so BeginInvoke works from the hook
            GC.KeepAlive(force);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Capture region  (PrtScn / Win+Shift+S)", null, delegate { PostDelayed(CaptureMode.Region); });
            menu.Items.Add("Capture this monitor  (Ctrl+PrtScn)", null, delegate { PostDelayed(CaptureMode.Monitor); });
            menu.Items.Add("Capture active window  (Alt+PrtScn)", null, delegate { PostDelayed(CaptureMode.Window); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open screenshots folder", null, delegate { OpenFolder(); });
            startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Screenshot - PrtScn to capture";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) PostDelayed(CaptureMode.Region);
            };

            hookProc = HookCallback;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
            {
                hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
                    Native.GetModuleHandle(m.ModuleName), 0);
            }
            Log("started; hook handle = " + hookHandle + ", error = " + Marshal.GetLastWin32Error());
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Could not install the PrtScn hook. Use the tray icon to capture instead.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- keyboard hook -------------------------------------------------------------

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
                                Post(CaptureMode.Region);
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
                        CaptureMode mode = CaptureMode.Region;
                        if (ctrl) mode = CaptureMode.Monitor;
                        else if (alt) mode = CaptureMode.Window;

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
                    result = Grab(CurrentMonitor());
                }
                else if (mode == CaptureMode.Window)
                {
                    Rectangle w = ActiveWindowRect();
                    if (w.Width > 0 && w.Height > 0) result = Grab(w);
                }
                else
                {
                    result = SelectRegion();
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

        private static Rectangle ActiveWindowRect()
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return Rectangle.Empty;
            Native.RECT r;
            int hr = Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r,
                Marshal.SizeOf(typeof(Native.RECT)));
            if (hr != 0)
            {
                if (!Native.GetWindowRect(h, out r)) return Rectangle.Empty;
            }
            Rectangle rect = Native.ToRectangle(r);
            rect.Intersect(VirtualScreen());
            return rect;
        }

        // Straight copy of screen pixels. The process is per-monitor DPI aware, so these
        // coordinates are real device pixels on every monitor: no scaling, no resampling.
        private static Bitmap Grab(Rectangle area)
        {
            // BitBlt directly (SRCCOPY | CAPTUREBLT) so layered windows are included.
            // Graphics.CopyFromScreen rejects that flag combination, hence the P/Invoke.
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
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

        private static Bitmap SelectRegion()
        {
            Rectangle vs = VirtualScreen();
            Log("virtual screen = " + vs);
            using (Bitmap desktop = Grab(vs))
            using (SelectionForm form = new SelectionForm(desktop, vs))
            {
                Log("grabbed desktop, showing overlay");
                form.ShowDialog();
                Rectangle r = form.Result;
                Log("overlay closed, selection = " + r);
                if (r.IsEmpty) return null;
                return desktop.Clone(r, PixelFormat.Format24bppRgb);
            }
        }

        private void Save(Bitmap bmp, Rectangle toastMonitor)
        {
            Directory.CreateDirectory(saveDir);
            string name = "Screenshot_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".png";
            string path = Path.Combine(saveDir, name);
            bmp.Save(path, ImageFormat.Png);
            Log("saved " + path);

            try
            {
                // Image for Paint/Word/etc., file for chat apps and Explorer, so pasting
                // into a chat sends the untouched PNG instead of a re-encoded bitmap.
                DataObject data = new DataObject();
                data.SetImage(bmp);
                StringCollection files = new StringCollection();
                files.Add(path);
                data.SetFileDropList(files);
                Clipboard.SetDataObject(data, true, 5, 50);
            }
            catch (Exception) { /* clipboard can be locked by another app; the file is saved anyway */ }

            try
            {
                Toast t = new Toast("Saved  " + bmp.Width + " x " + bmp.Height, toastMonitor);
                t.Show();
            }
            catch (Exception) { }
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
            using (Bitmap b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    g.FillEllipse(bg, 1, 1, 30, 30);
                }
                using (Pen p = new Pen(Color.White, 2.5f))
                {
                    g.DrawLine(p, 9, 14, 9, 9); g.DrawLine(p, 9, 9, 14, 9);
                    g.DrawLine(p, 18, 9, 23, 9); g.DrawLine(p, 23, 9, 23, 14);
                    g.DrawLine(p, 23, 18, 23, 23); g.DrawLine(p, 23, 23, 18, 23);
                    g.DrawLine(p, 14, 23, 9, 23); g.DrawLine(p, 9, 23, 9, 18);
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

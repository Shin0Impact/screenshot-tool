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
using System.Text.RegularExpressions;
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

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
            public uint time; public int ptX; public int ptY;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
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

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        public static extern int DwmSetWindowAttributeInt(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

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
        public static bool Edit = false;           // adjust the selection before capturing, then annotate
        public static bool Adjust { get { return Edit; } set { Edit = value; } }
        public static bool Annotate { get { return Edit; } set { Edit = value; } }
        public static bool MonitorFollow = false;  // Monitor capture takes the monitor under the mouse instead of asking
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
                    Edit = GetBool(k, "Edit", false);
                    MonitorFollow = GetBool(k, "MonitorFollow", false);
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
                    k.SetValue("Edit", Edit ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("MonitorFollow", MonitorFollow ? 1 : 0, RegistryValueKind.DWord);
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

    // Temporary diagnostics: %LOCALAPPDATA%\ScreenshotTool\screenshot-tool.log (kept small, never throws).
    static class Diag
    {
        private static readonly object gate = new object();
        private static string path;

        public static string Path_
        {
            get
            {
                if (path == null)
                {
                    string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotTool");
                    Directory.CreateDirectory(d);
                    path = System.IO.Path.Combine(d, "screenshot-tool.log");
                }
                return path;
            }
        }

        public static void Log(string text)
        {
            try
            {
                lock (gate)
                {
                    string p = Path_;
                    FileInfo fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 1000000)
                    {
                        try { File.Delete(p + ".old"); File.Move(p, p + ".old"); } catch (Exception) { }
                    }
                    File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Thread.CurrentThread.ManagedThreadId + "] " + text + "\r\n");
                }
            }
            catch (Exception) { }
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        public static Color Back() { return Theme.Light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(43, 43, 43); }
        public static Color Hot() { return Theme.Light ? Color.FromArgb(229, 229, 229) : Color.FromArgb(70, 70, 70); }
        private static Color Line() { return Theme.Light ? Color.FromArgb(205, 205, 205) : Color.FromArgb(72, 72, 72); }

        public override Color ToolStripDropDownBackground { get { return Back(); } }
        public override Color ImageMarginGradientBegin { get { return Back(); } }
        public override Color ImageMarginGradientMiddle { get { return Back(); } }
        public override Color ImageMarginGradientEnd { get { return Back(); } }
        public override Color MenuBorder { get { return Line(); } }
        public override Color MenuItemBorder { get { return Hot(); } }
        public override Color MenuItemSelected { get { return Hot(); } }
        public override Color MenuItemSelectedGradientBegin { get { return Hot(); } }
        public override Color MenuItemSelectedGradientEnd { get { return Hot(); } }
        public override Color MenuItemPressedGradientBegin { get { return Hot(); } }
        public override Color MenuItemPressedGradientMiddle { get { return Hot(); } }
        public override Color MenuItemPressedGradientEnd { get { return Hot(); } }
        public override Color SeparatorDark { get { return Line(); } }
        public override Color SeparatorLight { get { return Back(); } }
        public override Color CheckBackground { get { return Hot(); } }
        public override Color CheckSelectedBackground { get { return Hot(); } }
        public override Color CheckPressedBackground { get { return Hot(); } }
    }

    // The blurred picture of the screen behind the tray menu (taken just before it opens).
    static class FrostBackdrop
    {
        public static Bitmap Small;
        public static Point Origin;
        public const float Factor = 16f;

        public static void Capture(Rectangle monitor)
        {
            Release();
            try
            {
                using (Bitmap full = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full))
                        g.CopyFromScreen(monitor.Location, Point.Empty, monitor.Size);
                    int w = Math.Max(2, (int)(monitor.Width / Factor)), h = Math.Max(2, (int)(monitor.Height / Factor));
                    Bitmap small = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                    using (Graphics sg = Graphics.FromImage(small))
                    {
                        sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        sg.DrawImage(full, new Rectangle(0, 0, w, h), 0, 0, monitor.Width, monitor.Height, GraphicsUnit.Pixel);
                    }
                    Small = small;
                    Origin = monitor.Location;
                }
            }
            catch (Exception) { Small = null; }
        }

        public static void Release()
        {
            if (Small != null) { Small.Dispose(); Small = null; }
        }

        // Windows 11: smooth rounded corners for a popup window (ignored on older Windows).
        public static void Round(IntPtr handle)
        {
            try { int pref = 2; Native.DwmSetWindowAttributeInt(handle, 33, ref pref, 4); } catch (Exception) { }
        }
    }

    // Tray menu look: frosted, matte background that shows the blurred desktop behind it, a rounded
    // hover highlight and a plain check mark instead of a boxed one.
    class CleanRenderer : ToolStripProfessionalRenderer
    {
        public CleanRenderer() : base(new MenuColors()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            ToolStripDropDown dd = e.ToolStrip as ToolStripDropDown;
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
            Bitmap small = FrostBackdrop.Small;
            if (dd != null && small != null && dd.IsHandleCreated)
            {
                Point sp = dd.PointToScreen(Point.Empty);
                float k = FrostBackdrop.Factor;
                using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, r, (sp.X - FrostBackdrop.Origin.X) / k, (sp.Y - FrostBackdrop.Origin.Y) / k,
                        r.Width / k, r.Height / k, GraphicsUnit.Pixel, ia);
                }
                using (SolidBrush tint = new SolidBrush(Theme.Frost))
                    g.FillRectangle(tint, r);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(MenuColors.Back())) g.FillRectangle(b, r);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Light ? Color.FromArgb(50, 0, 0, 0) : Color.FromArgb(60, 255, 255, 255)))
                e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            SmoothingMode old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath gp = SelectionForm.RoundedRectPath(r, 5))
            using (SolidBrush b = new SolidBrush(Theme.Light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255)))
                e.Graphics.FillPath(b, gp);
            e.Graphics.SmoothingMode = old;
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            Color fg = Theme.Light ? Color.FromArgb(25, 25, 25) : Color.FromArgb(240, 240, 240);
            SmoothingMode old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(fg, 1.8f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, new PointF[] {
                    new PointF(cx - 4.5f, cy + 0.5f), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 4.5f, cy - 3.5f) });
            }
            e.Graphics.SmoothingMode = old;
        }
    }

    // Neutral colours that follow the Windows light/dark setting.
    static class Theme
    {
        public static bool Light;
        public static Color Frost, Bar, BarBorder, Text, Muted, OnFill, OnText, Hover, Accent, AccentText;

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
                Frost = Color.FromArgb(188, 243, 243, 245);
                Bar = Color.FromArgb(246, 243, 243, 243);
                BarBorder = Color.FromArgb(50, 0, 0, 0);
                Text = Color.FromArgb(255, 25, 25, 25);
                Muted = Color.FromArgb(255, 110, 110, 110);
                OnFill = Color.FromArgb(255, 40, 40, 40);
                OnText = Color.White;
                Hover = Color.FromArgb(40, 0, 0, 0);
                Accent = Color.FromArgb(255, 40, 40, 40);
                AccentText = Color.White;
            }
            else
            {
                Frost = Color.FromArgb(180, 30, 30, 34);
                Bar = Color.FromArgb(244, 32, 32, 32);
                BarBorder = Color.FromArgb(50, 255, 255, 255);
                Text = Color.FromArgb(255, 240, 240, 240);
                Muted = Color.FromArgb(255, 160, 160, 160);
                OnFill = Color.FromArgb(255, 235, 235, 235);
                OnText = Color.FromArgb(255, 25, 25, 25);
                Hover = Color.FromArgb(46, 255, 255, 255);
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
        public Color? Swatch;      // colour swatch button (annotation editor)
        public bool IsSizeButton;
        public bool RightGroup;
        public bool Primary;
    }

    class Annotation
    {
        public int Kind;     // 0 pen, 1 arrow, 2 box, 3 highlight, 4 text
        public Color Color;
        public float Width;
        public List<PointF> Points = new List<PointF>();   // pen: whole stroke; others: start, end (text: anchor)
        public string Text = "";
    }

    static class Fg
    {
        // Windows only lets the foreground app hand focus over; joining its input queue briefly gets
        // around that so a window can come up above a full-screen game.
        public static void Take(IntPtr handle)
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                uint pid;
                uint fgThread = (fg == IntPtr.Zero) ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
                uint me = Native.GetCurrentThreadId();
                bool attached = (fgThread != 0 && fgThread != me) && Native.AttachThreadInput(me, fgThread, true);
                Native.BringWindowToTop(handle);
                Native.SetForegroundWindow(handle);
                if (attached) Native.AttachThreadInput(me, fgThread, false);
            }
            catch (Exception) { }
        }
    }

    // Simple annotation editor shown after a capture when "Annotate" is on: pen, arrow, box,
    // highlight and text on top of the screenshot. Done (Enter) keeps the result, Esc discards it.
    class AnnotateForm : Form
    {
        private readonly Bitmap image;
        private readonly float scale;
        private readonly float view;          // image pixels -> screen pixels
        private readonly Font uiFont;
        private readonly StringFormat textFormat = new StringFormat();
        private readonly List<ToolbarButton> buttons = new List<ToolbarButton>();
        private readonly List<Annotation> items = new List<Annotation>();
        private Rectangle barRect;
        private Rectangle imgRect;
        private ToolbarButton hoverButton;
        private Annotation current;           // being dragged
        private Annotation editing;           // text being typed

        private int tool = 1;
        private int colorIndex = 0;
        private int sizeIndex = 1;
        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(235, 64, 52), Color.FromArgb(255, 204, 0), Color.FromArgb(52, 199, 89),
            Color.FromArgb(10, 132, 255), Color.FromArgb(20, 20, 20), Color.FromArgb(255, 255, 255)
        };
        private static readonly float[] Sizes = new float[] { 2f, 4f, 7f, 12f };

        public bool Accepted;
        public Bitmap Result;

        public AnnotateForm(Bitmap shot, Rectangle monitor)
        {
            Theme.Refresh();
            image = shot;
            scale = SelectionForm.MonitorScale(monitor);
            uiFont = new Font("Segoe UI", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            textFormat.Alignment = StringAlignment.Center;
            textFormat.LineAlignment = StringAlignment.Center;
            textFormat.FormatFlags = StringFormatFlags.NoWrap;

            AutoScaleMode = AutoScaleMode.None;
            Text = "Annotate";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(30, 30, 30);

            BuildButtons();   // sets barRect width / button rects (without position of right group yet)

            Rectangle wa = Screen.FromRectangle(monitor).WorkingArea;
            int pad = (int)(12 * scale);
            int barH = (int)(52 * scale);
            float maxW = wa.Width * 0.92f - 2 * pad;
            float maxH = wa.Height * 0.88f - barH - 2 * pad;
            float v = Math.Min(1f, Math.Min(maxW / image.Width, maxH / image.Height));
            if (v <= 0f) v = 1f;
            view = v;
            int iw = Math.Max(1, (int)(image.Width * view));
            int ih = Math.Max(1, (int)(image.Height * view));
            int clientW = Math.Max(iw + 2 * pad, barRect.Width + 2 * pad);
            int clientH = barH + ih + 2 * pad;
            ClientSize = new Size(clientW, clientH);
            Location = new Point(monitor.X + (monitor.Width - Width) / 2, monitor.Y + (monitor.Height - Height) / 2);
            if (Location.Y < wa.Y) Location = new Point(Location.X, wa.Y);

            barRect = new Rectangle(pad, pad / 2, clientW - 2 * pad, barH - pad / 2);
            imgRect = new Rectangle((clientW - iw) / 2, barH + pad, iw, ih);
            LayoutButtons();
        }

        // ---- toolbar ----------------------------------------------------------------------

        private void AddBtn(string text, Func<bool> on, Action click)
        {
            ToolbarButton b = new ToolbarButton();
            b.Text = text; b.IsOn = on; b.Click = click;
            buttons.Add(b);
        }

        private void AddSep()
        {
            ToolbarButton b = new ToolbarButton();
            b.IsSeparator = true;
            buttons.Add(b);
        }

        private void BuildButtons()
        {
            AddBtn("Pen", delegate { return tool == 0; }, delegate { SetTool(0); });
            AddBtn("Arrow", delegate { return tool == 1; }, delegate { SetTool(1); });
            AddBtn("Box", delegate { return tool == 2; }, delegate { SetTool(2); });
            AddBtn("Highlight", delegate { return tool == 3; }, delegate { SetTool(3); });
            AddBtn("Text", delegate { return tool == 4; }, delegate { SetTool(4); });
            AddSep();
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = i;
                ToolbarButton b = new ToolbarButton();
                b.Swatch = Palette[i];
                b.IsOn = delegate { return colorIndex == idx; };
                b.Click = delegate { colorIndex = idx; Invalidate(); };
                buttons.Add(b);
            }
            AddSep();
            ToolbarButton sz = new ToolbarButton();
            sz.Text = "Size " + (int)Sizes[sizeIndex];
            sz.IsSizeButton = true;
            sz.Click = delegate
            {
                sizeIndex = (sizeIndex + 1) % Sizes.Length;
                Invalidate();
            };
            buttons.Add(sz);
            AddBtn("Undo", null, delegate { Undo(); });
            // right-aligned group
            ToolbarButton d = new ToolbarButton(); d.Text = "Discard  (Esc)"; d.Click = delegate { Discard(); }; d.RightGroup = true; buttons.Add(d);
            ToolbarButton ok = new ToolbarButton(); ok.Text = "Done  (Enter)"; ok.Click = delegate { Done(); }; ok.RightGroup = true; ok.Primary = true; buttons.Add(ok);

            // provisional width of the whole bar, used to size the window
            int gap = (int)(4 * scale), btnH = (int)(36 * scale), pad = (int)(14 * scale);
            int total = (int)(16 * scale);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (ToolbarButton b in buttons)
                {
                    total += ButtonWidth(g, b, pad, btnH) + gap;
                }
            }
            barRect = new Rectangle(0, 0, total + (int)(24 * scale), 0);
        }

        private int ButtonWidth(Graphics g, ToolbarButton b, int pad, int btnH)
        {
            if (b.IsSeparator) return (int)(14 * scale);
            if (b.Swatch.HasValue) return btnH;
            int tw = (int)Math.Ceiling(g.MeasureString(b.IsSizeButton ? "Size 12" : b.Text, uiFont).Width);
            return Math.Max(btnH, tw + pad * 2);
        }

        private void LayoutButtons()
        {
            int gap = (int)(4 * scale), btnH = (int)(36 * scale), pad = (int)(14 * scale), outer = (int)(8 * scale);
            int y = barRect.Y + (barRect.Height - btnH) / 2;
            int x = barRect.X + outer;
            int rx = barRect.Right - outer;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (ToolbarButton b in buttons)
                {
                    if (b.RightGroup) continue;
                    int w = ButtonWidth(g, b, pad, btnH);
                    b.Rect = new Rectangle(x, y, w, btnH);
                    x += w + gap;
                }
                for (int i = buttons.Count - 1; i >= 0; i--)
                {
                    ToolbarButton b = buttons[i];
                    if (!b.RightGroup) continue;
                    int w = ButtonWidth(g, b, pad, btnH);
                    rx -= w;
                    b.Rect = new Rectangle(rx, y, w, btnH);
                    rx -= gap;
                }
            }
        }

        private void SetTool(int t)
        {
            CommitText();
            tool = t;
            Invalidate();
        }

        private ToolbarButton HitButton(Point p)
        {
            if (!barRect.Contains(p)) return null;
            foreach (ToolbarButton b in buttons)
            {
                if (b.Click != null && b.Rect.Contains(p)) return b;
            }
            return null;
        }

        // ---- actions ----------------------------------------------------------------------

        private void CommitText()
        {
            if (editing != null)
            {
                if (editing.Text.Length > 0) items.Add(editing);
                editing = null;
                Invalidate();
            }
        }

        private void Undo()
        {
            if (editing != null) { editing = null; Invalidate(); return; }
            if (items.Count > 0) { items.RemoveAt(items.Count - 1); Invalidate(); }
        }

        private void Done()
        {
            CommitText();
            Result = Compose();
            Accepted = true;
            Close();
        }

        private void Discard()
        {
            Accepted = false;
            Close();
        }

        private Bitmap Compose()
        {
            Bitmap b = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
                foreach (Annotation a in items) DrawAnnotation(g, a, false);
            }
            return b;
        }

        // ---- input ------------------------------------------------------------------------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Fg.Take(Handle);
            Activate();
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            if (key == Keys.Escape)
            {
                if (editing != null) { editing = null; Invalidate(); }
                else Discard();
                return true;
            }
            if (key == Keys.Enter)
            {
                if (editing != null) CommitText();
                else Done();
                return true;
            }
            if (ctrl && key == Keys.Z) { Undo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (editing != null)
            {
                if (e.KeyChar == (char)8)
                {
                    if (editing.Text.Length > 0) editing.Text = editing.Text.Substring(0, editing.Text.Length - 1);
                }
                else if (!char.IsControl(e.KeyChar))
                {
                    editing.Text += e.KeyChar;
                }
                e.Handled = true;
                Invalidate(imgRect);
                return;
            }
            base.OnKeyPress(e);
        }

        private PointF ToImage(Point p)
        {
            float x = (p.X - imgRect.X) / view;
            float y = (p.Y - imgRect.Y) / view;
            x = Math.Max(0f, Math.Min(image.Width, x));
            y = Math.Max(0f, Math.Min(image.Height, y));
            return new PointF(x, y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
            ToolbarButton b = HitButton(e.Location);
            if (b != null)
            {
                b.Click();
                if (b.IsSizeButton) b.Text = "Size " + (int)Sizes[sizeIndex];
                if (!IsDisposed) Invalidate(barRect);
                return;
            }
            if (!imgRect.Contains(e.Location)) return;

            PointF p = ToImage(e.Location);
            if (tool == 4)
            {
                CommitText();
                editing = new Annotation();
                editing.Kind = 4;
                editing.Color = Palette[colorIndex];
                editing.Width = Sizes[sizeIndex];
                editing.Points.Add(p);
                Invalidate(imgRect);
                return;
            }
            CommitText();
            current = new Annotation();
            current.Kind = tool;
            current.Color = Palette[colorIndex];
            current.Width = Sizes[sizeIndex];
            current.Points.Add(p);
            if (tool != 0) current.Points.Add(p);
            Invalidate(imgRect);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            ToolbarButton hb = HitButton(e.Location);
            if (hb != hoverButton) { hoverButton = hb; Invalidate(barRect); }

            if (current != null)
            {
                PointF p = ToImage(e.Location);
                if (current.Kind == 0) current.Points.Add(p);
                else current.Points[1] = p;
                Invalidate(imgRect);
            }
            Cursor = (imgRect.Contains(e.Location) && hb == null) ? (tool == 4 ? Cursors.IBeam : Cursors.Cross) : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && current != null)
            {
                Annotation a = current;
                current = null;
                bool tiny = false;
                if (a.Kind != 0)
                {
                    float dx = a.Points[1].X - a.Points[0].X, dy = a.Points[1].Y - a.Points[0].Y;
                    tiny = (dx * dx + dy * dy) < 9f;
                }
                if (!tiny) items.Add(a);
                Invalidate(imgRect);
                return;
            }
            base.OnMouseUp(e);
        }

        // ---- painting ---------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            // toolbar
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (GraphicsPath gp = SelectionForm.RoundedRectPath(barRect, (int)(12 * scale)))
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
                bool on = (b.IsOn != null && b.IsOn());
                if (b.Swatch.HasValue)
                {
                    int d = (int)(22 * scale);
                    Rectangle c = new Rectangle(b.Rect.X + (b.Rect.Width - d) / 2, b.Rect.Y + (b.Rect.Height - d) / 2, d, d);
                    using (SolidBrush sb = new SolidBrush(b.Swatch.Value))
                    using (Pen rim = new Pen(Theme.BarBorder, 1f))
                    using (Pen sel = new Pen(Theme.Text, Math.Max(2f, 2f * scale)))
                    {
                        g.FillEllipse(sb, c);
                        g.DrawEllipse(rim, c);
                        if (on)
                        {
                            Rectangle o = Rectangle.Inflate(c, (int)(4 * scale), (int)(4 * scale));
                            g.DrawEllipse(sel, o);
                        }
                    }
                    continue;
                }
                if (on || b == hoverButton || b.Primary)
                {
                    Color fillColor = (on || b.Primary) ? Theme.OnFill : Theme.Hover;
                    if (b == hoverButton && !on && !b.Primary) fillColor = Theme.Hover;
                    using (GraphicsPath bp = SelectionForm.RoundedRectPath(b.Rect, (int)(8 * scale)))
                    using (SolidBrush fill = new SolidBrush(fillColor))
                    {
                        g.FillPath(fill, bp);
                    }
                }
                using (SolidBrush tb = new SolidBrush((on || b.Primary) ? Theme.OnText : Theme.Text))
                {
                    g.DrawString(b.Text, uiFont, tb, b.Rect, textFormat);
                }
            }

            // image + annotations
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = (view < 1f) ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, imgRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            using (Pen frame = new Pen(Theme.BarBorder, 1f))
            {
                g.DrawRectangle(frame, imgRect.X - 1, imgRect.Y - 1, imgRect.Width + 1, imgRect.Height + 1);
            }

            GraphicsState state = g.Save();
            g.SetClip(imgRect);
            g.TranslateTransform(imgRect.X, imgRect.Y);
            g.ScaleTransform(view, view);
            foreach (Annotation a in items) DrawAnnotation(g, a, false);
            if (current != null) DrawAnnotation(g, current, false);
            if (editing != null) DrawAnnotation(g, editing, true);
            g.Restore(state);
        }

        private static void DrawAnnotation(Graphics g, Annotation a, bool caret)
        {
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            try
            {
                switch (a.Kind)
                {
                    case 0:
                        if (a.Points.Count == 1)
                        {
                            using (SolidBrush b = new SolidBrush(a.Color))
                            {
                                g.FillEllipse(b, a.Points[0].X - a.Width / 2, a.Points[0].Y - a.Width / 2, a.Width, a.Width);
                            }
                        }
                        else
                        {
                            using (Pen p = new Pen(a.Color, a.Width))
                            {
                                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                                g.DrawLines(p, a.Points.ToArray());
                            }
                        }
                        break;
                    case 1:
                        DrawArrow(g, a);
                        break;
                    case 2:
                    case 3:
                        {
                            PointF p0 = a.Points[0], p1 = a.Points[1];
                            float x = Math.Min(p0.X, p1.X), y = Math.Min(p0.Y, p1.Y);
                            float w = Math.Abs(p0.X - p1.X), h = Math.Abs(p0.Y - p1.Y);
                            if (a.Kind == 3)
                            {
                                using (SolidBrush b = new SolidBrush(Color.FromArgb(105, a.Color)))
                                {
                                    g.FillRectangle(b, x, y, w, h);
                                }
                            }
                            else
                            {
                                using (Pen p = new Pen(a.Color, a.Width))
                                {
                                    p.LineJoin = LineJoin.Round;
                                    g.DrawRectangle(p, x, y, w, h);
                                }
                            }
                        }
                        break;
                    case 4:
                        {
                            float px = 14f + a.Width * 2.4f;
                            using (Font f = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (SolidBrush b = new SolidBrush(a.Color))
                            {
                                string t = a.Text + (caret ? "|" : "");
                                g.DrawString(t, f, b, a.Points[0].X, a.Points[0].Y, StringFormat.GenericTypographic);
                            }
                        }
                        break;
                }
            }
            finally
            {
                g.SmoothingMode = oldSm;
            }
        }

        private static void DrawArrow(Graphics g, Annotation a)
        {
            PointF s = a.Points[0], t = a.Points[1];
            float dx = t.X - s.X, dy = t.Y - s.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Min(len, Math.Max(14f, a.Width * 4.5f));
            double half = 28.0 * Math.PI / 180.0;
            float c = (float)Math.Cos(half), sn = (float)Math.Sin(half);
            // rotate the reversed direction by +/- half
            PointF p1 = new PointF(t.X - head * (ux * c - uy * sn), t.Y - head * (uy * c + ux * sn));
            PointF p2 = new PointF(t.X - head * (ux * c + uy * sn), t.Y - head * (uy * c - ux * sn));
            PointF neck = new PointF(t.X - ux * head * 0.7f, t.Y - uy * head * 0.7f);
            using (Pen p = new Pen(a.Color, a.Width))
            using (SolidBrush b = new SolidBrush(a.Color))
            {
                p.StartCap = LineCap.Round;
                g.DrawLine(p, s, neck);
                g.FillPolygon(b, new PointF[] { t, p1, p2 });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                uiFont.Dispose();
                textFormat.Dispose();
            }
            base.Dispose(disposing);
        }
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
        private Point startCursor;   // mouse position (screen) when the overlay opened
        private bool clickedAway;    // this press only deselected an existing selection
        private bool dragging;       // drawing a new selection
        private bool exceeded;       // pointer left the click-tolerance zone during this drag
        private Point start;
        private Rectangle sel = Rectangle.Empty;   // region selection, or the hovered window
        private bool hasSelection;   // a finished region that can still be adjusted
        private int activeHandle = -1;             // 0-7 = resize handle, 8 = move, -1 = none
        private Rectangle dragOrig;
        private Point dragOrigPoint;

        public OverlayOutcome Outcome = OverlayOutcome.Cancelled;
        public Rectangle Result = Rectangle.Empty;   // bitmap pixels
        public int DelaySeconds;
        public int RestartMode;
        public string CloseReason = "";
        private bool restartPending;
        private readonly DateTime created = DateTime.UtcNow;

        public SelectionForm(Bitmap desktopShot, Rectangle screenBounds, List<Rectangle> windowRects,
            List<Rectangle> monitorRects, Rectangle toolbarMonitorScreen, int startMode)
        {
            Theme.Refresh();
            monitors = monitorRects;
            Native.POINT sc;
            Native.GetCursorPos(out sc);
            startCursor = new Point(sc.X, sc.Y);
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
            slideOffset = (int)(34 * scale);
        }

        internal static float MonitorScale(Rectangle monitorScreen)
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
            AddButton("Window", delegate { return mode == 1 && !Settings.MonitorFollow; },
                delegate { if (Settings.MonitorFollow) FinishFollowWindow(); else SetMode(1); });
            AddButton("Monitor", delegate { return mode == 2 && !Settings.MonitorFollow; },
                delegate { if (Settings.MonitorFollow) FinishRect(monitorClient); else SetMode(2); });
            AddButton("All monitors", null, delegate { Outcome = OverlayOutcome.AllMonitors; Close(); });
            AddSeparator();
            AddButton("Edit", delegate { return Settings.Edit; },
                delegate { Settings.Edit = !Settings.Edit; Settings.Save(); });
            AddButton("Save", delegate { return Settings.SaveFile; }, delegate { Settings.ToggleSave(); });
            AddButton("Copy", delegate { return Settings.CopyClipboard; }, delegate { Settings.ToggleCopy(); });

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

        private void Confirm()
        {
            if (hasSelection) FinishRect(sel);
        }

        // Shortcut pressed again while open: close and reopen on a fresh screenshot.
        public void RestartFresh()
        {
            // Pressed again before the window is on screen (the shortcut was spammed): remember it and
            // restart as soon as the window is up, instead of closing a window that does not exist yet.
            if (!IsHandleCreated || !Visible) { restartPending = true; Diag.Log("restart asked before overlay was visible: queued"); return; }
            CloseReason = "shortcut again";
            Outcome = OverlayOutcome.DelayRestart;
            DelaySeconds = 0;
            RestartMode = mode;
            try { Close(); } catch (Exception) { ForceClose(); }
        }

        // Last resort for an overlay that never came up: end its dialog so the app can capture again.
        public void ForceClose()
        {
            try { Outcome = OverlayOutcome.Cancelled; CloseReason = "forced"; } catch (Exception) { }
            try { DialogResult = DialogResult.Cancel; } catch (Exception) { }
            try { Hide(); } catch (Exception) { }
            try { Close(); } catch (Exception) { }
        }

        public bool NeverShown { get { return !Visible && (DateTime.UtcNow - created).TotalSeconds > 2; } }

        // Follow mouse: the window that was under the mouse when the shortcut was pressed.
        private void FinishFollowWindow()
        {
            if (windows == null) return;
            foreach (Rectangle w in windows)
            {
                if (w.Contains(startCursor))
                {
                    Rectangle r = w;
                    r.Offset(-bounds.X, -bounds.Y);
                    FinishRect(r);
                    return;
                }
            }
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
            TakeForeground();
            Activate();
            Focus();
            StartSlideIn();
            if (restartPending)
            {
                restartPending = false;
                BeginInvoke(new Action(RestartFresh));
            }
            // A game in front may fight for the top spot: check a moment later and re-assert.
            System.Windows.Forms.Timer check = new System.Windows.Forms.Timer();
            check.Interval = 150;
            check.Tick += delegate
            {
                check.Stop();
                check.Dispose();
                if (IsDisposed) return;
                bool fg = (Native.GetForegroundWindow() == Handle);
                if (!fg)
                {
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width,
                        bounds.Height, Native.SWP_SHOWWINDOW);
                    TakeForeground();
                }
            };
            check.Start();

            if (windowMode)
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                UpdateHover(new Point(pt.X - bounds.X, pt.Y - bounds.Y));
            }
        }

        private void TakeForeground() { Fg.Take(Handle); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Anything that did not go through Cancel()/Finish*() was closed from outside. The log showed
            // unexplained closes (no key, no click, CloseReason.None) a fraction of a second after opening,
            // so those are refused: the overlay only ever closes through its own buttons and keys.
            if (CloseReason.Length == 0 && Outcome == OverlayOutcome.Cancelled)
            {
                if (e.CloseReason == System.Windows.Forms.CloseReason.None)
                {
                    e.Cancel = true;
                    return;
                }
                CloseReason = "external (" + e.CloseReason + ")";
            }
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
                    // clicking away only deselects
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

        internal static GraphicsPath RoundedRectPath(Rectangle r, int radius) { return RoundedRect(r, radius); }

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

        private Bitmap acrylic;          // the screen around the toolbar, shrunk (see FrostBackdrop.Factor)
        private Point acrylicOrigin;      // where that picture starts, in screen-shot pixels
        private Rectangle acrylicFor;

        private void EnsureAcrylic()
        {
            if (acrylic != null && acrylicFor == toolbarRect) return;
            if (acrylic != null) { acrylic.Dispose(); acrylic = null; }
            acrylicFor = toolbarRect;
            Rectangle r = Rectangle.Intersect(Rectangle.Inflate(toolbarRect, 96, 96), new Rectangle(0, 0, shot.Width, shot.Height));
            if (r.Width < 4 || r.Height < 4) return;
            try
            {
                float k = FrostBackdrop.Factor;
                int sw = Math.Max(2, (int)(r.Width / k)), sh = Math.Max(2, (int)(r.Height / k));
                Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppRgb);
                using (Graphics sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.DrawImage(shot, new Rectangle(0, 0, sw, sh), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
                }
                acrylic = small;
                acrylicOrigin = r.Location;
            }
            catch (Exception) { acrylic = null; }
        }

        private int slideOffset;   // pixels the bar is still above its place while it slides in

        private void DrawToolbar(Graphics g, Rectangle clip)
        {
            if (slideOffset == 0) { DrawToolbarCore(g, clip); return; }
            GraphicsState keep = g.Save();
            g.TranslateTransform(0, -slideOffset);
            DrawToolbarCore(g, clip);
            g.Restore(keep);
        }

        private void StartSlideIn()
        {
            int dist = slideOffset;
            if (dist <= 0) return;
            Stopwatch sw = Stopwatch.StartNew();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 15;
            t.Tick += delegate
            {
                if (IsDisposed) { t.Stop(); t.Dispose(); return; }
                double k = Math.Min(1.0, sw.ElapsedMilliseconds / 170.0);
                double ease = 1.0 - Math.Pow(1.0 - k, 3);
                slideOffset = k >= 1.0 ? 0 : (int)Math.Round(dist * (1.0 - ease));
                Invalidate(new Rectangle(toolbarRect.X - 4, toolbarRect.Y - dist - 4, toolbarRect.Width + 8, toolbarRect.Height + dist + 8));
                if (k >= 1.0) { t.Stop(); t.Dispose(); }
            };
            t.Start();
        }

        private void DrawToolbarCore(Graphics g, Rectangle clip)
        {
            if (!toolbarRect.IntersectsWith(clip)) return;
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            EnsureAcrylic();
            using (GraphicsPath gp = RoundedRect(toolbarRect, (int)(8 * scale)))
            {
                // Frosted look: the blurred desktop behind the bar, a matte tint on top, a soft edge.
                GraphicsState st = g.Save();
                g.SetClip(gp, CombineMode.Intersect);
                if (acrylic != null)
                {
                    // same blur and tint as the tray menu: the screen behind, shrunk 16x and stretched back
                    float k = FrostBackdrop.Factor;
                    using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(acrylic, toolbarRect, (toolbarRect.X - acrylicOrigin.X) / k, (toolbarRect.Y - acrylicOrigin.Y) / k,
                            toolbarRect.Width / k, toolbarRect.Height / k, GraphicsUnit.Pixel, ia);
                    }
                }
                using (SolidBrush tint = new SolidBrush(Theme.Frost))
                    g.FillRectangle(tint, toolbarRect);
                g.Restore(st);
                using (Pen border = new Pen(Theme.BarBorder, 1f))
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
                if (acrylic != null) acrylic.Dispose();
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
        private Thread hookThread;
        private uint hookThreadId;
        private bool busy;
        private bool swallowKeyUp;
        private bool swallowSKeyUp;
        private bool snapshotPassed;
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
            menu.Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
            menu.Items.Add("Capture bar  (PrtScn)", null, delegate { PostDelayed(CaptureMode.Overlay); });
            menu.Items.Add("Monitor  (Ctrl+PrtScn)", null, delegate { PostDelayed(CaptureMode.Monitor); });
            menu.Items.Add("Window  (Alt+PrtScn)", null, delegate { PostDelayed(CaptureMode.Window); });
            menu.Items.Add("All monitors  (Shift+PrtScn)", null, delegate { PostDelayed(CaptureMode.AllMonitors); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem adjustItem = new ToolStripMenuItem("Edit (adjust + annotate)");
            adjustItem.Click += delegate { Settings.Edit = !Settings.Edit; Settings.Save(); };
            ToolStripMenuItem followItem = new ToolStripMenuItem("Follow mouse (no picking)");
            followItem.Click += delegate { Settings.MonitorFollow = !Settings.MonitorFollow; Settings.Save(); };
            ToolStripMenuItem saveItem = new ToolStripMenuItem("Save to file");
            saveItem.Click += delegate { Settings.ToggleSave(); };
            ToolStripMenuItem copyItem = new ToolStripMenuItem("Copy to clipboard");
            copyItem.Click += delegate { Settings.ToggleCopy(); };
            menu.Items.Add(adjustItem);
            menu.Items.Add(saveItem);
            menu.Items.Add(copyItem);
            menu.Items.Add(followItem);

            ToolStripMenuItem tolItem = new ToolStripMenuItem("Click tolerance");
            int[] tolChoices = new int[] { 0, 5, 12, 25, 50 };
            foreach (int choice in tolChoices)
            {
                int value = choice;
                ToolStripMenuItem item = new ToolStripMenuItem(value == 0 ? "Off" : value + " px");
                item.Tag = value;
                item.Click += delegate
                {
                    Settings.ClickTolerance = value;
                    Settings.Save();
                };
                tolItem.DropDownItems.Add(item);
            }
            tolItem.DropDownDirection = ToolStripDropDownDirection.Right;
            tolItem.DropDown.Opened += delegate { FrostBackdrop.Round(tolItem.DropDown.Handle); };
            menu.Opened += delegate { FrostBackdrop.Round(menu.Handle); };
            menu.Closed += delegate { FrostBackdrop.Release(); };
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
                ApplyMenuTheme(menu);
                adjustItem.Checked = Settings.Adjust;
                saveItem.Checked = Settings.SaveFile;
                copyItem.Checked = Settings.CopyClipboard;
                followItem.Checked = Settings.MonitorFollow;
                foreach (ToolStripMenuItem t in tolItem.DropDownItems)
                {
                    t.Checked = ((int)t.Tag == Settings.ClickTolerance);
                }
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Check for updates", null, delegate { CheckForUpdates(); });
            string uninstaller = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall.exe");
            if (File.Exists(uninstaller))
            {
                menu.Items.Add("Uninstall...", null, delegate
                {
                    if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        ProcessStartInfo u = new ProcessStartInfo(uninstaller, "--uninstall");
                        u.UseShellExecute = true;
                        Process.Start(u);
                    }
                    catch (Exception) { }
                });
            }
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Screenshot - PrtScn to capture";
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                Diag.Log("tray click: " + e.Button + ", busy=" + busy);
                if (e.Button == MouseButtons.Left) PostDelayed(CaptureMode.Overlay);
                else if (e.Button == MouseButtons.Right) ShowTrayMenu(menu);
            };

            hookProc = HookCallback;
            StartHookThread();
            CleanOldLogs();
            MigrateStartup();
            Thread wd = new Thread(Watchdog);
            wd.IsBackground = true;
            wd.Start();
            sync.BeginInvoke(new Action(WarmUp));
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Could not install the PrtScn hook. Use the tray icon to capture instead.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Windows silently drops a low-level keyboard hook that was ever too slow, after which no shortcut works
        // (tray icon there, nothing opens). Every few seconds send a harmless key (an unassigned virtual key)
        // and check the hook saw it; if not, install the hook again.
        private int hookEvents;
        private void CheckHook()
        {
            try
            {
                int before = hookEvents;
                Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                Thread.Sleep(400);
                if (hookEvents != before) return;
                Diag.Log("keyboard hook stopped receiving keys: installing it again");
                if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
                Thread old = hookThread;
                if (old != null) old.Join(1000);
                Array.Clear(keys, 0, keys.Length);
                swallowSKeyUp = false;
                StartHookThread();
            }
            catch (Exception ex) { Diag.Log("hook check failed: " + ex.Message); }
        }

        // If the UI thread stops answering for a while (the symptom: tray icon there, nothing opens), start a
        // fresh copy and quit this one.
        private void Watchdog()
        {
            for (;;)
            {
                Thread.Sleep(4000);
                CheckHook();
                ManualResetEvent answered = new ManualResetEvent(false);
                Stopwatch lat = Stopwatch.StartNew();
                try { sync.BeginInvoke(new Action(delegate { answered.Set(); })); }
                catch (Exception) { return; }
                bool ok = answered.WaitOne(15000);
                if (ok && lat.ElapsedMilliseconds > 700) Diag.Log("UI thread was slow: " + lat.ElapsedMilliseconds + " ms to answer");
                if (!ok)
                {
                    Diag.Log("UI thread did not answer for 15 s: restarting the app");
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                            "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + Application.ExecutablePath + "\"");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                    Environment.Exit(1);
                }
            }
        }

        // ---- updates -----------------------------------------------------------------------

        private const string ReleaseApi = "https://api.github.com/repos/Shin0Impact/screenshot-tool/releases/tags/latest";

        // The commit this build was made from (CI embeds it); null for a local build.
        private static string OwnBuild()
        {
            try
            {
                using (Stream st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildId"))
                {
                    if (st == null) return null;
                    using (StreamReader r = new StreamReader(st)) return r.ReadToEnd().Trim();
                }
            }
            catch (Exception) { return null; }
        }

        private void CheckForUpdates()
        {
            try { tray.ShowBalloonTip(2000, "Screenshot", "Checking for updates...", ToolTipIcon.Info); } catch (Exception) { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string json = null, err = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;   // TLS 1.2
                    System.Net.HttpWebRequest rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(ReleaseApi);
                    rq.UserAgent = "ScreenshotTool";
                    rq.Timeout = 15000;
                    using (System.Net.WebResponse resp = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                        json = sr.ReadToEnd();
                }
                catch (Exception ex) { err = ex.Message; }
                sync.BeginInvoke(new Action(delegate { ShowUpdateResult(json, err); }));
            });
        }

        private void ShowUpdateResult(string json, string err)
        {
            if (json == null)
            {
                MessageBox.Show("Could not check for updates:\r\n" + err, "Screenshot",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Match commit = Regex.Match(json, "\"target_commitish\"\\s*:\\s*\"([0-9a-fA-F]{7,40})\"");
            Match url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+Screenshot-Installer\\.exe)\"");
            string own = OwnBuild();
            if (commit.Success && own != null && string.Equals(commit.Groups[1].Value, own, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You have the latest version.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!url.Success)
            {
                MessageBox.Show("Could not find the latest installer.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string question = own == null
                ? "This is a local build, so I can't tell if it is current. Download the latest installer?"
                : "A newer version is available. Download the installer and update now?";
            if (MessageBox.Show(question, "Screenshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string link = url.Groups[1].Value;
            string tmpDir = Path.Combine(Path.GetTempPath(), "ScreenshotUpdate");
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch (Exception) { }
            try { Directory.CreateDirectory(tmpDir); } catch (Exception) { }
            string target = Path.Combine(tmpDir, "Screenshot-Installer.exe");
            try { tray.ShowBalloonTip(3000, "Screenshot", "Downloading the update...", ToolTipIcon.Info); } catch (Exception) { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string dlErr = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Headers.Add("User-Agent", "ScreenshotTool");
                        wc.DownloadFile(link, target);
                    }
                }
                catch (Exception ex)
                {
                    dlErr = ex.Message;
                    for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException) dlErr += "\r\n" + inner.Message;
                }
                sync.BeginInvoke(new Action(delegate
                {
                    if (dlErr != null)
                    {
                        MessageBox.Show("Could not download the update:\r\n" + dlErr, "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(target);
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not start the installer:\r\n" + ex.Message, "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }));
            });
        }

        // The menu opens with its left edge where you clicked, growing to the right and upward from the
        // taskbar. It only slides left as far as needed to stay on screen.
        private void ShowTrayMenu(ContextMenuStrip menu)
        {
            try
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                ApplyMenuTheme(menu);
                Size sz = menu.GetPreferredSize(Size.Empty);
                Rectangle wa = Screen.FromPoint(new Point(pt.X, pt.Y)).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(pt.X, wa.Right - sz.Width));
                int bottom = Math.Min(pt.Y, wa.Bottom);
                int y = Math.Max(wa.Top, bottom - sz.Height);
                FrostBackdrop.Capture(Screen.FromPoint(new Point(pt.X, pt.Y)).Bounds);   // before the menu covers it
                Native.SetForegroundWindow(sync.Handle);   // so the menu closes when you click elsewhere
                menu.Show(new Point(x, y));
            }
            catch (Exception) { }
        }

        // ---- keyboard hook -------------------------------------------------------------

        // 1 = elevated, 0 = not elevated, -1 = could not tell (access denied: protected or higher integrity).
        private static int IsElevated(uint pid) { return TokenFlag(pid, 20); }      // TokenElevation
        private static int HasUiAccess(uint pid) { return TokenFlag(pid, 26); }    // TokenUIAccess

        private static int TokenFlag(uint pid, int tokenClass)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return -1;
            try
            {
                IntPtr tok;
                if (!Native.OpenProcessToken(h, 0x0008, out tok)) return -1;   // TOKEN_QUERY
                try
                {
                    int elev, ret;
                    if (!Native.GetTokenInformation(tok, tokenClass, out elev, 4, out ret)) return -1;
                    return elev != 0 ? 1 : 0;
                }
                finally { Native.CloseHandle(tok); }
            }
            finally { Native.CloseHandle(h); }
        }

        // The hook lives on its own thread with its own message loop. A low-level hook that sits on
        // the UI thread is silently dropped by Windows whenever that thread is busy for too long
        // (grabbing the screen, saving a PNG, filling the clipboard), which is when Windows' own
        // Snipping Tool used to take over again.
        private void StartHookThread()
        {
            ManualResetEvent ready = new ManualResetEvent(false);
            hookThread = new Thread(delegate()
            {
                hookThreadId = Native.GetCurrentThreadId();
                using (Process p = Process.GetCurrentProcess())
                using (ProcessModule m = p.MainModule)
                {
                    hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
                        Native.GetModuleHandle(m.ModuleName), 0);
                }
                ready.Set();
                Diag.Log("keyboard hook installed: " + (hookHandle != IntPtr.Zero) + ", elevated=" + IsElevated((uint)Process.GetCurrentProcess().Id)
                    + ", uiAccess=" + HasUiAccess((uint)Process.GetCurrentProcess().Id));
                if (hookHandle == IntPtr.Zero) return;
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                Diag.Log("keyboard hook thread ended");
                Native.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            });
            hookThread.IsBackground = true;
            hookThread.Name = "keyboard hook";
            hookThread.Start();
            ready.WaitOne(3000);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                hookEvents++;
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
                    bool win = keys[Native.VK_LWIN] || keys[Native.VK_RWIN];
                    bool shift = keys[0xA0] || keys[0xA1] || keys[Native.VK_SHIFT];
                    bool ctrl = keys[0xA2] || keys[0xA3] || keys[Native.VK_CONTROL];
                    bool alt = keys[0xA4] || keys[0xA5] || keys[Native.VK_MENU];

                    if (vk == Native.VK_S)
                    {
                        // Win+Shift+S: take over Windows' own snip shortcut.
                        if (down && win && shift && !ctrl && !alt)
                        {
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
                catch (Exception) { }
            }
            return Native.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        // Older builds wrote diagnostic logs; remove any that are still lying around.
        private static void CleanOldLogs()
        {
            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot-tool.log")); } catch (Exception) { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "screenshot-setup.log")); } catch (Exception) { }
        }

        private void Post(CaptureMode mode)
        {
            // Never do real work inside the hook; hand it to the UI thread.
            DateTime queued = DateTime.UtcNow;
            sync.BeginInvoke(new Action(delegate
            {
                Diag.Log("hotkey " + mode + ", UI thread picked it up after " + (int)(DateTime.UtcNow - queued).TotalMilliseconds + " ms; foreground: " + ForegroundInfo());
                Capture(mode);
            }));
        }

        private static string ForegroundInfo()
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName + " (elevated=" + IsElevated(pid) + ")";
            }
            catch (Exception) { return "?"; }
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
            if (busy)
            {
                // Pressing the shortcut again while the overlay is open starts a fresh capture.
                SelectionForm o = openOverlay;
                AnnotateForm a = openAnnotate;
                Diag.Log("busy: overlay=" + (o != null && !o.IsDisposed) + (o != null && !o.IsDisposed ? " visible=" + o.Visible : "")
                    + ", annotate=" + (a != null && !a.IsDisposed) + ", busy for " + (int)(DateTime.UtcNow - busySince).TotalSeconds + " s");
                if (o != null && !o.IsDisposed)
                {
                    if (o.NeverShown) { Diag.Log("overlay never showed: forcing it closed"); o.ForceClose(); }
                    else if (o.Visible) o.RestartFresh();
                    else { pendingPresses++; Diag.Log("queued press (overlay not on screen yet): " + pendingPresses); }
                    return;
                }
                if (a != null && !a.IsDisposed)
                {
                    try { a.WindowState = FormWindowState.Normal; Fg.Take(a.Handle); a.Activate(); } catch (Exception) { }
                    return;
                }
                // between two overlays: remember the press, the next overlay picks it up
                if ((DateTime.UtcNow - busySince).TotalSeconds < 8) { pendingPresses++; Diag.Log("queued press (between overlays): " + pendingPresses); return; }
                Diag.Log("busy flag was stale: starting over");
            }
            busy = true;
            busySince = DateTime.UtcNow;
            pendingPresses = 0;
            Diag.Log("capture start, mode " + mode);
            try
            {
                Bitmap result = null;
                Rectangle toastMonitor = CurrentMonitor();

                // "Follow mouse": monitor / window capture takes whatever is under the mouse, no picking.
                Rectangle direct = Rectangle.Empty;
                if (Settings.MonitorFollow && mode == CaptureMode.Monitor) direct = toastMonitor;
                else if (Settings.MonitorFollow && mode == CaptureMode.Window) direct = WindowUnderCursor();

                if (!direct.IsEmpty)
                {
                    result = Grab(direct, PixelFormat.Format24bppRgb);
                }
                else if (mode == CaptureMode.AllMonitors)
                {
                    result = GrabAllMonitors();
                }
                else
                {
                    int startMode = (mode == CaptureMode.Window) ? 1
                        : (mode == CaptureMode.Monitor) ? 2
                        : (mode == CaptureMode.Overlay ? Settings.LastMode : 0);
                    if (Settings.MonitorFollow && mode == CaptureMode.Overlay) startMode = 0;
                    for (;;)
                    {
                        OverlayResult o = RunOverlay(startMode);
                        if (o.Restart)
                        {
                            // The shortcut was pressed again: close and freeze the screen afresh.
                            startMode = o.RestartMode;
                            Countdown(o.RestartDelay, toastMonitor);
                            continue;
                        }
                        result = o.Image;
                        break;
                    }
                }

                if (result != null && Settings.Annotate)
                {
                    Bitmap edited = null;
                    try
                    {
                        using (AnnotateForm af = new AnnotateForm(result, toastMonitor))
                        {
                            openAnnotate = af;
                            Diag.Log("annotate window opening");
                            try { af.ShowDialog(); }
                            finally { openAnnotate = null; Diag.Log("annotate window closed, accepted=" + af.Accepted); }
                            if (af.Accepted) edited = af.Result;
                        }
                    }
                    finally
                    {
                        result.Dispose();
                    }
                    result = edited;
                }

                if (result != null)
                {
                    // Writing the file / filling the clipboard can stall (antivirus, clipboard managers), so it
                    // runs on its own thread and never freezes the app.
                    Bitmap toSave = result;
                    result = null;
                    SaveAsync(toSave, toastMonitor);
                }
            }
            catch (Exception ex)
            {
                Diag.Log("capture error: " + ex);
                try
                {
                    tray.ShowBalloonTip(3000, "Screenshot failed", ex.Message, ToolTipIcon.Error);
                }
                catch (Exception) { }
            }
            finally
            {
                busy = false;
                pendingPresses = 0;
                Diag.Log("capture end (" + (int)(DateTime.UtcNow - busySince).TotalMilliseconds + " ms)");
            }
        }

        // Shows "Capturing in N..." and waits, keeping the UI responsive. The popup is gone
        // before this returns, so it never ends up in the screenshot.
        private static SelectionForm openOverlay;
        private static int pendingPresses;   // shortcut presses that arrived while no overlay could take them
        private static AnnotateForm openAnnotate;
        private DateTime busySince = DateTime.UtcNow;

        private static void Countdown(int seconds, Rectangle monitor)
        {
            if (seconds <= 0)
            {
                // quick restart: just let the old overlay disappear before the screen is grabbed again
                DateTime quick = DateTime.UtcNow.AddMilliseconds(120);
                while (DateTime.UtcNow < quick) { Application.DoEvents(); Thread.Sleep(10); }
                return;
            }
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

        private static Rectangle WindowUnderCursor()
        {
            Native.POINT pt;
            Native.GetCursorPos(out pt);
            Rectangle vs = VirtualScreen();
            foreach (Rectangle w in CollectWindows())
            {
                if (w.Contains(pt.X, pt.Y))
                {
                    Rectangle r = Rectangle.Intersect(w, vs);
                    if (r.Width > 0 && r.Height > 0) return r;
                }
            }
            return Rectangle.Empty;
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

            // If Windows' own snipping overlay opened too, close it so ours is the only one.
            try
            {
                foreach (Process sp in Process.GetProcessesByName("ScreenClippingHost"))
                {
                    try { sp.Kill(); } catch (Exception) { }
                    sp.Dispose();
                }
            }
            catch (Exception) { }

            OverlayResult res = new OverlayResult();
            Stopwatch ov = Stopwatch.StartNew();
            using (Bitmap desktop = Grab(vs, PixelFormat.Format32bppRgb))
            using (SelectionForm form = new SelectionForm(desktop, vs, windows, MonitorRects(), mon, startMode))
            {
                Diag.Log("overlay prepared in " + ov.ElapsedMilliseconds + " ms (" + windows.Count + " windows), showing");
                openOverlay = form;
                form.Shown += delegate
                {
                    // presses that came in while this overlay was being prepared are served by it: it is already fresh
                    if (pendingPresses > 0) { Diag.Log("presses folded into this overlay: " + pendingPresses); pendingPresses = 0; }
                };
                try { form.ShowDialog(); }
                finally { openOverlay = null; }
                Diag.Log("overlay closed after " + ov.ElapsedMilliseconds + " ms: " + form.Outcome
                    + (form.CloseReason.Length > 0 ? " (" + form.CloseReason + ")" : ""));

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

        private void SaveAsync(Bitmap bmp, Rectangle toastMonitor)
        {
            Thread th = new Thread(delegate()
            {
                Stopwatch sv = Stopwatch.StartNew();
                try { Save(bmp, toastMonitor); Diag.Log("saved/copied in " + sv.ElapsedMilliseconds + " ms"); }
                catch (Exception ex) { Diag.Log("save error: " + ex.Message); }
                finally { bmp.Dispose(); }
            });
            th.SetApartmentState(ApartmentState.STA);   // the clipboard needs an STA thread
            th.IsBackground = false;
            th.Start();
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
            }

            // Feedback first, so it feels instant; the clipboard work follows.
            try
            {
                string verb = (path != null) ? "Saved" : "Copied";
                string msg = verb + "  " + bmp.Width + " x " + bmp.Height;
                sync.BeginInvoke(new Action(delegate
                {
                    Toast t = new Toast(msg, toastMonitor, 1400);
                    t.Show();
                    t.Refresh();
                }));
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

        // Starting with Windows uses a scheduled task that runs with the highest privileges, so the
        // (elevated) app starts at logon without a UAC prompt.
        private const string TaskName = "ScreenshotTool";

        private static int RunSchtasks(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception) { return -1; }
        }

        private static bool RunKeySet()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return k != null && k.GetValue(RunValueName) != null;
            }
            catch (Exception) { return false; }
        }

        private static void SetRunKey(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception) { }
        }

        private static bool IsStartupEnabled()
        {
            return RunKeySet() || RunSchtasks("/Query /TN " + TaskName) == 0;
        }

        // Older installs started at logon through a scheduled task, which cannot launch a UIAccess program.
        // Move them to the Run entry (started by the shell, like a double-click).
        private static void MigrateStartup()
        {
            try
            {
                if (HasUiAccess((uint)Process.GetCurrentProcess().Id) != 1) return;
                if (RunSchtasks("/Query /TN " + TaskName) != 0) return;
                SetRunKey(true);
                RunSchtasks("/Delete /TN " + TaskName + " /F");
            }
            catch (Exception) { }
        }

        private static string XmlEscape(string t)
        {
            return t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void ToggleStartup()
        {
            try
            {
                if (IsStartupEnabled())
                {
                    RunSchtasks("/Delete /TN " + TaskName + " /F");
                    SetRunKey(false);
                }
                else if (IsElevated((uint)Process.GetCurrentProcess().Id) != 1)
                {
                    SetRunKey(true);   // started by the shell at logon (works for the UIAccess build too)
                }
                else
                {
                    string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    // An elevated copy needs a highest-privilege task; a UIAccess copy runs as a normal user.
                    string runLevel = (IsElevated((uint)Process.GetCurrentProcess().Id) == 1) ? "HighestAvailable" : "LeastPrivilege";
                    string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>"
                        + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
                        + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + XmlEscape(user) + "</UserId></LogonTrigger></Triggers>"
                        + "<Principals><Principal id=\"Author\"><UserId>" + XmlEscape(user) + "</UserId>"
                        + "<LogonType>InteractiveToken</LogonType><RunLevel>" + runLevel + "</RunLevel></Principal></Principals>"
                        + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
                        + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                        + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
                        + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowHardTerminate>true</AllowHardTerminate></Settings>"
                        + "<Actions Context=\"Author\"><Exec><Command>" + XmlEscape(Application.ExecutablePath) + "</Command></Exec></Actions></Task>";
                    string tmp = Path.Combine(Path.GetTempPath(), "screenshot-tool-task.xml");
                    File.WriteAllText(tmp, xml, Encoding.Unicode);
                    int rc = RunSchtasks("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F");
                    try { File.Delete(tmp); } catch (Exception) { }
                    // drop the old non-elevated Run entry, if any
                    try
                    {
                        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                        {
                            if (k != null) k.DeleteValue(RunValueName, false);
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            startupItem.Checked = IsStartupEnabled();
        }

        // Neutral menu colours that follow the Windows light/dark setting.
        private static void ApplyMenuTheme(ContextMenuStrip menu)
        {
            Theme.Refresh();
            menu.Renderer = new CleanRenderer();
            Color bg = MenuColors.Back();
            Color fg = Theme.Light ? Color.FromArgb(25, 25, 25) : Color.FromArgb(240, 240, 240);
            menu.BackColor = bg;
            menu.ForeColor = fg;
            StyleItems(menu.Items, bg, fg);
        }

        private static void StyleItems(ToolStripItemCollection items, Color bg, Color fg)
        {
            foreach (ToolStripItem it in items)
            {
                it.BackColor = Color.Transparent;
                it.ForeColor = fg;
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems)
                {
                    mi.DropDown.BackColor = bg;
                    StyleItems(mi.DropDownItems, bg, fg);
                }
            }
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
            if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            tray.Visible = false;
            tray.Dispose();
            sync.Dispose();
            base.ExitThreadCore();
        }
    }

#if !UIA
    // Screenshot-Installer.exe is a launcher with two builds of the app embedded in it:
    //  * with administrator support: a UIAccess build, signed with a local certificate and installed in
    //    Program Files (one Windows permission prompt). UIAccess lets a normal-user app see keys typed
    //    into administrator windows (a game started as administrator).
    //  * without administrator support: the ordinary build in a folder of your choice, no prompt.
    // Running the installer again offers to add administrator support, update, start or uninstall.
    static class Setup
    {
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool";
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string ProgramFilesDir
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return pf.TrimEnd('\\');
            }
        }

        private static string AdminDefaultDir { get { return Path.Combine(ProgramFilesDir, "ScreenshotTool"); } }

        private static string UserDefaultDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\ScreenshotTool");
            }
        }

        private static string SetupLog { get { return Path.Combine(Path.GetTempPath(), "screenshot-setup.log"); } }

        // Where a mode was installed, from its Uninstall registry entry (null = not installed).
        private static string Registered(RegistryKey hive)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(UninstallKeyPath))
                {
                    if (k == null) return null;
                    string v = k.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            catch (Exception) { return null; }
        }

        private static string AdminDir() { return Registered(Registry.LocalMachine); }
        private static string UserDir() { return Registered(Registry.CurrentUser); }

        private static bool HasApp(string dir)
        {
            return dir != null && File.Exists(Path.Combine(dir, "Screenshot.exe"));
        }

        private static bool SameDir(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool IsInside(string dir, string parent)
        {
            try
            {
                return Path.GetFullPath(dir).TrimEnd('\\').StartsWith(parent.TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // A folder that is new, empty, or already holds this app; never the root of a drive.
        private static bool IsUsableDir(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                if (full.Length < 4 || Path.GetPathRoot(full).TrimEnd('\\').Length == full.Length) return false;
                if (Directory.Exists(full))
                {
                    bool mine = File.Exists(Path.Combine(full, "version.txt"));
                    if (!mine && Directory.GetFileSystemEntries(full).Length > 0) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static byte[] ReadEmbedded(string name)
        {
            using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                byte[] data = new byte[s.Length];
                int got = 0;
                while (got < data.Length)
                {
                    int n = s.Read(data, got, data.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                return data;
            }
        }

        private static string VersionOf(byte[] data)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool VersionMatches(string dir, string version)
        {
            try { return HasApp(dir) && File.ReadAllText(Path.Combine(dir, "version.txt")).Trim() == version; }
            catch (Exception) { return false; }
        }

        // The administrator build only starts while its signing certificate is trusted on this computer.
        private static bool CertTrusted()
        {
            try
            {
                foreach (System.Security.Cryptography.X509Certificates.StoreName name in new System.Security.Cryptography.X509Certificates.StoreName[] {
                    System.Security.Cryptography.X509Certificates.StoreName.Root,
                    System.Security.Cryptography.X509Certificates.StoreName.TrustedPublisher })
                {
                    System.Security.Cryptography.X509Certificates.X509Store st = new System.Security.Cryptography.X509Certificates.X509Store(
                        name, System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                    st.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                    bool found = false;
                    foreach (System.Security.Cryptography.X509Certificates.X509Certificate2 c in st.Certificates)
                        if (c.Subject == "CN=Screenshot Tool (local)") found = true;
                    st.Close();
                    if (!found) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool StartApp(string dir)
        {
            try
            {
                // Through the shell (like a double-click): a UIAccess exe cannot be started with a plain
                // CreateProcess call, which fails with "The requested operation requires elevation".
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(dir, "Screenshot.exe"));
                psi.UseShellExecute = true;
                psi.WorkingDirectory = dir;
                Process.Start(psi);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void KillOthers(int parentPid)
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id == parentPid) continue;
                    if (p.ProcessName.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }

        private static string Q(string s) { return s.Replace("'", "''"); }

        // Runs a PowerShell script (elevated, because this process is) and returns its exit code.
        private static int RunPowerShell(string script, out string output)
        {
            string exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            ProcessStartInfo psi = new ProcessStartInfo(exe,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(180000);
                output = o + e;
                return p.HasExited ? p.ExitCode : -1;
            }
        }

        // ---- administrator install (runs elevated, started with "runas" by the installer window) ----

        private static int InstallAdmin(byte[] data, string version, int parentPid, string dir, string oldDir)
        {
            string output = "";
            int code = 1;
            string tmp = Path.Combine(Path.GetTempPath(), "Screenshot-UIAccess-" + version + ".exe");
            try
            {
                KillOthers(parentPid);
                File.WriteAllBytes(tmp, data);
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                string script = @"
$ErrorActionPreference = 'Stop'
try {
  $src = '__SRC__'; $dir = '__DIR__'; $ver = '__VER__'; $user = '__USER__'; $launcher = '__LAUNCHER__'; $old = '__OLD__'
  $subject = 'CN=Screenshot Tool (local)'
  $target = Join-Path $dir 'Screenshot.exe'
  $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
  if (-not $cert) { $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(10) -KeyExportPolicy NonExportable }
  $cer = Join-Path $env:TEMP 'screenshot-tool-local.cer'
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
  Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
  Remove-Item $cer -Force
  if ($old -and ($old -ne $dir) -and (Test-Path $old)) { Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory -Force $dir | Out-Null
  Copy-Item $src $target -Force
  $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -HashAlgorithm SHA256
  if ($sig.Status -ne 'Valid') { throw ('Signing failed: ' + $sig.Status + ' ' + $sig.StatusMessage) }
  Set-Content -Path (Join-Path $dir 'version.txt') -Value $ver -Encoding Ascii
  $unin = Join-Path $dir 'Uninstall.exe'
  Copy-Item $launcher $unin -Force
  $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool'
  New-Item -Path $key -Force | Out-Null
  Set-ItemProperty $key DisplayName 'Screenshot'
  Set-ItemProperty $key Publisher 'Shin0Impact'
  Set-ItemProperty $key DisplayVersion $ver
  Set-ItemProperty $key DisplayIcon $target
  Set-ItemProperty $key InstallLocation $dir
  Set-ItemProperty $key UninstallString ('""' + $unin + '"" --uninstall')
  Set-ItemProperty $key NoModify 1 -Type DWord
  Set-ItemProperty $key NoRepair 1 -Type DWord
  Unregister-ScheduledTask -TaskName 'ScreenshotTool' -Confirm:$false -ErrorAction SilentlyContinue
  Write-Output 'OK'
} catch {
  Write-Output ('FAILED: ' + $_.Exception.Message)
  exit 1
}
".Replace("__SRC__", Q(tmp)).Replace("__DIR__", Q(dir)).Replace("__VER__", Q(version)).Replace("__USER__", Q(user)).Replace("__LAUNCHER__", Q(Application.ExecutablePath)).Replace("__OLD__", Q(oldDir ?? ""));
                code = RunPowerShell(script, out output);
            }
            catch (Exception ex)
            {
                output += "\r\nFAILED: " + ex.Message;
                code = 1;
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }
            try { File.WriteAllText(SetupLog, output); } catch (Exception) { }
            return code;
        }

        private static int UninstallAdmin(int parentPid)
        {
            string output = "";
            try
            {
                KillOthers(parentPid);
                string target = AdminDir();
                if (target == null || !IsInside(target, ProgramFilesDir)) target = AdminDefaultDir;
                string script = @"
$ErrorActionPreference = 'Continue'
$subject = 'CN=Screenshot Tool (local)'
Unregister-ScheduledTask -TaskName 'ScreenshotTool' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool' -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path '__DIR__') { Remove-Item '__DIR__' -Recurse -Force }
foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher') {
  Get-ChildItem $store | Where-Object { $_.Subject -eq $subject } | Remove-Item -Force
}
Write-Output 'OK'
".Replace("__DIR__", Q(target));
                int code = RunPowerShell(script, out output);
                try { File.WriteAllText(SetupLog, output); } catch (Exception) { }
                return code;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(SetupLog, ex.ToString()); } catch (Exception) { }
                return 1;
            }
        }

        // ---- standard install (no administrator rights needed) ----

        // Returns null on success, otherwise a message.
        private static string InstallUser(byte[] data, string version, string dir, string oldDir)
        {
            try
            {
                KillOthers(0);
                if (oldDir != null && !SameDir(oldDir, dir) && Directory.Exists(oldDir)) TryDeleteDir(oldDir);
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, "Screenshot.exe");
                File.WriteAllBytes(exe, data);
                File.Copy(Application.ExecutablePath, Path.Combine(dir, "Uninstall.exe"), true);
                File.WriteAllText(Path.Combine(dir, "version.txt"), version);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
                {
                    k.SetValue("DisplayName", "Screenshot");
                    k.SetValue("Publisher", "Shin0Impact");
                    k.SetValue("DisplayVersion", version);
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + Path.Combine(dir, "Uninstall.exe") + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                using (RegistryKey r = Registry.CurrentUser.CreateSubKey(RunPath))
                    r.SetValue("ScreenshotTool", "\"" + exe + "\"");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static void TryDeleteDir(string dir)
        {
            for (int i = 0; i < 8; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
                catch (Exception) { Thread.Sleep(300); }
            }
        }

        private static void UninstallUser(string dir)
        {
            KillOthers(0);
            try
            {
                using (RegistryKey r = Registry.CurrentUser.OpenSubKey(RunPath, true))
                    if (r != null) r.DeleteValue("ScreenshotTool", false);
            }
            catch (Exception) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch (Exception) { }
            if (dir != null) TryDeleteDir(dir);
        }

        // ---- plumbing ----

        // Starts this same exe elevated with the given argument and waits (keeping a small window alive).
        // Returns -2 if the user said no.
        private static int RunElevated(string argument, string dir, string busyText)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath,
                argument + " " + Process.GetCurrentProcess().Id + (dir != null ? " \"" + dir + "\"" : ""));
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            Form busy = null;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    busy = BusyWindow(busyText);
                    while (!p.WaitForExit(40)) Application.DoEvents();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return -2; }
            finally { if (busy != null) { busy.Close(); busy.Dispose(); } }
        }

        private static Form BusyWindow(string text)
        {
            Form f = new Form();
            f.Text = "Screenshot";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.ControlBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = SystemFonts.MessageBoxFont;
            f.ClientSize = new Size(420, 96);
            f.TopMost = true;
            Label l = new Label();
            l.SetBounds(16, 14, 388, 36);
            l.Text = text;
            f.Controls.Add(l);
            ProgressBar pb = new ProgressBar();
            pb.SetBounds(16, 58, 388, 18);
            pb.Style = ProgressBarStyle.Marquee;
            pb.MarqueeAnimationSpeed = 30;
            f.Controls.Add(pb);
            f.Show();
            f.Refresh();
            return f;
        }

        private static string ReadSetupLog()
        {
            string detail = "";
            try { detail = File.ReadAllText(SetupLog); } catch (Exception) { }
            try { File.Delete(SetupLog); } catch (Exception) { }
            return detail;
        }

        // ---- the installer window ----

        private class InstallerForm : Form
        {
            private readonly TextBox path = new TextBox();
            private readonly Label problem = new Label();
            private readonly RadioButton adminRadio = new RadioButton();
            private readonly RadioButton userRadio = new RadioButton();
            public string Action = "close";
            public string Chosen = "";

            private void AddButton(string text, string action, int x, int y, int w)
            {
                Button b = new Button();
                b.SetBounds(x, y, w, 32);
                b.Text = text;
                b.Click += delegate { Action = action; DialogResult = DialogResult.OK; };
                Controls.Add(b);
            }

            public InstallerForm(string adminDir, string userDir, bool adminCurrent, bool userCurrent)
            {
                Text = "Screenshot installer";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                Font = SystemFonts.MessageBoxFont;
                TopMost = true;

                Label head = new Label();
                head.SetBounds(16, 14, 668, 80);
                Controls.Add(head);

                if (adminDir == null && userDir == null)
                {
                    ClientSize = new Size(580, 330);
                    head.Text = "Install Screenshot. Administrator support lets the hotkeys work over games and other "
                        + "programs that run as administrator. Windows asks for permission once; after that the app "
                        + "starts normally, with no prompts.";

                    adminRadio.SetBounds(16, 96, 548, 22);
                    adminRadio.Text = "With administrator support (asks Windows for permission once)";
                    adminRadio.Checked = true;
                    adminRadio.CheckedChanged += delegate { SwitchDefault(); };
                    Controls.Add(adminRadio);

                    userRadio.SetBounds(16, 122, 548, 22);
                    userRadio.Text = "Without administrator support (no permission needed)";
                    userRadio.CheckedChanged += delegate { SwitchDefault(); };
                    Controls.Add(userRadio);

                    Label where = new Label();
                    where.SetBounds(16, 160, 548, 20);
                    where.Text = "Install folder:";
                    Controls.Add(where);

                    path.SetBounds(16, 184, 456, 24);
                    path.Text = AdminDefaultDir;
                    Controls.Add(path);

                    Button browse = new Button();
                    browse.SetBounds(480, 182, 84, 28);
                    browse.Text = "Browse...";
                    browse.Click += delegate
                    {
                        using (FolderBrowserDialog fb = new FolderBrowserDialog())
                        {
                            fb.Description = "Choose the install folder.";
                            fb.ShowNewFolderButton = true;
                            try { if (Directory.Exists(path.Text)) fb.SelectedPath = path.Text; } catch (Exception) { }
                            if (fb.ShowDialog(this) == DialogResult.OK) { path.Text = fb.SelectedPath; problem.Text = ""; }
                        }
                    };
                    Controls.Add(browse);

                    problem.SetBounds(16, 216, 548, 40);
                    problem.ForeColor = Color.Firebrick;
                    Controls.Add(problem);

                    Button install = new Button();
                    install.SetBounds(16, 280, 120, 32);
                    install.Text = "Install";
                    install.Click += delegate { TryInstall(); };
                    Controls.Add(install);

                    Button cancel = new Button();
                    cancel.SetBounds(444, 280, 120, 32);
                    cancel.Text = "Cancel";
                    cancel.Click += delegate { Action = "close"; DialogResult = DialogResult.Cancel; };
                    Controls.Add(cancel);

                    AcceptButton = install;
                    CancelButton = cancel;
                    return;
                }

                ClientSize = new Size(700, 190);
                bool admin = adminDir != null;
                bool current = admin ? adminCurrent : userCurrent;
                head.Text = admin
                    ? "Screenshot is installed with administrator support."
                    : "Screenshot is installed without administrator support. Its hotkeys do not work over "
                        + "programs that run as administrator.";
                if (!current) head.Text += "\r\n\r\nThe installed copy needs an update or repair.";

                // Fixed slots so each button is always in the same place: [Start/Update] [administrator
                // support, in the middle] [Uninstall] [Close].
                int bw = 700;
                if (!current) AddButton("Update", "update", 16, 120, 150);
                else AddButton("Start Screenshot", "start", 16, 120, 150);
                if (admin) AddButton("Remove administrator support", "removeadmin", (bw - 250) / 2, 120, 250);
                else AddButton("Add administrator support", "addadmin", (bw - 250) / 2, 120, 250);
                AddButton("Uninstall", "uninstall", 482, 120, 96);
                Button close = new Button();
                close.SetBounds(588, 120, 96, 32);
                close.Text = "Close";
                close.Click += delegate { Action = "close"; DialogResult = DialogResult.Cancel; };
                Controls.Add(close);
                CancelButton = close;
            }

            private void SwitchDefault()
            {
                string t = path.Text.Trim();
                if (adminRadio.Checked && SameDir(t, UserDefaultDir)) path.Text = AdminDefaultDir;
                else if (userRadio.Checked && SameDir(t, AdminDefaultDir)) path.Text = UserDefaultDir;
            }

            private void TryInstall()
            {
                string p = path.Text.Trim();
                bool admin = adminRadio.Checked;
                if (p.Length == 0 || !IsUsableDir(p))
                {
                    problem.Text = "Please use a new or empty folder.";
                    return;
                }
                if (admin && !IsInside(p, ProgramFilesDir))
                {
                    problem.Text = "With administrator support the folder must be inside " + ProgramFilesDir
                        + " (for example " + AdminDefaultDir + "). Windows only trusts programs there.";
                    return;
                }
                if (!admin && IsInside(p, ProgramFilesDir))
                {
                    problem.Text = "Without administrator support please choose a folder outside Program Files "
                        + "(for example " + UserDefaultDir + ").";
                    return;
                }
                Chosen = Path.GetFullPath(p).TrimEnd('\\');
                Action = admin ? "install-admin" : "install-user";
                DialogResult = DialogResult.OK;
            }
        }

        // ---- uninstall ----

        private static void DoUninstall()
        {
            string adminDir = AdminDir();
            string userDir = UserDir();
            string self = Application.ExecutablePath;
            string copy = Path.Combine(Path.GetTempPath(), "ScreenUninstall.exe");
            if ((adminDir != null && IsInside(self, adminDir)) || (userDir != null && IsInside(self, userDir)))
            {
                // Running from a folder that is about to be deleted: continue from a temporary copy.
                try
                {
                    File.Copy(self, copy, true);
                    ProcessStartInfo again = new ProcessStartInfo(copy, "--uninstall");
                    again.UseShellExecute = true;
                    Process.Start(again);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not start the uninstaller:\r\n" + ex.Message, "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            bool ok = true;
            bool cancelled = false;
            if (adminDir != null)
            {
                int rc = RunElevated("--uninstall-elevated", null, "Removing Screenshot... this can take a few seconds.");
                try { File.Delete(SetupLog); } catch (Exception) { }
                if (rc == -2) cancelled = true;
                else if (rc != 0) ok = false;
            }
            if (!cancelled && userDir != null) UninstallUser(userDir);
            if (!cancelled)
            {
                try
                {
                    using (RegistryKey r = Registry.CurrentUser.OpenSubKey(RunPath, true))
                        if (r != null) r.DeleteValue("ScreenshotTool", false);
                }
                catch (Exception) { }
            }

            MessageBox.Show(cancelled ? "Nothing was removed (Windows permission was declined)."
                : ok ? "Screenshot was removed." : "Could not remove everything.",
                "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (string.Equals(self, copy, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ProcessStartInfo del = new ProcessStartInfo("cmd.exe",
                        "/c ping -n 3 127.0.0.1 >nul & del \"" + copy + "\"");
                    del.CreateNoWindow = true;
                    del.UseShellExecute = false;
                    Process.Start(del);
                }
                catch (Exception) { }
            }
        }

        // Returns true when the app should simply start in this process (built without the embedded
        // builds, e.g. with build.bat).
        public static bool Handle(string[] args)
        {
            byte[] adminData = ReadEmbedded("UIAccessBuild.exe");
            byte[] userData = ReadEmbedded("StandardBuild.exe");
            if (adminData == null || userData == null) return true;
            string adminVer = VersionOf(adminData);
            string userVer = VersionOf(userData);
            string cmd = args.Length > 0 ? args[0] : "";
            int parent = 0;
            if (args.Length > 1) int.TryParse(args[1], out parent);

            if (cmd == "--install-elevated")
            {
                string old = AdminDir();
                string dir = args.Length > 2 ? args[2] : AdminDefaultDir;
                Environment.ExitCode = InstallAdmin(adminData, adminVer, parent, dir, old);
                return false;
            }
            if (cmd == "--uninstall-elevated") { Environment.ExitCode = UninstallAdmin(parent); return false; }

            Application.EnableVisualStyles();
            bool asUninstaller = string.Equals(Path.GetFileName(Application.ExecutablePath), "Uninstall.exe",
                StringComparison.OrdinalIgnoreCase);
            if (asUninstaller && cmd == "")
            {
                if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                cmd = "--uninstall";
            }
            if (cmd == "--uninstall") { DoUninstall(); return false; }

            for (;;)
            {
                string adminDir = HasApp(AdminDir()) ? AdminDir() : null;
                string userDir = HasApp(UserDir()) ? UserDir() : null;
                if (adminDir != null) userDir = null;   // the administrator install wins if both exist
                bool adminCurrent = adminDir != null && VersionMatches(adminDir, adminVer) && CertTrusted();
                bool userCurrent = userDir != null && VersionMatches(userDir, userVer);

                string action;
                string chosen;
                using (InstallerForm form = new InstallerForm(adminDir, userDir, adminCurrent, userCurrent))
                {
                    form.ShowDialog();
                    action = form.Action;
                    chosen = form.Chosen;
                }

                if (action == "close") return false;
                if (action == "start")
                {
                    if (StartApp(adminDir != null ? adminDir : userDir)) return false;
                    MessageBox.Show("Could not start Screenshot.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    continue;
                }
                if (action == "uninstall")
                {
                    if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                    DoUninstall();
                    return false;
                }

                if (action == "removeadmin")
                {
                    if (MessageBox.Show("Remove administrator support? Screenshot will keep working, installed for you "
                        + "only, but its hotkeys will stop working over programs that run as administrator.",
                        "Screenshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                    int rr = RunElevated("--uninstall-elevated", null, "Removing administrator support...");
                    if (rr == -2) continue;
                    try { File.Delete(SetupLog); } catch (Exception) { }
                    if (rr != 0 || HasApp(AdminDir()))
                    {
                        MessageBox.Show("Could not remove administrator support.", "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    string err = InstallUser(userData, userVer, UserDefaultDir, null);
                    if (err != null)
                    {
                        MessageBox.Show("Administrator support was removed, but the standard install did not finish.\r\n\r\n" + err,
                            "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (StartApp(UserDefaultDir)) return false;
                    continue;
                }

                bool admin = action == "install-admin" || action == "addadmin" || (action == "update" && adminDir != null);
                if (admin)
                {
                    string dir = action == "install-admin" ? chosen : (adminDir != null ? adminDir : AdminDefaultDir);
                    int rc = RunElevated("--install-elevated", dir, "Setting up Screenshot... the first time this can take up to a minute.");
                    if (rc == -2) continue;   // Windows permission refused: back to the window
                    string aDir = AdminDir();
                    if (HasApp(aDir) && VersionMatches(aDir, adminVer))
                    {
                        try { File.Delete(SetupLog); } catch (Exception) { }
                        string u = UserDir();
                        if (u != null) UninstallUser(u);   // moved to the administrator install
                        // Start at logon through the shell (Run key): a scheduled task cannot start a UIAccess program.
                        try
                        {
                            using (RegistryKey r = Registry.CurrentUser.CreateSubKey(RunPath))
                                r.SetValue("ScreenshotTool", "\"" + Path.Combine(aDir, "Screenshot.exe") + "\"");
                        }
                        catch (Exception) { }
                        if (StartApp(aDir)) return false;
                        MessageBox.Show("Installed, but could not start it. Try again from the installer.",
                            "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    MessageBox.Show("The setup did not finish.\r\n\r\n" + ReadSetupLog(), "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                string target = action == "install-user" ? chosen : userDir;
                string error = InstallUser(userData, userVer, target, UserDir());
                if (error != null)
                {
                    MessageBox.Show("The install did not finish.\r\n\r\n" + error, "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (StartApp(target)) return false;
            }
        }
    }
#endif

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if !UIA
            if (!Setup.Handle(args)) return;
#endif
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ScreenshotTool_SingleInstance", out created))
            {
                if (!created) { Diag.Log("second instance exited (already running)"); return; }
                Diag.Log("---- start, build " + (Environment.GetCommandLineArgs().Length) + " args, exe " + Application.ExecutablePath);
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Diag.Log("UNHANDLED (terminating=" + e.IsTerminating + "): " + e.ExceptionObject);
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Diag.Log("UI exception swallowed: " + e.Exception);   // must not kill the tray app
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}

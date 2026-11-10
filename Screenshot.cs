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

    class MenuColors : ProfessionalColorTable
    {
        public static Color Back() { return Theme.Light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(43, 43, 43); }
        private static Color Hot() { return Theme.Light ? Color.FromArgb(229, 229, 229) : Color.FromArgb(70, 70, 70); }
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
            CloseReason = "shortcut again";
            Outcome = OverlayOutcome.DelayRestart;
            DelaySeconds = 0;
            RestartMode = mode;
            Close();
        }

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
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) PostDelayed(CaptureMode.Overlay);
            };

            hookProc = HookCallback;
            StartHookThread();
            CleanOldLogs();
            sync.BeginInvoke(new Action(WarmUp));
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Could not install the PrtScn hook. Use the tray icon to capture instead.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
                if (hookHandle == IntPtr.Zero) return;
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
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
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotTool");
                if (Directory.Exists(d))
                {
                    try { File.Delete(Path.Combine(d, "screenshot-tool.log")); } catch (Exception) { }
                    try { Directory.Delete(d); } catch (Exception) { }
                }
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
            if (busy)
            {
                // Pressing the shortcut again while the overlay is open starts a fresh capture.
                if (openOverlay != null && !openOverlay.IsDisposed) openOverlay.RestartFresh();
                return;
            }
            busy = true;
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
                            af.ShowDialog();
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
                    using (result)
                    {
                        Save(result, toastMonitor);
                    }
                }
            }
            catch (Exception ex)
            {
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
        private static SelectionForm openOverlay;

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
            using (Bitmap desktop = Grab(vs, PixelFormat.Format32bppRgb))
            using (SelectionForm form = new SelectionForm(desktop, vs, windows, MonitorRects(), mon, startMode))
            {
                openOverlay = form;
                try { form.ShowDialog(); }
                finally { openOverlay = null; }

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

        private static bool IsStartupEnabled()
        {
            return RunSchtasks("/Query /TN " + TaskName) == 0;
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
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
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
                it.BackColor = bg;
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
    // The Screenshot.exe you download is a small launcher with the real app (a UIAccess build)
    // embedded in it. The first time it runs it sets that build up once (one Windows permission
    // prompt): a local certificate, the signed copy in Program Files and a start-at-logon task. A
    // UIAccess app, unlike an ordinary one, can see keys typed into elevated windows (a game started
    // as administrator) while still running as a normal user. After that, running the launcher just
    // starts the installed copy; a newer launcher updates it. "Screenshot.exe --uninstall" removes it.
    static class Setup
    {
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool";
        private static string installDirOverride;

        private static string ProgramFilesDir
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return pf;
            }
        }

        private static string DefaultDir { get { return Path.Combine(ProgramFilesDir, "ScreenshotTool"); } }

        // Where Setup put the app last time (null if never installed).
        private static string RegisteredDir()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                {
                    if (k == null) return null;
                    string v = k.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            catch (Exception) { return null; }
        }

        private static string InstallDir
        {
            get
            {
                if (installDirOverride != null) return installDirOverride;
                string r = RegisteredDir();
                return r != null ? r : DefaultDir;
            }
        }

        // A UIAccess app only starts from a protected folder, so the location has to be a folder inside
        // Program Files. It must not be Program Files itself: uninstalling deletes the whole folder.
        private static bool IsUnderProgramFiles(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                return full.StartsWith(ProgramFilesDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool IsValidDir(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                string pf = ProgramFilesDir.TrimEnd('\\');
                if (!full.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                if (Directory.Exists(full))
                {
                    bool mine = File.Exists(Path.Combine(full, "version.txt"));
                    if (!mine && Directory.GetFileSystemEntries(full).Length > 0) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string ChooseDir()
        {
            while (true)
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.Description = "Where should Screenshot be installed? It must be a folder inside Program Files "
                        + "so that Windows lets it see keys typed into administrator windows. The default is fine for most people.";
                    fb.SelectedPath = InstallDir;
                    fb.ShowNewFolderButton = true;
                    if (fb.ShowDialog() != DialogResult.OK) return null;
                    if (IsValidDir(fb.SelectedPath)) return Path.GetFullPath(fb.SelectedPath).TrimEnd('\\');
                }
                MessageBox.Show("Please choose a new or empty folder inside\r\n" + ProgramFilesDir
                    + "\r\n(for example " + DefaultDir + ").", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string InstalledExe { get { return Path.Combine(InstallDir, "Screenshot.exe"); } }
        private static string VersionFile { get { return Path.Combine(InstallDir, "version.txt"); } }
        private static string SetupLog { get { return Path.Combine(Path.GetTempPath(), "screenshot-setup.log"); } }

        private static byte[] ReadEmbedded()
        {
            using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("UIAccessBuild.exe"))
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

        private static bool IsCurrent(string version)
        {
            try
            {
                return File.Exists(InstalledExe) && File.Exists(VersionFile)
                    && File.ReadAllText(VersionFile).Trim() == version
                    && CertTrusted();
            }
            catch (Exception) { return false; }
        }

        // The installed copy only starts while its signing certificate is trusted on this computer.
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

        private static bool LaunchInstalled()
        {
            try
            {
                // Through the shell (like a double-click): a UIAccess exe cannot be started with a plain
                // CreateProcess call, which fails with "The requested operation requires elevation".
                ProcessStartInfo psi = new ProcessStartInfo(InstalledExe);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = InstallDir;
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

        // Runs elevated (started with "runas" by the launcher).
        private static int Install(byte[] data, string version, int parentPid, string oldDir)
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
  Register-ScheduledTask -TaskName 'ScreenshotTool' -Action (New-ScheduledTaskAction -Execute $target) -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $user) -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited) -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)) | Out-Null
  Write-Output 'OK'
} catch {
  Write-Output ('FAILED: ' + $_.Exception.Message)
  exit 1
}
".Replace("__SRC__", Q(tmp)).Replace("__DIR__", Q(InstallDir)).Replace("__VER__", Q(version)).Replace("__USER__", Q(user)).Replace("__LAUNCHER__", Q(Application.ExecutablePath)).Replace("__OLD__", Q(oldDir));
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

        private static int Uninstall(int parentPid)
        {
            string output = "";
            try
            {
                KillOthers(parentPid);
                string target = InstallDir;
                if (!IsUnderProgramFiles(target)) target = DefaultDir;
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

        // Starts this same exe elevated with the given argument and waits. Returns -2 if the user said no.
        private static int RunElevated(string argument, string dir)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath,
                argument + " " + Process.GetCurrentProcess().Id + (dir != null ? " \"" + dir + "\"" : ""));
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return -2; }
        }

        // Returns true when the app should simply start in this process (no embedded build, or the
        // user chose to skip the setup).
        public static bool Handle(string[] args)
        {
            byte[] data = ReadEmbedded();
            if (data == null) return true;   // built without the embedded app (build.bat): run directly
            string version = VersionOf(data);
            string cmd = args.Length > 0 ? args[0] : "";
            int parent = 0;
            if (args.Length > 1) int.TryParse(args[1], out parent);

            if (cmd == "--install-elevated")
            {
                string old = RegisteredDir();
                if (args.Length > 2) installDirOverride = args[2];
                Environment.ExitCode = Install(data, version, parent, old);
                return false;
            }
            if (cmd == "--uninstall-elevated") { Environment.ExitCode = Uninstall(parent); return false; }

            Application.EnableVisualStyles();
            if (cmd == "--uninstall")
            {
                string self = Application.ExecutablePath;
                string copy = Path.Combine(Path.GetTempPath(), "ScreenUninstall.exe");
                if (self.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Running from the folder that is about to be deleted: continue from a temporary copy.
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
                    return false;
                }
                int rc = RunElevated("--uninstall-elevated", null);
                try { File.Delete(SetupLog); } catch (Exception) { }
                MessageBox.Show(rc == 0 ? "Screenshot was removed." : "Could not remove it.",
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
                return false;
            }

            if (IsCurrent(version) && LaunchInstalled()) return false;

            DialogResult ask = MessageBox.Show(
                "Screenshot installer.\r\n\r\nScreenshot needs a one-time setup so its hotkeys also work over games and other programs " +
                "that run as administrator. Windows will ask for permission once; after that it starts " +
                "normally, with no prompts.\r\n\r\nYou will pick the install folder next (the default is fine).\r\n\r\n" +
                "Yes = install     No = run without it (hotkeys won't work over administrator windows)     Cancel = quit",
                "Screenshot installer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ask == DialogResult.Cancel) return false;
            if (ask == DialogResult.No) return true;

            string chosen = ChooseDir();
            if (chosen == null) return false;   // cancelled
            installDirOverride = chosen;
            int result = RunElevated("--install-elevated", chosen);
            if (result == -2) return true;   // permission refused: run without the setup
            if (IsCurrent(version)) { try { File.Delete(SetupLog); } catch (Exception) { } if (LaunchInstalled()) return false; }

            string detail = "";
            try { detail = File.ReadAllText(SetupLog); } catch (Exception) { }
            try { File.Delete(SetupLog); } catch (Exception) { }
            MessageBox.Show("The setup did not finish, so Screenshot will run without it this time.\r\n\r\n" + detail,
                "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
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
                if (!created) return;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    // swallowed: a stray UI exception must not kill the tray app
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}

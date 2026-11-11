using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshotTool
{
    // Full-screen overlay: frozen copy of the desktop with the capture bar on top.
    partial class SelectionForm : Form
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

        // Darkened copy, made once.
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

    }
}

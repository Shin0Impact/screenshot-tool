using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenshotTool
{
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
        // Joins the foreground app's input queue so a window can come up over a fullscreen game.
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
}

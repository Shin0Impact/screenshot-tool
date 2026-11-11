using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {
        // Shows "Capturing in N..." and waits; the popup is gone before the grab.
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

        // Visible top-level windows, top first.
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

        // 1:1 copy of screen pixels (per-monitor DPI aware).
        private static Bitmap Grab(Rectangle area, PixelFormat format)
        {
                // BitBlt with CAPTUREBLT to include layered windows; CopyFromScreen rejects that flag.
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

        // Monitors side by side, scaled to the tallest one's height.
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

    }
}

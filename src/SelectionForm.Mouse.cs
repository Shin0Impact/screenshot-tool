using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class SelectionForm : Form
    {

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

    }
}

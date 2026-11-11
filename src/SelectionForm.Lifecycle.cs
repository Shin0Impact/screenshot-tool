using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class SelectionForm : Form
    {

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

        // Shortcut pressed again: reopen on a fresh screenshot.
        public void RestartFresh()
        {
            // Not on screen yet: queue the restart until it is.
            if (!IsHandleCreated || !Visible) { restartPending = true; Diag.Log("restart asked before overlay was visible: queued"); return; }
            CloseReason = "shortcut again";
            Outcome = OverlayOutcome.DelayRestart;
            DelaySeconds = 0;
            RestartMode = mode;
            try { Close(); } catch (Exception) { ForceClose(); }
        }

        // Ends a dialog that never came up.
        public void ForceClose()
        {
            try { Outcome = OverlayOutcome.Cancelled; CloseReason = "forced"; } catch (Exception) { }
            try { DialogResult = DialogResult.Cancel; } catch (Exception) { }
            try { Hide(); } catch (Exception) { }
            try { Close(); } catch (Exception) { }
        }

        public bool FailedToShow;
        public double AgeSeconds { get { return (DateTime.UtcNow - created).TotalSeconds; } }
        public bool NeverShown { get { return !Visible && (DateTime.UtcNow - created).TotalSeconds > 2; } }

        // Follow mouse: use the window that was under the mouse.
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
            // Only our own buttons and keys may close the overlay; outside closes are refused.
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
            // Keep the exact virtual-screen rectangle on DPI changes.
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

    }
}

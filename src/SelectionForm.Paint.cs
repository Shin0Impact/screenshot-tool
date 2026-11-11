using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class SelectionForm : Form
    {

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
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class AnnotateForm : Form
    {

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
}

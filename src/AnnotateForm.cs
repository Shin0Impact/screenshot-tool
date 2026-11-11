using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScreenshotTool
{
    // Editor shown after a capture when Edit is on.
    partial class AnnotateForm : Form
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

    }
}

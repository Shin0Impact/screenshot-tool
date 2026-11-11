using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenshotTool
{
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

    // Blurred desktop shown behind the tray menu.
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

    // Tray menu look: frosted background, rounded hover, plain check mark.
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

    // Colors that follow the Windows light/dark setting.
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
}

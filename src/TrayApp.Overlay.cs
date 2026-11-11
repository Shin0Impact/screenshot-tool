using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {
        private static OverlayResult RunOverlay(int startMode)
        {
            Rectangle vs = VirtualScreen();
            Rectangle mon = CurrentMonitor();
            List<Rectangle> windows = CollectWindows();

                // Close Windows' own snipping overlay if it opened too.
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
            Stopwatch ov = Stopwatch.StartNew();
            using (Bitmap desktop = Grab(vs, PixelFormat.Format32bppRgb))
            using (SelectionForm form = new SelectionForm(desktop, vs, windows, MonitorRects(), mon, startMode))
            {
                Diag.Log("overlay prepared in " + ov.ElapsedMilliseconds + " ms (" + windows.Count + " windows), showing");
                openOverlay = form;
                SelectionForm shownForm = form;
                form.Shown += delegate
                {
                                        // A queued press restarts the overlay once.
                    if (pendingPresses > 0) { pendingPresses--; Diag.Log("queued press taken: " + pendingPresses + " left"); shownForm.RestartFresh(); }
                };
                                // If the window never appears, start over.
                System.Windows.Forms.Timer showCheck = new System.Windows.Forms.Timer();
                showCheck.Interval = 400;
                showCheck.Tick += delegate
                {
                    showCheck.Stop();
                    if (!form.IsHandleCreated && showRetries < 3)
                    {
                        showRetries++;
                        Diag.Log("overlay window never appeared: starting it again (try " + showRetries + ")");
                        Diag.Log(SnipDiag());
                        form.FailedToShow = true;
                        form.ForceClose();
                    }
                };
                showCheck.Start();
                try { form.ShowDialog(); }
                finally { openOverlay = null; showCheck.Stop(); showCheck.Dispose(); }
                if (form.FailedToShow)
                {
                    res.Restart = true;
                    res.RestartDelay = 0;
                    res.RestartMode = startMode;
                    return res;
                }
                Diag.Log("overlay closed after " + ov.ElapsedMilliseconds + " ms: " + form.Outcome
                    + (form.CloseReason.Length > 0 ? " (" + form.CloseReason + ")" : ""));

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

        private void SaveAsync(Bitmap bmp, Rectangle toastMonitor)
        {
            Thread th = new Thread(delegate()
            {
                Stopwatch sv = Stopwatch.StartNew();
                try { Save(bmp, toastMonitor); Diag.Log("saved/copied in " + sv.ElapsedMilliseconds + " ms"); }
                catch (Exception ex) { Diag.Log("save error: " + ex.Message); }
                finally { bmp.Dispose(); }
            });
            th.SetApartmentState(ApartmentState.STA);   // the clipboard needs an STA thread
            th.IsBackground = false;
            th.Start();
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
                string msg = verb + "  " + bmp.Width + " x " + bmp.Height;
                sync.BeginInvoke(new Action(delegate
                {
                    Toast t = new Toast(msg, toastMonitor, 1400);
                    t.Show();
                    t.Refresh();
                }));
            }
            catch (Exception) { }

            if (Settings.CopyClipboard)
            {
                sw.Reset();
                sw.Start();
                try
                {
                            // Image plus the saved file, so chat apps paste the original PNG.
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

        // Loads slow first-use code ahead of time.
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

    }
}

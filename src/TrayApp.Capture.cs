using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {
        // Remove logs left by older builds.
        private static void CleanOldLogs()
        {
            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot-tool.log")); } catch (Exception) { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "screenshot-setup.log")); } catch (Exception) { }
        }

        private void Post(CaptureMode mode)
        {
            // Never do real work inside the hook; hand it to the UI thread.
            DateTime queued = DateTime.UtcNow;
            sync.BeginInvoke(new Action(delegate
            {
                Diag.Log("hotkey " + mode + ", UI thread picked it up after " + (int)(DateTime.UtcNow - queued).TotalMilliseconds + " ms; foreground: " + ForegroundInfo());
                Capture(mode);
            }));
        }

        // Logs snipping processes and the foreground window.
        private static string SnipDiag()
        {
            try
            {
                List<string> found = new List<string>();
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string n = p.ProcessName;
                        if (n.IndexOf("Snip", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("ScreenClipping", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("ScreenSketch", StringComparison.OrdinalIgnoreCase) >= 0)
                            found.Add(n + "(" + (p.MainWindowHandle != IntPtr.Zero ? "has window" : "no window") + ")");
                    }
                    catch (Exception) { }
                    finally { p.Dispose(); }
                }
                string title = "";
                try
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
                    IntPtr fg = Native.GetForegroundWindow();
                    Native.GetWindowText(fg, sb, sb.Capacity);
                    title = sb.ToString();
                }
                catch (Exception) { }
                return "snipping processes: " + (found.Count == 0 ? "none" : string.Join(", ", found.ToArray())) + "; foreground: " + ForegroundInfo() + " \"" + title + "\"";
            }
            catch (Exception ex) { return "snip diag failed: " + ex.Message; }
        }

        private static string ForegroundInfo()
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName + " (elevated=" + IsElevated(pid) + ")";
            }
            catch (Exception) { return "?"; }
        }

        // For tray-menu / tray-click captures: wait a moment so the menu is gone before the shot.
        private void PostDelayed(CaptureMode mode)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 250;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                Capture(mode);
            };
            t.Start();
        }

        private void Capture(CaptureMode mode)
        {
            if (busy)
            {
                // Pressed again while open: start a fresh capture.
                SelectionForm o = openOverlay;
                AnnotateForm a = openAnnotate;
                Diag.Log("busy: overlay=" + (o != null && !o.IsDisposed) + (o != null && !o.IsDisposed ? " visible=" + o.Visible : "")
                    + ", annotate=" + (a != null && !a.IsDisposed) + ", busy for " + (int)(DateTime.UtcNow - busySince).TotalSeconds + " s");
                if (o != null && !o.IsDisposed)
                {
                    bool alive = o.IsHandleCreated && o.Visible;
                    if (o.NeverShown || (!alive && o.AgeSeconds > 1.5) || (!o.IsHandleCreated && o.AgeSeconds > 0.5))
                    {
                            // Overlay is not really on screen: drop it and start over.
                        Diag.Log("overlay is not on screen (handle=" + o.IsHandleCreated + " visible=" + o.Visible + " age=" + o.AgeSeconds.ToString("0.0") + " s): dropping it and starting over");
                        Diag.Log(SnipDiag());
                        o.ForceClose();
                        busy = false;
                        openOverlay = null;
                        Post(mode);
                    }
                    else o.RestartFresh();
                    return;
                }
                if (a != null && !a.IsDisposed)
                {
                    try { a.WindowState = FormWindowState.Normal; Fg.Take(a.Handle); a.Activate(); } catch (Exception) { }
                    return;
                }
                // Between two overlays: the next one takes the press.
                if ((DateTime.UtcNow - busySince).TotalSeconds < 8) { pendingPresses++; Diag.Log("queued press (between overlays): " + pendingPresses); return; }
                Diag.Log("busy flag was stale: starting over");
            }
            busy = true;
            busySince = DateTime.UtcNow;
            pendingPresses = 0;
            showRetries = 0;
            Diag.Log("capture start, mode " + mode);
            try
            {
                Bitmap result = null;
                Rectangle toastMonitor = CurrentMonitor();

                // "Follow mouse": monitor / window capture takes whatever is under the mouse, no picking.
                Rectangle direct = Rectangle.Empty;
                if (Settings.MonitorFollow && mode == CaptureMode.Monitor) direct = toastMonitor;
                else if (Settings.MonitorFollow && mode == CaptureMode.Window) direct = WindowUnderCursor();

                if (!direct.IsEmpty)
                {
                    result = Grab(direct, PixelFormat.Format24bppRgb);
                }
                else if (mode == CaptureMode.AllMonitors)
                {
                    result = GrabAllMonitors();
                }
                else
                {
                    int startMode = (mode == CaptureMode.Window) ? 1
                        : (mode == CaptureMode.Monitor) ? 2
                        : (mode == CaptureMode.Overlay ? Settings.LastMode : 0);
                    if (Settings.MonitorFollow && mode == CaptureMode.Overlay) startMode = 0;
                    for (;;)
                    {
                        OverlayResult o = RunOverlay(startMode);
                        if (o.Restart)
                        {
                            // The shortcut was pressed again: close and freeze the screen afresh.
                            startMode = o.RestartMode;
                            Countdown(o.RestartDelay, toastMonitor);
                            continue;
                        }
                        result = o.Image;
                        break;
                    }
                }

                if (result != null && Settings.Annotate)
                {
                    Bitmap edited = null;
                    try
                    {
                        using (AnnotateForm af = new AnnotateForm(result, toastMonitor))
                        {
                            openAnnotate = af;
                            Diag.Log("annotate window opening");
                            try { af.ShowDialog(); }
                            finally { openAnnotate = null; Diag.Log("annotate window closed, accepted=" + af.Accepted); }
                            if (af.Accepted) edited = af.Result;
                        }
                    }
                    finally
                    {
                        result.Dispose();
                    }
                    result = edited;
                }

                if (result != null)
                {
                        // Saving can stall (antivirus, clipboard managers), so it runs on its own thread.
                    Bitmap toSave = result;
                    result = null;
                    SaveAsync(toSave, toastMonitor);
                }
            }
            catch (Exception ex)
            {
                Diag.Log("capture error: " + ex);
                try
                {
                    tray.ShowBalloonTip(3000, "Screenshot failed", ex.Message, ToolTipIcon.Error);
                }
                catch (Exception) { }
            }
            finally
            {
                busy = false;
                pendingPresses = 0;
                Diag.Log("capture end (" + (int)(DateTime.UtcNow - busySince).TotalMilliseconds + " ms)");
            }
        }

        private static SelectionForm openOverlay;
        private static int showRetries;
        private static int pendingPresses;   // shortcut presses that arrived while no overlay could take them
        private static AnnotateForm openAnnotate;
        private DateTime busySince = DateTime.UtcNow;

    }
}

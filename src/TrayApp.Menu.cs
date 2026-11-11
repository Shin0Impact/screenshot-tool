using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {

        private const string ReleaseApi = "https://api.github.com/repos/Shin0Impact/screenshot-tool/releases/tags/latest";

        // The commit this build was made from (CI embeds it); null for a local build.
        private static string OwnBuild()
        {
            try
            {
                using (Stream st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildId"))
                {
                    if (st == null) return null;
                    using (StreamReader r = new StreamReader(st)) return r.ReadToEnd().Trim();
                }
            }
            catch (Exception) { return null; }
        }

        private void CheckForUpdates()
        {
            try { tray.ShowBalloonTip(2000, "Screenshot", "Checking for updates...", ToolTipIcon.Info); } catch (Exception) { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string json = null, err = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;   // TLS 1.2
                    System.Net.HttpWebRequest rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(ReleaseApi);
                    rq.UserAgent = "ScreenshotTool";
                    rq.Timeout = 15000;
                    using (System.Net.WebResponse resp = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                        json = sr.ReadToEnd();
                }
                catch (Exception ex) { err = ex.Message; }
                sync.BeginInvoke(new Action(delegate { ShowUpdateResult(json, err); }));
            });
        }

        private void ShowUpdateResult(string json, string err)
        {
            if (json == null)
            {
                MessageBox.Show("Could not check for updates:\r\n" + err, "Screenshot",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Match commit = Regex.Match(json, "\"target_commitish\"\\s*:\\s*\"([0-9a-fA-F]{7,40})\"");
            Match url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+Screenshot-Installer\\.exe)\"");
            string own = OwnBuild();
            if (commit.Success && own != null && string.Equals(commit.Groups[1].Value, own, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You have the latest version.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!url.Success)
            {
                MessageBox.Show("Could not find the latest installer.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string question = own == null
                ? "This is a local build, so I can't tell if it is current. Download the latest installer?"
                : "A newer version is available. Download the installer and update now?";
            if (MessageBox.Show(question, "Screenshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string link = url.Groups[1].Value;
            // A previous installer may still be running from here (its file is locked), so every download gets its own folder.
            string tmpRoot = Path.Combine(Path.GetTempPath(), "ScreenshotUpdate");
            try { if (Directory.Exists(tmpRoot)) foreach (string old in Directory.GetDirectories(tmpRoot)) { try { Directory.Delete(old, true); } catch (Exception) { } } } catch (Exception) { }
            string tmpDir = Path.Combine(tmpRoot, Guid.NewGuid().ToString("N").Substring(0, 8));
            try { Directory.CreateDirectory(tmpDir); } catch (Exception) { }
            string target = Path.Combine(tmpDir, "Screenshot-Installer.exe");
            try { tray.ShowBalloonTip(3000, "Screenshot", "Downloading the update...", ToolTipIcon.Info); } catch (Exception) { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string dlErr = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Headers.Add("User-Agent", "ScreenshotTool");
                        wc.DownloadFile(link, target);
                    }
                }
                catch (Exception ex)
                {
                    dlErr = ex.Message;
                    for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException) dlErr += "\r\n" + inner.Message;
                }
                sync.BeginInvoke(new Action(delegate
                {
                    if (dlErr != null)
                    {
                        MessageBox.Show("Could not download the update:\r\n" + dlErr, "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(target);
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not start the installer:\r\n" + ex.Message, "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }));
            });
        }

        // Opens at the click, shifted left only as needed to stay on screen.
        private void ShowTrayMenu(ContextMenuStrip menu)
        {
            try
            {
                Native.POINT pt;
                Native.GetCursorPos(out pt);
                ApplyMenuTheme(menu);
                Size sz = menu.GetPreferredSize(Size.Empty);
                Rectangle wa = Screen.FromPoint(new Point(pt.X, pt.Y)).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(pt.X, wa.Right - sz.Width));
                int bottom = Math.Min(pt.Y, wa.Bottom);
                int y = Math.Max(wa.Top, bottom - sz.Height);
                FrostBackdrop.Capture(Screen.FromPoint(new Point(pt.X, pt.Y)).Bounds);   // before the menu covers it
                Native.SetForegroundWindow(sync.Handle);   // so the menu closes when you click elsewhere
                menu.Show(new Point(x, y));
            }
            catch (Exception) { }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(saveDir);
                Process.Start("explorer.exe", "\"" + saveDir + "\"");
            }
            catch (Exception) { }
        }

        // Startup task with highest privileges, so an elevated app starts without a UAC prompt.
        private const string TaskName = "ScreenshotTool";

        private static int RunSchtasks(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception) { return -1; }
        }

        private static bool RunKeySet()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return k != null && k.GetValue(RunValueName) != null;
            }
            catch (Exception) { return false; }
        }

        private static void SetRunKey(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception) { }
        }

        private static bool IsStartupEnabled()
        {
            return RunKeySet() || RunSchtasks("/Query /TN " + TaskName) == 0;
        }

        // Moves old scheduled-task startup (cannot launch UIAccess) to the Run key.
        private static void MigrateStartup()
        {
            try
            {
                if (HasUiAccess((uint)Process.GetCurrentProcess().Id) != 1) return;
                if (RunSchtasks("/Query /TN " + TaskName) != 0) return;
                SetRunKey(true);
                RunSchtasks("/Delete /TN " + TaskName + " /F");
            }
            catch (Exception) { }
        }

        private static string XmlEscape(string t)
        {
            return t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void ToggleStartup()
        {
            try
            {
                if (IsStartupEnabled())
                {
                    RunSchtasks("/Delete /TN " + TaskName + " /F");
                    SetRunKey(false);
                }
                else if (IsElevated((uint)Process.GetCurrentProcess().Id) != 1)
                {
                    SetRunKey(true);   // started by the shell at logon (works for the UIAccess build too)
                }
                else
                {
                    string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    // An elevated copy needs a highest-privilege task; a UIAccess copy runs as a normal user.
                    string runLevel = (IsElevated((uint)Process.GetCurrentProcess().Id) == 1) ? "HighestAvailable" : "LeastPrivilege";
                    string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>"
                        + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
                        + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + XmlEscape(user) + "</UserId></LogonTrigger></Triggers>"
                        + "<Principals><Principal id=\"Author\"><UserId>" + XmlEscape(user) + "</UserId>"
                        + "<LogonType>InteractiveToken</LogonType><RunLevel>" + runLevel + "</RunLevel></Principal></Principals>"
                        + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
                        + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                        + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
                        + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowHardTerminate>true</AllowHardTerminate></Settings>"
                        + "<Actions Context=\"Author\"><Exec><Command>" + XmlEscape(Application.ExecutablePath) + "</Command></Exec></Actions></Task>";
                    string tmp = Path.Combine(Path.GetTempPath(), "screenshot-tool-task.xml");
                    File.WriteAllText(tmp, xml, Encoding.Unicode);
                    int rc = RunSchtasks("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F");
                    try { File.Delete(tmp); } catch (Exception) { }
                    // drop the old non-elevated Run entry, if any
                    try
                    {
                        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                        {
                            if (k != null) k.DeleteValue(RunValueName, false);
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            startupItem.Checked = IsStartupEnabled();
        }

        // Neutral menu colours that follow the Windows light/dark setting.
        private static void ApplyMenuTheme(ContextMenuStrip menu)
        {
            Theme.Refresh();
            menu.Renderer = new CleanRenderer();
            Color bg = MenuColors.Back();
            Color fg = Theme.Light ? Color.FromArgb(25, 25, 25) : Color.FromArgb(240, 240, 240);
            menu.BackColor = bg;
            menu.ForeColor = fg;
            StyleItems(menu.Items, bg, fg);
        }

        private static void StyleItems(ToolStripItemCollection items, Color bg, Color fg)
        {
            foreach (ToolStripItem it in items)
            {
                it.BackColor = Color.Transparent;
                it.ForeColor = fg;
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems)
                {
                    mi.DropDown.BackColor = bg;
                    StyleItems(mi.DropDownItems, bg, fg);
                }
            }
        }

        private static Icon MakeIcon()
        {
            // White on a dark taskbar, near-black on a light one.
            Color fg = Theme.TaskbarLight ? Color.FromArgb(32, 32, 32) : Color.White;
            using (Bitmap b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(fg, 3.5f))
                {
                    g.DrawLine(p, 5, 12, 5, 5); g.DrawLine(p, 5, 5, 12, 5);
                    g.DrawLine(p, 20, 5, 27, 5); g.DrawLine(p, 27, 5, 27, 12);
                    g.DrawLine(p, 27, 20, 27, 27); g.DrawLine(p, 27, 27, 20, 27);
                    g.DrawLine(p, 12, 27, 5, 27); g.DrawLine(p, 5, 27, 5, 20);
                }
                IntPtr hIcon = b.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(hIcon);
                }
            }
        }

    }
}

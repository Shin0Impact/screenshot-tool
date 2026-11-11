using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

#if !UIA
namespace ScreenshotTool
{
    static partial class Setup
    {

        private static void DoUninstall()
        {
            string adminDir = AdminDir();
            string userDir = UserDir();
            string self = Application.ExecutablePath;
            string copy = Path.Combine(Path.GetTempPath(), "ScreenUninstall.exe");
            if ((adminDir != null && IsInside(self, adminDir)) || (userDir != null && IsInside(self, userDir)))
            {
                // Running from a folder that is about to be deleted: continue from a temporary copy.
                try
                {
                    File.Copy(self, copy, true);
                    ProcessStartInfo again = new ProcessStartInfo(copy, "--uninstall");
                    again.UseShellExecute = true;
                    Process.Start(again);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not start the uninstaller:\r\n" + ex.Message, "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            bool ok = true;
            bool cancelled = false;
            if (adminDir != null)
            {
                int rc = RunElevated("--uninstall-elevated", null, "Removing Screenshot... this can take a few seconds.");
                try { File.Delete(SetupLog); } catch (Exception) { }
                if (rc == -2) cancelled = true;
                else if (rc != 0) ok = false;
            }
            if (!cancelled && userDir != null) UninstallUser(userDir);
            if (!cancelled)
            {
                try
                {
                    using (RegistryKey r = Registry.CurrentUser.OpenSubKey(RunPath, true))
                        if (r != null) r.DeleteValue("ScreenshotTool", false);
                }
                catch (Exception) { }
            }

            MessageBox.Show(cancelled ? "Nothing was removed (Windows permission was declined)."
                : ok ? "Screenshot was removed." : "Could not remove everything.",
                "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (string.Equals(self, copy, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ProcessStartInfo del = new ProcessStartInfo("cmd.exe",
                        "/c ping -n 3 127.0.0.1 >nul & del \"" + copy + "\"");
                    del.CreateNoWindow = true;
                    del.UseShellExecute = false;
                    Process.Start(del);
                }
                catch (Exception) { }
            }
        }

        // True when the app should just start here (no embedded builds, e.g. build.bat).
        public static bool Handle(string[] args)
        {
            byte[] adminData = ReadEmbedded("UIAccessBuild.exe");
            byte[] userData = ReadEmbedded("StandardBuild.exe");
            if (adminData == null || userData == null) return true;
            string adminVer = VersionOf(adminData);
            string userVer = VersionOf(userData);
            string cmd = args.Length > 0 ? args[0] : "";
            int parent = 0;
            if (args.Length > 1) int.TryParse(args[1], out parent);

            if (cmd == "--install-elevated")
            {
                string old = AdminDir();
                string dir = args.Length > 2 ? args[2] : AdminDefaultDir;
                Environment.ExitCode = InstallAdmin(adminData, adminVer, parent, dir, old);
                return false;
            }
            if (cmd == "--uninstall-elevated") { Environment.ExitCode = UninstallAdmin(parent); return false; }

            Application.EnableVisualStyles();
            bool asUninstaller = string.Equals(Path.GetFileName(Application.ExecutablePath), "Uninstall.exe",
                StringComparison.OrdinalIgnoreCase);
            if (asUninstaller && cmd == "")
            {
                if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                cmd = "--uninstall";
            }
            if (cmd == "--uninstall") { DoUninstall(); return false; }

            for (;;)
            {
                string adminDir = HasApp(AdminDir()) ? AdminDir() : null;
                string userDir = HasApp(UserDir()) ? UserDir() : null;
                if (adminDir != null) userDir = null;   // the administrator install wins if both exist
                bool adminCurrent = adminDir != null && VersionMatches(adminDir, adminVer) && CertTrusted();
                bool userCurrent = userDir != null && VersionMatches(userDir, userVer);

                string action;
                string chosen;
                using (InstallerForm form = new InstallerForm(adminDir, userDir, adminCurrent, userCurrent))
                {
                    form.ShowDialog();
                    action = form.Action;
                    chosen = form.Chosen;
                }

                if (action == "close") return false;
                if (action == "start")
                {
                    if (StartApp(adminDir != null ? adminDir : userDir)) return false;
                    MessageBox.Show("Could not start Screenshot.", "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    continue;
                }
                if (action == "uninstall")
                {
                    if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                    DoUninstall();
                    return false;
                }

                if (action == "removeadmin")
                {
                    if (MessageBox.Show("Remove administrator support? Screenshot will keep working, installed for you "
                        + "only, but its hotkeys will stop working over programs that run as administrator.",
                        "Screenshot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                    int rr = RunElevated("--uninstall-elevated", null, "Removing administrator support...");
                    if (rr == -2) continue;
                    try { File.Delete(SetupLog); } catch (Exception) { }
                    if (rr != 0 || HasApp(AdminDir()))
                    {
                        MessageBox.Show("Could not remove administrator support.", "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    string err = InstallUser(userData, userVer, UserDefaultDir, null);
                    if (err != null)
                    {
                        MessageBox.Show("Administrator support was removed, but the standard install did not finish.\r\n\r\n" + err,
                            "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (StartApp(UserDefaultDir)) return false;
                    continue;
                }

                bool admin = action == "install-admin" || action == "addadmin" || (action == "update" && adminDir != null);
                if (admin)
                {
                    string dir = action == "install-admin" ? chosen : (adminDir != null ? adminDir : AdminDefaultDir);
                    int rc = RunElevated("--install-elevated", dir, "Setting up Screenshot... the first time this can take up to a minute.");
                    if (rc == -2) continue;   // Windows permission refused: back to the window
                    string aDir = AdminDir();
                    if (HasApp(aDir) && VersionMatches(aDir, adminVer))
                    {
                        try { File.Delete(SetupLog); } catch (Exception) { }
                        string u = UserDir();
                        if (u != null) UninstallUser(u);   // moved to the administrator install
                        // Start at logon through the shell (Run key): a scheduled task cannot start a UIAccess program.
                        try
                        {
                            using (RegistryKey r = Registry.CurrentUser.CreateSubKey(RunPath))
                                r.SetValue("ScreenshotTool", "\"" + Path.Combine(aDir, "Screenshot.exe") + "\"");
                        }
                        catch (Exception) { }
                        if (StartApp(aDir)) return false;
                        MessageBox.Show("Installed, but could not start it. Try again from the installer.",
                            "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    MessageBox.Show("The setup did not finish.\r\n\r\n" + ReadSetupLog(), "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                string target = action == "install-user" ? chosen : userDir;
                string error = InstallUser(userData, userVer, target, UserDir());
                if (error != null)
                {
                    MessageBox.Show("The install did not finish.\r\n\r\n" + error, "Screenshot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (StartApp(target)) return false;
            }
        }
    }
}
#endif

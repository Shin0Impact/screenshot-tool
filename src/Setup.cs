using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

#if !UIA
namespace ScreenshotTool
{
    // Screenshot-Installer.exe embeds two builds: UIAccess (signed, in Program Files, sees keys typed into
    // administrator windows) and standard (any folder, no prompt).
    static partial class Setup
    {
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool";
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string ProgramFilesDir
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return pf.TrimEnd('\\');
            }
        }

        private static string AdminDefaultDir { get { return Path.Combine(ProgramFilesDir, "ScreenshotTool"); } }

        private static string UserDefaultDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\ScreenshotTool");
            }
        }

        private static string SetupLog { get { return Path.Combine(Path.GetTempPath(), "screenshot-setup.log"); } }

        // Where a mode was installed, from its Uninstall registry entry (null = not installed).
        private static string Registered(RegistryKey hive)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(UninstallKeyPath))
                {
                    if (k == null) return null;
                    string v = k.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            catch (Exception) { return null; }
        }

        private static string AdminDir() { return Registered(Registry.LocalMachine); }
        private static string UserDir() { return Registered(Registry.CurrentUser); }

        private static bool HasApp(string dir)
        {
            return dir != null && File.Exists(Path.Combine(dir, "Screenshot.exe"));
        }

        private static bool SameDir(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool IsInside(string dir, string parent)
        {
            try
            {
                return Path.GetFullPath(dir).TrimEnd('\\').StartsWith(parent.TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // A folder that is new, empty, or already holds this app; never the root of a drive.
        private static bool IsUsableDir(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\');
                if (full.Length < 4 || Path.GetPathRoot(full).TrimEnd('\\').Length == full.Length) return false;
                if (Directory.Exists(full))
                {
                    bool mine = File.Exists(Path.Combine(full, "version.txt"));
                    if (!mine && Directory.GetFileSystemEntries(full).Length > 0) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static byte[] ReadEmbedded(string name)
        {
            using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                byte[] data = new byte[s.Length];
                int got = 0;
                while (got < data.Length)
                {
                    int n = s.Read(data, got, data.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                return data;
            }
        }

        private static string VersionOf(byte[] data)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool VersionMatches(string dir, string version)
        {
            try { return HasApp(dir) && File.ReadAllText(Path.Combine(dir, "version.txt")).Trim() == version; }
            catch (Exception) { return false; }
        }

        // The administrator build only starts while its certificate is trusted.
        private static bool CertTrusted()
        {
            try
            {
                foreach (System.Security.Cryptography.X509Certificates.StoreName name in new System.Security.Cryptography.X509Certificates.StoreName[] {
                    System.Security.Cryptography.X509Certificates.StoreName.Root,
                    System.Security.Cryptography.X509Certificates.StoreName.TrustedPublisher })
                {
                    System.Security.Cryptography.X509Certificates.X509Store st = new System.Security.Cryptography.X509Certificates.X509Store(
                        name, System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                    st.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                    bool found = false;
                    foreach (System.Security.Cryptography.X509Certificates.X509Certificate2 c in st.Certificates)
                        if (c.Subject == "CN=Screenshot Tool (local)") found = true;
                    st.Close();
                    if (!found) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool StartApp(string dir)
        {
            try
            {
                    // Through the shell: CreateProcess fails with "requires elevation" for a UIAccess exe.
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(dir, "Screenshot.exe"));
                psi.UseShellExecute = true;
                psi.WorkingDirectory = dir;
                Process.Start(psi);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void KillOthers(int parentPid)
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id == parentPid) continue;
                    if (p.ProcessName.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }

        private static string Q(string s) { return s.Replace("'", "''"); }

        // Runs an elevated PowerShell script and returns its exit code.
        private static int RunPowerShell(string script, out string output)
        {
            string exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            ProcessStartInfo psi = new ProcessStartInfo(exe,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(180000);
                output = o + e;
                return p.HasExited ? p.ExitCode : -1;
            }
        }

    }
}
#endif

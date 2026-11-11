using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

#if !UIA
namespace ScreenshotTool
{
    static partial class Setup
    {

        private static int InstallAdmin(byte[] data, string version, int parentPid, string dir, string oldDir)
        {
            string output = "";
            int code = 1;
            string tmp = Path.Combine(Path.GetTempPath(), "Screenshot-UIAccess-" + version + ".exe");
            try
            {
                KillOthers(parentPid);
                File.WriteAllBytes(tmp, data);
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                string script = @"
$ErrorActionPreference = 'Stop'
try {
  $src = '__SRC__'; $dir = '__DIR__'; $ver = '__VER__'; $user = '__USER__'; $launcher = '__LAUNCHER__'; $old = '__OLD__'
  $subject = 'CN=Screenshot Tool (local)'
  $target = Join-Path $dir 'Screenshot.exe'
  $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
  if (-not $cert) { $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(10) -KeyExportPolicy NonExportable }
  $cer = Join-Path $env:TEMP 'screenshot-tool-local.cer'
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
  Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
  Remove-Item $cer -Force
  if ($old -and ($old -ne $dir) -and (Test-Path $old)) { Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory -Force $dir | Out-Null
  Copy-Item $src $target -Force
  $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -HashAlgorithm SHA256
  if ($sig.Status -ne 'Valid') { throw ('Signing failed: ' + $sig.Status + ' ' + $sig.StatusMessage) }
  Set-Content -Path (Join-Path $dir 'version.txt') -Value $ver -Encoding Ascii
  $unin = Join-Path $dir 'Uninstall.exe'
  Copy-Item $launcher $unin -Force
  $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool'
  New-Item -Path $key -Force | Out-Null
  Set-ItemProperty $key DisplayName 'Screenshot'
  Set-ItemProperty $key Publisher 'Shin0Impact'
  Set-ItemProperty $key DisplayVersion $ver
  Set-ItemProperty $key DisplayIcon $target
  Set-ItemProperty $key InstallLocation $dir
  Set-ItemProperty $key UninstallString ('""' + $unin + '"" --uninstall')
  Set-ItemProperty $key NoModify 1 -Type DWord
  Set-ItemProperty $key NoRepair 1 -Type DWord
  Unregister-ScheduledTask -TaskName 'ScreenshotTool' -Confirm:$false -ErrorAction SilentlyContinue
  Write-Output 'OK'
} catch {
  Write-Output ('FAILED: ' + $_.Exception.Message)
  exit 1
}
".Replace("__SRC__", Q(tmp)).Replace("__DIR__", Q(dir)).Replace("__VER__", Q(version)).Replace("__USER__", Q(user)).Replace("__LAUNCHER__", Q(Application.ExecutablePath)).Replace("__OLD__", Q(oldDir ?? ""));
                code = RunPowerShell(script, out output);
            }
            catch (Exception ex)
            {
                output += "\r\nFAILED: " + ex.Message;
                code = 1;
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }
            try { File.WriteAllText(SetupLog, output); } catch (Exception) { }
            return code;
        }

        private static int UninstallAdmin(int parentPid)
        {
            string output = "";
            try
            {
                KillOthers(parentPid);
                string target = AdminDir();
                if (target == null || !IsInside(target, ProgramFilesDir)) target = AdminDefaultDir;
                string script = @"
$ErrorActionPreference = 'Continue'
$subject = 'CN=Screenshot Tool (local)'
Unregister-ScheduledTask -TaskName 'ScreenshotTool' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenshotTool' -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path '__DIR__') { Remove-Item '__DIR__' -Recurse -Force }
foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher') {
  Get-ChildItem $store | Where-Object { $_.Subject -eq $subject } | Remove-Item -Force
}
Write-Output 'OK'
".Replace("__DIR__", Q(target));
                int code = RunPowerShell(script, out output);
                try { File.WriteAllText(SetupLog, output); } catch (Exception) { }
                return code;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(SetupLog, ex.ToString()); } catch (Exception) { }
                return 1;
            }
        }

        // Returns null on success, otherwise a message.
        private static string InstallUser(byte[] data, string version, string dir, string oldDir)
        {
            try
            {
                KillOthers(0);
                if (oldDir != null && !SameDir(oldDir, dir) && Directory.Exists(oldDir)) TryDeleteDir(oldDir);
                Directory.CreateDirectory(dir);
                string exe = Path.Combine(dir, "Screenshot.exe");
                File.WriteAllBytes(exe, data);
                File.Copy(Application.ExecutablePath, Path.Combine(dir, "Uninstall.exe"), true);
                File.WriteAllText(Path.Combine(dir, "version.txt"), version);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
                {
                    k.SetValue("DisplayName", "Screenshot");
                    k.SetValue("Publisher", "Shin0Impact");
                    k.SetValue("DisplayVersion", version);
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + Path.Combine(dir, "Uninstall.exe") + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                using (RegistryKey r = Registry.CurrentUser.CreateSubKey(RunPath))
                    r.SetValue("ScreenshotTool", "\"" + exe + "\"");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static void TryDeleteDir(string dir)
        {
            for (int i = 0; i < 8; i++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
                catch (Exception) { Thread.Sleep(300); }
            }
        }

        private static void UninstallUser(string dir)
        {
            KillOthers(0);
            try
            {
                using (RegistryKey r = Registry.CurrentUser.OpenSubKey(RunPath, true))
                    if (r != null) r.DeleteValue("ScreenshotTool", false);
            }
            catch (Exception) { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch (Exception) { }
            if (dir != null) TryDeleteDir(dir);
        }

        // Restarts this exe elevated with an argument and waits. Returns -2 if refused.
        private static int RunElevated(string argument, string dir, string busyText)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath,
                argument + " " + Process.GetCurrentProcess().Id + (dir != null ? " \"" + dir + "\"" : ""));
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            Form busy = null;
            try
            {
                using (Process p = Process.Start(psi))
                {
                    busy = BusyWindow(busyText);
                    while (!p.WaitForExit(40)) Application.DoEvents();
                    return p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return -2; }
            finally { if (busy != null) { busy.Close(); busy.Dispose(); } }
        }

        private static Form BusyWindow(string text)
        {
            Form f = new Form();
            f.Text = "Screenshot";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.ControlBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Font = SystemFonts.MessageBoxFont;
            f.ClientSize = new Size(420, 96);
            f.TopMost = true;
            Label l = new Label();
            l.SetBounds(16, 14, 388, 36);
            l.Text = text;
            f.Controls.Add(l);
            ProgressBar pb = new ProgressBar();
            pb.SetBounds(16, 58, 388, 18);
            pb.Style = ProgressBarStyle.Marquee;
            pb.MarqueeAnimationSpeed = 30;
            f.Controls.Add(pb);
            f.Show();
            f.Refresh();
            return f;
        }

        private static string ReadSetupLog()
        {
            string detail = "";
            try { detail = File.ReadAllText(SetupLog); } catch (Exception) { }
            try { File.Delete(SetupLog); } catch (Exception) { }
            return detail;
        }

    }
}
#endif

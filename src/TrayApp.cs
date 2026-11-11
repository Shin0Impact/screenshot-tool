using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ScreenshotTool";

        private readonly NotifyIcon tray;
        private readonly Control sync;
        private readonly Native.LowLevelKeyboardProc hookProc;
        private IntPtr hookHandle = IntPtr.Zero;
        private Thread hookThread;
        private uint hookThreadId;
        private bool busy;
        private bool swallowKeyUp;
        private bool swallowSKeyUp;
        private bool snapshotPassed;
        private readonly bool[] keys = new bool[256];
        private readonly string saveDir;
        private readonly ToolStripMenuItem startupItem;

        public TrayApp()
        {
            saveDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

            Settings.Load();

            sync = new Control();
            IntPtr force = sync.Handle; // create the handle so BeginInvoke works from the hook
            GC.KeepAlive(force);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
            menu.Items.Add("Capture bar  (PrtScn)", null, delegate { PostDelayed(CaptureMode.Overlay); });
            menu.Items.Add("Monitor  (Ctrl+PrtScn)", null, delegate { PostDelayed(CaptureMode.Monitor); });
            menu.Items.Add("Window  (Alt+PrtScn)", null, delegate { PostDelayed(CaptureMode.Window); });
            menu.Items.Add("All monitors  (Shift+PrtScn)", null, delegate { PostDelayed(CaptureMode.AllMonitors); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem adjustItem = new ToolStripMenuItem("Edit (adjust + annotate)");
            adjustItem.Click += delegate { Settings.Edit = !Settings.Edit; Settings.Save(); };
            ToolStripMenuItem followItem = new ToolStripMenuItem("Follow mouse (no picking)");
            followItem.Click += delegate { Settings.MonitorFollow = !Settings.MonitorFollow; Settings.Save(); };
            ToolStripMenuItem saveItem = new ToolStripMenuItem("Save to file");
            saveItem.Click += delegate { Settings.ToggleSave(); };
            ToolStripMenuItem copyItem = new ToolStripMenuItem("Copy to clipboard");
            copyItem.Click += delegate { Settings.ToggleCopy(); };
            menu.Items.Add(adjustItem);
            menu.Items.Add(saveItem);
            menu.Items.Add(copyItem);
            menu.Items.Add(followItem);

            ToolStripMenuItem tolItem = new ToolStripMenuItem("Click tolerance");
            int[] tolChoices = new int[] { 0, 5, 12, 25, 50 };
            foreach (int choice in tolChoices)
            {
                int value = choice;
                ToolStripMenuItem item = new ToolStripMenuItem(value == 0 ? "Off" : value + " px");
                item.Tag = value;
                item.Click += delegate
                {
                    Settings.ClickTolerance = value;
                    Settings.Save();
                };
                tolItem.DropDownItems.Add(item);
            }
            tolItem.DropDownDirection = ToolStripDropDownDirection.Right;
            tolItem.DropDown.Opened += delegate { FrostBackdrop.Round(tolItem.DropDown.Handle); };
            menu.Opened += delegate { FrostBackdrop.Round(menu.Handle); };
            menu.Closed += delegate { FrostBackdrop.Release(); };
            menu.Items.Add(tolItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open screenshots folder", null, delegate { OpenFolder(); });
            startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += delegate { ToggleStartup(); };
            menu.Items.Add(startupItem);

            // keep the check marks in sync with the settings (they can also change from the capture bar)
            menu.Opening += delegate
            {
                ApplyMenuTheme(menu);
                adjustItem.Checked = Settings.Adjust;
                saveItem.Checked = Settings.SaveFile;
                copyItem.Checked = Settings.CopyClipboard;
                followItem.Checked = Settings.MonitorFollow;
                foreach (ToolStripMenuItem t in tolItem.DropDownItems)
                {
                    t.Checked = ((int)t.Tag == Settings.ClickTolerance);
                }
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Check for updates", null, delegate { CheckForUpdates(); });
            string uninstaller = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall.exe");
            if (File.Exists(uninstaller))
            {
                menu.Items.Add("Uninstall...", null, delegate
                {
                    if (MessageBox.Show("Remove Screenshot from this computer?", "Screenshot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        ProcessStartInfo u = new ProcessStartInfo(uninstaller, "--uninstall");
                        u.UseShellExecute = true;
                        Process.Start(u);
                    }
                    catch (Exception) { }
                });
            }
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Screenshot - PrtScn to capture";
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                Diag.Log("tray click: " + e.Button + ", busy=" + busy);
                if (e.Button == MouseButtons.Left) PostDelayed(CaptureMode.Overlay);
                else if (e.Button == MouseButtons.Right) ShowTrayMenu(menu);
            };

            hookProc = HookCallback;
            StartHookThread();
            RegisterBackupHotkeys();
            CleanOldLogs();
            MigrateStartup();
            Thread wd = new Thread(Watchdog);
            wd.IsBackground = true;
            wd.Start();
            sync.BeginInvoke(new Action(WarmUp));
            if (hookHandle == IntPtr.Zero)
            {
                MessageBox.Show("Could not install the PrtScn hook. Use the tray icon to capture instead.",
                    "Screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void ExitThreadCore()
        {
            if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            try { if (hotkeyWindow != null) hotkeyWindow.DestroyHandle(); } catch (Exception) { }
            tray.Visible = false;
            tray.Dispose();
            sync.Dispose();
            base.ExitThreadCore();
        }
    }
}

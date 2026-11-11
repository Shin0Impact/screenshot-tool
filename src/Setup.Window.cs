using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

#if !UIA
namespace ScreenshotTool
{
    static partial class Setup
    {

        private class InstallerForm : Form
        {
            private readonly TextBox path = new TextBox();
            private readonly Label problem = new Label();
            private readonly RadioButton adminRadio = new RadioButton();
            private readonly RadioButton userRadio = new RadioButton();
            public string Action = "close";
            public string Chosen = "";

            private void AddButton(string text, string action, int x, int y, int w)
            {
                Button b = new Button();
                b.SetBounds(x, y, w, 32);
                b.Text = text;
                b.Click += delegate { Action = action; DialogResult = DialogResult.OK; };
                Controls.Add(b);
            }

            public InstallerForm(string adminDir, string userDir, bool adminCurrent, bool userCurrent)
            {
                Text = "Screenshot installer";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                Font = SystemFonts.MessageBoxFont;
                TopMost = true;

                Label head = new Label();
                head.SetBounds(16, 14, 668, 80);
                Controls.Add(head);

                if (adminDir == null && userDir == null)
                {
                    ClientSize = new Size(580, 330);
                    head.Text = "Install Screenshot. Administrator support lets the hotkeys work over games and other "
                        + "programs that run as administrator. Windows asks for permission once; after that the app "
                        + "starts normally, with no prompts.";

                    adminRadio.SetBounds(16, 96, 548, 22);
                    adminRadio.Text = "With administrator support (asks Windows for permission once)";
                    adminRadio.Checked = true;
                    adminRadio.CheckedChanged += delegate { SwitchDefault(); };
                    Controls.Add(adminRadio);

                    userRadio.SetBounds(16, 122, 548, 22);
                    userRadio.Text = "Without administrator support (no permission needed)";
                    userRadio.CheckedChanged += delegate { SwitchDefault(); };
                    Controls.Add(userRadio);

                    Label where = new Label();
                    where.SetBounds(16, 160, 548, 20);
                    where.Text = "Install folder:";
                    Controls.Add(where);

                    path.SetBounds(16, 184, 456, 24);
                    path.Text = AdminDefaultDir;
                    Controls.Add(path);

                    Button browse = new Button();
                    browse.SetBounds(480, 182, 84, 28);
                    browse.Text = "Browse...";
                    browse.Click += delegate
                    {
                        using (FolderBrowserDialog fb = new FolderBrowserDialog())
                        {
                            fb.Description = "Choose the install folder.";
                            fb.ShowNewFolderButton = true;
                            try { if (Directory.Exists(path.Text)) fb.SelectedPath = path.Text; } catch (Exception) { }
                            if (fb.ShowDialog(this) == DialogResult.OK) { path.Text = fb.SelectedPath; problem.Text = ""; }
                        }
                    };
                    Controls.Add(browse);

                    problem.SetBounds(16, 216, 548, 40);
                    problem.ForeColor = Color.Firebrick;
                    Controls.Add(problem);

                    Button install = new Button();
                    install.SetBounds(16, 280, 120, 32);
                    install.Text = "Install";
                    install.Click += delegate { TryInstall(); };
                    Controls.Add(install);

                    Button cancel = new Button();
                    cancel.SetBounds(444, 280, 120, 32);
                    cancel.Text = "Cancel";
                    cancel.Click += delegate { Action = "close"; DialogResult = DialogResult.Cancel; };
                    Controls.Add(cancel);

                    AcceptButton = install;
                    CancelButton = cancel;
                    return;
                }

                ClientSize = new Size(700, 190);
                bool admin = adminDir != null;
                bool current = admin ? adminCurrent : userCurrent;
                head.Text = admin
                    ? "Screenshot is installed with administrator support."
                    : "Screenshot is installed without administrator support. Its hotkeys do not work over "
                        + "programs that run as administrator.";
                if (!current) head.Text += "\r\n\r\nThe installed copy needs an update or repair.";

                                // Fixed button slots: Start/Update, administrator support, Uninstall, Close.
                int bw = 700;
                if (!current) AddButton("Update", "update", 16, 120, 150);
                else AddButton("Start Screenshot", "start", 16, 120, 150);
                if (admin) AddButton("Remove administrator support", "removeadmin", (bw - 250) / 2, 120, 250);
                else AddButton("Add administrator support", "addadmin", (bw - 250) / 2, 120, 250);
                AddButton("Uninstall", "uninstall", 482, 120, 96);
                Button close = new Button();
                close.SetBounds(588, 120, 96, 32);
                close.Text = "Close";
                close.Click += delegate { Action = "close"; DialogResult = DialogResult.Cancel; };
                Controls.Add(close);
                CancelButton = close;
            }

            private void SwitchDefault()
            {
                string t = path.Text.Trim();
                if (adminRadio.Checked && SameDir(t, UserDefaultDir)) path.Text = AdminDefaultDir;
                else if (userRadio.Checked && SameDir(t, AdminDefaultDir)) path.Text = UserDefaultDir;
            }

            private void TryInstall()
            {
                string p = path.Text.Trim();
                bool admin = adminRadio.Checked;
                if (p.Length == 0 || !IsUsableDir(p))
                {
                    problem.Text = "Please use a new or empty folder.";
                    return;
                }
                if (admin && !IsInside(p, ProgramFilesDir))
                {
                    problem.Text = "With administrator support the folder must be inside " + ProgramFilesDir
                        + " (for example " + AdminDefaultDir + "). Windows only trusts programs there.";
                    return;
                }
                if (!admin && IsInside(p, ProgramFilesDir))
                {
                    problem.Text = "Without administrator support please choose a folder outside Program Files "
                        + "(for example " + UserDefaultDir + ").";
                    return;
                }
                Chosen = Path.GetFullPath(p).TrimEnd('\\');
                Action = admin ? "install-admin" : "install-user";
                DialogResult = DialogResult.OK;
            }
        }

    }
}
#endif

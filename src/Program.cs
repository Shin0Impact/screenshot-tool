using System;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotTool
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if !UIA
            if (!Setup.Handle(args)) return;
#endif
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ScreenshotTool_SingleInstance", out created))
            {
                if (!created) { Diag.Log("second instance exited (already running)"); return; }
                Diag.Log("---- start, build " + (Environment.GetCommandLineArgs().Length) + " args, exe " + Application.ExecutablePath);
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Diag.Log("UNHANDLED (terminating=" + e.IsTerminating + "): " + e.ExceptionObject);
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Diag.Log("UI exception swallowed: " + e.Exception);   // must not kill the tray app
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}

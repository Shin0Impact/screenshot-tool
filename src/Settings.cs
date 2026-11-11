using System;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace ScreenshotTool
{
    // Persistent settings (HKCU\Software\ScreenshotTool).
    static class Settings
    {
        private const string KeyPath = @"Software\ScreenshotTool";

        public static bool SaveFile = true;        // write a PNG file
        public static bool CopyClipboard = true;   // put the shot on the clipboard
        public static bool Edit = false;           // adjust the selection before capturing, then annotate
        public static bool Adjust { get { return Edit; } set { Edit = value; } }
        public static bool Annotate { get { return Edit; } set { Edit = value; } }
        public static bool MonitorFollow = false;  // Monitor capture takes the monitor under the mouse instead of asking
        public static int LastMode;                // last overlay mode: 0 = region, 1 = window, 2 = monitor
        public static int ClickTolerance = 12;     // px the mouse may move and still count as a click

        public static void Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                {
                    if (k == null) return;
                    SaveFile = GetBool(k, "SaveFile", true);
                    CopyClipboard = GetBool(k, "CopyClipboard", true);
                    Edit = GetBool(k, "Edit", false);
                    MonitorFollow = GetBool(k, "MonitorFollow", false);
                    object lm = k.GetValue("LastMode");
                    if (lm is int && (int)lm >= 0 && (int)lm <= 2) LastMode = (int)lm;
                    else if (GetBool(k, "WindowModeLast", false)) LastMode = 1;
                    object v = k.GetValue("ClickTolerance");
                    if (v is int && (int)v >= 0 && (int)v <= 200) ClickTolerance = (int)v;
                    if (!SaveFile && !CopyClipboard) CopyClipboard = true;
                }
            }
            catch (Exception) { }
        }

        private static bool GetBool(RegistryKey k, string name, bool fallback)
        {
            object v = k.GetValue(name);
            if (v is int) return ((int)v) != 0;
            return fallback;
        }

        public static void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("SaveFile", SaveFile ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("CopyClipboard", CopyClipboard ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("Edit", Edit ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("MonitorFollow", MonitorFollow ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("LastMode", LastMode, RegistryValueKind.DWord);
                    k.SetValue("ClickTolerance", ClickTolerance, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }

        // At least one output (file or clipboard) always stays on.
        public static void ToggleSave()
        {
            SaveFile = !SaveFile;
            if (!SaveFile && !CopyClipboard) CopyClipboard = true;
            Save();
        }

        public static void ToggleCopy()
        {
            CopyClipboard = !CopyClipboard;
            if (!CopyClipboard && !SaveFile) SaveFile = true;
            Save();
        }
    }

    // Debug log: %LOCALAPPDATA%\ScreenshotTool\screenshot-tool.log
    static class Diag
    {
        private static readonly object gate = new object();
        private static string path;

        public static string Path_
        {
            get
            {
                if (path == null)
                {
                    string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenshotTool");
                    Directory.CreateDirectory(d);
                    path = System.IO.Path.Combine(d, "screenshot-tool.log");
                }
                return path;
            }
        }

        public static void Log(string text)
        {
            try
            {
                lock (gate)
                {
                    string p = Path_;
                    FileInfo fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 6000000)
                    {
                        try { File.Delete(p + ".old"); File.Move(p, p + ".old"); } catch (Exception) { }
                    }
                    File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Thread.CurrentThread.ManagedThreadId + "] " + text + "\r\n");
                }
            }
            catch (Exception) { }
        }
    }
}

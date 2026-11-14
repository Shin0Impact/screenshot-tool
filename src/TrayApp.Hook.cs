using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotTool
{
    partial class TrayApp : ApplicationContext
    {
        // Fallback for when Windows drops the hook: RegisterHotKey for the PrtScn shortcuts.
        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly Action<int> onHotkey;
            public HotkeyWindow(Action<int> cb)
            {
                onHotkey = cb;
                CreateParams cp = new CreateParams();
                cp.Parent = (IntPtr)(-3);   // HWND_MESSAGE: no visible window
                CreateHandle(cp);
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) onHotkey(m.WParam.ToInt32());   // WM_HOTKEY
                else base.WndProc(ref m);
            }
        }
        private HotkeyWindow hotkeyWindow;

        private void RegisterBackupHotkeys()
        {
            try
            {
                hotkeyWindow = new HotkeyWindow(delegate(int id)
                {
                    CaptureMode m = id == 2 ? CaptureMode.Monitor : id == 3 ? CaptureMode.Window : id == 4 ? CaptureMode.AllMonitors : CaptureMode.Overlay;
                    Diag.Log("hotkey came through RegisterHotKey (the keyboard hook did not see it): " + m);
                    Post(m);
                });
                uint norepeat = 0x4000;
                bool a = Native.RegisterHotKey(hotkeyWindow.Handle, 1, norepeat, (uint)Native.VK_SNAPSHOT);
                bool b = Native.RegisterHotKey(hotkeyWindow.Handle, 2, norepeat | 2, (uint)Native.VK_SNAPSHOT);
                bool c = Native.RegisterHotKey(hotkeyWindow.Handle, 3, norepeat | 1, (uint)Native.VK_SNAPSHOT);
                bool d = Native.RegisterHotKey(hotkeyWindow.Handle, 4, norepeat | 4, (uint)Native.VK_SNAPSHOT);
                Diag.Log("backup hotkeys registered: PrtScn=" + a + " Ctrl=" + b + " Alt=" + c + " Shift=" + d);
            }
            catch (Exception ex) { Diag.Log("backup hotkeys failed: " + ex.Message); }
        }

        // Windows drops a hook that was ever too slow. Watch the physical PrtScn / Win+Shift+S state and
        // reinstall the hook if it never saw the key. Nothing is sent to other programs.
        private int hookEvents;
        private int lastHookTick;
        private void HookWatch()
        {
            for (;;)
            {
                Thread.Sleep(50);
                bool down = Native.IsDown(0x2C)
                    || (Native.IsDown(0x53) && (Native.IsDown(0x5B) || Native.IsDown(0x5C)) && Native.IsDown(0x10));
                if (!down) continue;
                int seen = Environment.TickCount;
                if (seen - lastHookTick <= 150) { Thread.Sleep(500); continue; }
                Thread.Sleep(300);
                if (lastHookTick - seen >= 0) { Thread.Sleep(500); continue; }
                Diag.Log("keyboard hook stopped receiving keys: installing it again");
                RestartHook();
                Thread.Sleep(1000);
            }
        }

        private void RestartHook()
        {
            try
            {
                if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
                Thread old = hookThread;
                if (old != null) old.Join(1000);
                Array.Clear(keys, 0, keys.Length);
                swallowSKeyUp = false;
                StartHookThread();
            }
            catch (Exception ex) { Diag.Log("hook restart failed: " + ex.Message); }
        }

        // Restarts the app if the UI thread stops answering.
        private void Watchdog()
        {
            for (;;)
            {
                Thread.Sleep(4000);
                ManualResetEvent answered = new ManualResetEvent(false);
                Stopwatch lat = Stopwatch.StartNew();
                try { sync.BeginInvoke(new Action(delegate { answered.Set(); })); }
                catch (Exception) { return; }
                bool ok = answered.WaitOne(15000);
                if (ok && lat.ElapsedMilliseconds > 700) Diag.Log("UI thread was slow: " + lat.ElapsedMilliseconds + " ms to answer");
                if (!ok)
                {
                    Diag.Log("UI thread did not answer for 15 s: restarting the app");
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                            "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + Application.ExecutablePath + "\"");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                    Environment.Exit(1);
                }
            }
        }

        // 1 = elevated, 0 = not elevated, -1 = could not tell (access denied: protected or higher integrity).
        private static int IsElevated(uint pid) { return TokenFlag(pid, 20); }      // TokenElevation
        private static int HasUiAccess(uint pid) { return TokenFlag(pid, 26); }    // TokenUIAccess

        private static int TokenFlag(uint pid, int tokenClass)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return -1;
            try
            {
                IntPtr tok;
                if (!Native.OpenProcessToken(h, 0x0008, out tok)) return -1;   // TOKEN_QUERY
                try
                {
                    int elev, ret;
                    if (!Native.GetTokenInformation(tok, tokenClass, out elev, 4, out ret)) return -1;
                    return elev != 0 ? 1 : 0;
                }
                finally { Native.CloseHandle(tok); }
            }
            finally { Native.CloseHandle(h); }
        }

        // The hook runs on its own thread so a busy UI thread cannot get it dropped.
        private void StartHookThread()
        {
            ManualResetEvent ready = new ManualResetEvent(false);
            hookThread = new Thread(delegate()
            {
                hookThreadId = Native.GetCurrentThreadId();
                using (Process p = Process.GetCurrentProcess())
                using (ProcessModule m = p.MainModule)
                {
                    hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc,
                        Native.GetModuleHandle(m.ModuleName), 0);
                }
                ready.Set();
                Diag.Log("keyboard hook installed: " + (hookHandle != IntPtr.Zero) + ", elevated=" + IsElevated((uint)Process.GetCurrentProcess().Id)
                    + ", uiAccess=" + HasUiAccess((uint)Process.GetCurrentProcess().Id));
                if (hookHandle == IntPtr.Zero) return;
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                Diag.Log("keyboard hook thread ended");
                Native.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            });
            hookThread.IsBackground = true;
            hookThread.Name = "keyboard hook";
            hookThread.Start();
            ready.WaitOne(3000);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                hookEvents++; lastHookTick = Environment.TickCount;
                try
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    bool down = (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN);
                    bool up = (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP);
                    int vk = (int)k.vkCode;

                    // Track modifier state ourselves; GetAsyncKeyState is unreliable inside LL hooks.
                    if (vk >= 0 && vk < 256)
                    {
                        if (down) keys[vk] = true;
                        else if (up) keys[vk] = false;
                    }
                    bool win = keys[Native.VK_LWIN] || keys[Native.VK_RWIN];
                    bool shift = keys[0xA0] || keys[0xA1] || keys[Native.VK_SHIFT];
                    bool ctrl = keys[0xA2] || keys[0xA3] || keys[Native.VK_CONTROL];
                    bool alt = keys[0xA4] || keys[0xA5] || keys[Native.VK_MENU];
                    // Only shortcut keys are logged.
                    if ((vk == 0x53 && win && shift) || vk == 0x2C
                        || ((vk == 0x1B || vk == 0x0D) && openOverlay != null))
                    {
                        string line = "key " + vk.ToString("X2") + (down ? " down" : " up") + (((int)k.flags & 0x10) != 0 ? " injected" : "")
                            + " [win=" + win + " shift=" + shift + " ctrl=" + ctrl + " alt=" + alt + " swallowS=" + swallowSKeyUp + "]";
                        ThreadPool.QueueUserWorkItem(delegate { Diag.Log(line); });
                    }

                    if (vk == Native.VK_S)
                    {
                        // Win+Shift+S: take over Windows' own snip shortcut.
                        if (down && win && shift && !ctrl && !alt)
                        {
                            if (!swallowSKeyUp)
                            {
                                swallowSKeyUp = true;
                                Post(CaptureMode.Overlay);
                            }
                            return (IntPtr)1;
                        }
                        if (up && swallowSKeyUp)
                        {
                            swallowSKeyUp = false;
                            return (IntPtr)1;
                        }
                    }
                    else if (vk == Native.VK_SNAPSHOT)
                    {
                        CaptureMode mode = CaptureMode.Overlay;
                        if (ctrl) mode = CaptureMode.Monitor;
                        else if (alt) mode = CaptureMode.Window;
                        else if (shift) mode = CaptureMode.AllMonitors;

                        if (down)
                        {
                            if (win)
                            {
                                snapshotPassed = true; // Win+PrtScn: leave it to Windows
                            }
                            else
                            {
                                // Ignore key auto-repeat; only the first press captures.
                                if (!swallowKeyUp)
                                {
                                    swallowKeyUp = true;
                                    Post(mode);
                                }
                                return (IntPtr)1; // swallow so Windows' Snipping Tool does not open
                            }
                        }
                        else if (up)
                        {
                            if (snapshotPassed)
                            {
                                snapshotPassed = false;
                            }
                            else if (swallowKeyUp)
                            {
                                swallowKeyUp = false;
                                return (IntPtr)1;
                            }
                            else if (!win)
                            {
                                // Some keyboards/drivers only report the key-up for PrtScn.
                                Post(mode);
                                return (IntPtr)1;
                            }
                        }
                    }
                }
                catch (Exception) { }
            }
            return Native.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

    }
}

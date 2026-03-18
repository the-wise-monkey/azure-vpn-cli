using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace VpnCli
{
    class Program
    {
        static bool DebugMode;
        static string ExeDir;
        static string PbkPath;
        static string SetupDonePath;

        #region Win32

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_LAYERED = 0x80000;
        const uint LWA_ALPHA = 0x2;
        const int SW_MINIMIZE = 6;
        const int SW_RESTORE = 9;

        #endregion

        [STAThread]
        static int Main(string[] args)
        {
            ExeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            PbkPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.AzureVpn_8wekyb3d8bbwe\LocalState\rasphone.pbk");
            SetupDonePath = Path.Combine(ExeDir, ".setup-done");

            var argList = args.ToList();
            DebugMode = argList.Remove("-d");
            string arg1 = argList.Count > 0 ? argList[0] : null;
            string arg2 = argList.Count > 1 ? argList[1] : null;

            Log($"Args: Arg1='{arg1}' Arg2='{arg2}'");
            EnsurePrerequisites();

            var cmds = new VpnCommands(
                PbkPath,
                GetActiveConnections,
                InvokeVpnAction,
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Console.Out);

            int exitCode = 0;
            try
            {
                switch (arg1?.ToLowerInvariant())
                {
                    case "setup":
                        Log("Command: setup");
                        if (File.Exists(SetupDonePath)) File.Delete(SetupDonePath);
                        EnsurePrerequisites();
                        Console.WriteLine("Setup complete.");
                        break;

                    case "list":
                        exitCode = cmds.CmdList();
                        break;

                    case "export":
                        exitCode = cmds.CmdExport(arg2);
                        break;

                    case "import":
                        exitCode = cmds.CmdImport(arg2);
                        break;

                    default:
                        exitCode = cmds.CmdDefault(arg1, arg2);
                        break;
                }
            }
            finally
            {
                RestoreVpnWindow();
            }

            return exitCode;
        }

        // ── Prerequisites ────────────────────────────────────────

        static void EnsurePrerequisites()
        {
            if (File.Exists(SetupDonePath))
            {
                Log("Setup flag found, skipping prerequisites check");
                return;
            }
            Log("First run - checking prerequisites");

            string packageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.AzureVpn_8wekyb3d8bbwe");

            if (Directory.Exists(packageDir))
            {
                Log("Prerequisites OK, writing setup flag");
                File.WriteAllText(SetupDonePath, DateTime.Now.ToString("o"));
                return;
            }

            Console.WriteLine();
            WriteColored("  Missing prerequisite: Azure VPN Client", ConsoleColor.Yellow);
            WriteColored("  Installing Azure VPN Client...", ConsoleColor.Cyan);

            string result = RunProcess("winget",
                "install \"9NP355QT2SQB\" --source msstore --accept-package-agreements --accept-source-agreements");

            if (Directory.Exists(packageDir))
            {
                WriteColored("  Azure VPN Client installed.", ConsoleColor.Green);
                File.WriteAllText(SetupDonePath, DateTime.Now.ToString("o"));
            }
            else
            {
                WriteColored("  Failed to install Azure VPN Client. Install from the Microsoft Store.", ConsoleColor.Red);
                Log($"winget output: {result}");
                Environment.Exit(1);
            }
            Console.WriteLine();
        }

        // ── UI Automation ────────────────────────────────────────

        static AutomationElement GetVpnWindow()
        {
            var proc = FindVpnProcess();
            if (proc == null)
            {
                Log("Launching Azure VPN Client");
                Process.Start("explorer.exe",
                    @"shell:AppsFolder\Microsoft.AzureVpn_8wekyb3d8bbwe!App");
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(500);
                    proc = FindVpnProcess();
                    if (proc != null) break;
                }
                if (proc == null) return null;
                Thread.Sleep(1500);
            }

            IntPtr hwnd = proc.MainWindowHandle;
            ShowWindow(hwnd, SW_RESTORE);

            if (DebugMode)
            {
                GetWindowRect(hwnd, out RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                MoveWindow(hwnd, 100, 100, w, h, true);
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((style & WS_EX_LAYERED) != 0)
                    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            }
            else
            {
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
                SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
                Log("Window set to transparent");
            }
            Thread.Sleep(400);

            var root = AutomationElement.RootElement;
            var cond = new PropertyCondition(AutomationElement.NameProperty, "Azure VPN Client");
            var win = root.FindFirst(TreeScope.Children, cond);
            Log($"UI Automation window: {win != null}");
            return win;
        }

        static AutomationElement FindElement(AutomationElement parent, ControlType type, string namePattern)
        {
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
            var all = parent.FindAll(TreeScope.Descendants, cond);
            var regex = new Regex(namePattern);
            foreach (AutomationElement el in all)
            {
                if (regex.IsMatch(el.Current.Name))
                    return el;
            }
            return null;
        }

        static void InvokeButton(AutomationElement el)
        {
            var pattern = (InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern);
            pattern.Invoke();
        }

        static void DbgStep(string msg)
        {
            if (DebugMode)
            {
                Log(msg);
                Thread.Sleep(1500);
            }
        }

        static int InvokeVpnAction(string action, string name)
        {
            Log($"InvokeVpnAction: action={action} name={name}");

            var win = GetVpnWindow();
            if (win == null)
            {
                Console.WriteLine("Failed to open Azure VPN Client.");
                return 1;
            }

            if (action == "connect" || action == "disconnect")
            {
                string escapedName = Regex.Escape(name);
                DbgStep($"Looking for profile '{name}'...");
                var item = FindElement(win, ControlType.ListItem, $"^{escapedName}");
                if (item == null)
                {
                    Log($"ListItem for '{name}' not found");
                    return 1;
                }
                Log($"Found ListItem: '{item.Current.Name}'");

                string itemName = item.Current.Name;
                bool isDisconnected = itemName.EndsWith("Disconnected");
                bool isConnected = !isDisconnected && itemName.EndsWith("Connected");
                Log($"State: connected={isConnected} disconnected={isDisconnected}");

                if (action == "connect" && isConnected)
                {
                    Console.WriteLine("Already connected.");
                    return 0;
                }
                if (action == "disconnect" && isDisconnected)
                {
                    Console.WriteLine("Already disconnected.");
                    return 0;
                }

                DbgStep("Selecting profile...");
                try
                {
                    var selectPattern = (SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern);
                    selectPattern.Select();
                }
                catch
                {
                    Log("SelectionItemPattern failed, trying Invoke");
                    try { InvokeButton(item); }
                    catch (Exception ex) { Log($"Invoke also failed: {ex.Message}"); }
                }
                Thread.Sleep(500);

                bool alreadyInProgress = false;
                if (action == "connect" && item.Current.Name.Contains("Connecting"))
                {
                    Log("Profile already connecting (sign-in may be pending)");
                    alreadyInProgress = true;
                }

                if (!alreadyInProgress)
                {
                    string buttonName = action == "connect" ? "^Connect " : "^Disconnect ";
                    DbgStep($"Looking for '{action}' button...");
                    var btn = FindElement(win, ControlType.Button, buttonName);
                    if (btn == null)
                    {
                        Log($"Button matching '{buttonName}' not found");
                        return 1;
                    }
                    Log($"Found button: '{btn.Current.Name}'");
                    DbgStep($"Clicking '{action}' button...");
                    InvokeButton(btn);
                }

                if (action == "connect")
                {
                    Log("Waiting for connection...");
                    bool signInNotified = false;
                    for (int wait = 0; wait < 60; wait++)
                    {
                        Thread.Sleep(1000);

                        string ras = RunProcess("rasdial", "");
                        if (Regex.IsMatch(ras, Regex.Escape(name)))
                        {
                            Log($"Connected after {wait}s");
                            break;
                        }

                        var signInItem = FindElement(win, ControlType.ListItem, "account");
                        if (signInItem != null && !signInNotified)
                        {
                            Log("Sign-in prompt detected");
                            WriteColored("Waiting for sign-in...", ConsoleColor.Yellow);
                            var p = FindVpnProcess();
                            if (p != null)
                            {
                                IntPtr h = p.MainWindowHandle;
                                SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
                                ShowWindow(h, SW_RESTORE);
                                var screen = Screen.PrimaryScreen.WorkingArea;
                                GetWindowRect(h, out RECT rect);
                                int ww = rect.Right - rect.Left;
                                int wh = rect.Bottom - rect.Top;
                                int cx = (screen.Width - ww) / 2 + screen.X;
                                int cy = (screen.Height - wh) / 2 + screen.Y;
                                MoveWindow(h, cx, cy, ww, wh, true);
                                Log($"Window centered at ({cx}, {cy})");
                                SetForegroundWindow(h);
                            }
                            signInNotified = true;
                        }

                        var profileItem = FindElement(win, ControlType.ListItem,
                            $"^{escapedName}Disconnected");
                        if (profileItem != null)
                        {
                            Log("Profile reverted to Disconnected");
                            return 1;
                        }
                    }
                }
                else
                {
                    DbgStep("Waiting for action to complete...");
                    Thread.Sleep(1000);
                }

                string result = action == "connect" ? "Connected" : "Disconnected";
                Console.WriteLine(result);

                if (!DebugMode)
                {
                    var p2 = FindVpnProcess();
                    if (p2 != null)
                        ShowWindow(p2.MainWindowHandle, SW_MINIMIZE);
                }
                return 0;
            }

            if (action == "import")
            {
                DbgStep("Looking for Add/Import button...");
                var btn = FindElement(win, ControlType.Button, "Add or Import");
                if (btn == null)
                {
                    Log("Add/Import button not found");
                    return 1;
                }
                Log($"Found button: '{btn.Current.Name}'");

                DbgStep("Opening menu...");
                InvokeButton(btn);
                Thread.Sleep(500);

                DbgStep("Looking for Import menu item...");
                var importItem = FindElement(win, ControlType.MenuItem, "^Import$");
                if (importItem == null)
                {
                    Log("Import menu item not found");
                    return 1;
                }
                Log($"Found menu item: '{importItem.Current.Name}'");

                DbgStep("Clicking Import...");
                InvokeButton(importItem);
                Thread.Sleep(1000);

                DbgStep("Waiting for file dialog...");
                AutomationElement openDlg = null;
                for (int i = 0; i < 10; i++)
                {
                    openDlg = FindElement(win, ControlType.Window, "^Open$");
                    if (openDlg != null) break;
                    Thread.Sleep(500);
                }
                if (openDlg == null)
                {
                    Log("Open dialog not found");
                    return 1;
                }
                Log("Found file dialog");

                string filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"{name}.AzureVpnProfile.xml");
                DbgStep($"Typing path: {filePath}");

                try { openDlg.SetFocus(); }
                catch (Exception ex) { Log($"SetFocus failed: {ex.Message}"); }
                Thread.Sleep(300);
                SendKeys.SendWait(filePath);
                Thread.Sleep(300);
                DbgStep("Pressing Enter...");
                SendKeys.SendWait("{ENTER}");

                DbgStep("Waiting for profile editor...");
                AutomationElement saveBtn = null;
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(500);
                    saveBtn = FindElement(win, ControlType.Button, "^Save$");
                    if (saveBtn != null) break;
                }
                if (saveBtn == null)
                {
                    Log("Save button not found");
                    return 1;
                }
                Log("Found Save button");

                DbgStep("Clicking Save...");
                InvokeButton(saveBtn);
                DbgStep("Waiting for import to complete...");
                Thread.Sleep(1000);

                if (!DebugMode)
                {
                    var p2 = FindVpnProcess();
                    if (p2 != null)
                        ShowWindow(p2.MainWindowHandle, SW_MINIMIZE);
                }
                return 0;
            }

            return 1;
        }

        // ── Helpers ──────────────────────────────────────────────

        static void Log(string msg)
        {
            if (!DebugMode) return;
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[{ts}] {msg}");
            Console.ForegroundColor = prev;
        }

        static void WriteColored(string msg, ConsoleColor color)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ForegroundColor = prev;
        }

        static Process FindVpnProcess()
        {
            return Process.GetProcesses().FirstOrDefault(p =>
            {
                try { return p.MainWindowTitle.Contains("Azure VPN Client"); }
                catch { return false; }
            });
        }

        static void RestoreVpnWindow()
        {
            var proc = FindVpnProcess();
            if (proc != null)
            {
                Log("Restoring window opacity");
                SetLayeredWindowAttributes(proc.MainWindowHandle, 0, 255, LWA_ALPHA);
            }
        }

        static string GetActiveConnections()
        {
            Log("Checking active connections via rasdial");
            string output = RunProcess("rasdial", "");
            Log($"rasdial output: {output.Trim()}");
            return output;
        }

        static string RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    return output + error;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}

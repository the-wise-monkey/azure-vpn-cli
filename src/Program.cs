using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace VpnCli
{
    class Program
    {
        private static bool DebugMode;
        private static string ExeDir;
        private static string SetupDonePath;

        static int Main(string[] args)
        {
            ExeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            SetupDonePath = Path.Combine(ExeDir, ".setup-done");

            var argList = args.ToList();
            DebugMode = argList.Remove("-d");
            string arg1 = argList.Count > 0 ? argList[0] : null;
            string arg2 = argList.Count > 1 ? argList[1] : null;
            string arg3 = argList.Count > 2 ? argList[2] : null;

            Log("Args: " + string.Join(" ", argList));

            var helper = new VpnHelperClient(
                Environment.GetEnvironmentVariable("VPN_HELPER_EXE"),
                Log);

            var cmds = new VpnCommands(
                helper,
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Console.Out);

            switch ((arg1 ?? "").ToLowerInvariant())
            {
                case "setup":
                    Log("Command: setup");
                    if (File.Exists(SetupDonePath))
                    {
                        File.Delete(SetupDonePath);
                    }
                    return EnsurePrerequisites(helper, force: true);

                case "list":
                    if (EnsurePrerequisites(helper, force: false) != 0) return 1;
                    return cmds.CmdList();

                case "import":
                    if (EnsurePrerequisites(helper, force: false) != 0) return 1;
                    return cmds.CmdImport(arg2, arg3);

                case "export":
                    if (EnsurePrerequisites(helper, force: false) != 0) return 1;
                    return cmds.CmdExport(arg2, arg3);

                case "delete":
                    if (EnsurePrerequisites(helper, force: false) != 0) return 1;
                    return cmds.CmdDelete(arg2);

                default:
                    if (string.IsNullOrEmpty(arg1) || string.IsNullOrEmpty(arg2))
                    {
                        return cmds.CmdDefault(arg1, arg2);
                    }
                    if (EnsurePrerequisites(helper, force: false) != 0) return 1;
                    return cmds.CmdDefault(arg1, arg2);
            }
        }

        private static int EnsurePrerequisites(IVpnHelperClient helper, bool force)
        {
            if (!force && File.Exists(SetupDonePath))
            {
                Log("Setup flag found, validating prerequisites");
            }

            Log("Checking prerequisites");
            bool ok = true;

            if (!IsAzureVpnClientInstalled())
            {
                Console.WriteLine("Missing prerequisite: Azure VPN Client.");
                Console.WriteLine("Install it from Microsoft Store or run:");
                Console.WriteLine("  winget install 9NP355QT2SQB --source msstore");
                ok = false;
            }

            HelperResponse health = helper.Health();
            if (health == null || !health.Ok)
            {
                Console.WriteLine("Missing prerequisite: VpnPackagedHelper.");
                Console.WriteLine("If AzureVPN-CLI was installed from a release, reinstall or repair the installer.");
                Console.WriteLine("For development, enable Developer Mode and run:");
                Console.WriteLine("  .\\tools\\vpn-packaged-helper\\scripts\\Build-Helper.ps1");
                Console.WriteLine("  .\\tools\\vpn-packaged-helper\\scripts\\Install-Helper.ps1 -RegisterLoose");
                ok = false;
            }

            if (!ok)
            {
                return 1;
            }

            File.WriteAllText(SetupDonePath, DateTime.Now.ToString("o"));
            if (force)
            {
                Console.WriteLine("Setup complete.");
            }
            return 0;
        }

        private static bool IsAzureVpnClientInstalled()
        {
            string userPackageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Packages\Microsoft.AzureVpn_8wekyb3d8bbwe");

            if (Directory.Exists(userPackageDir))
            {
                return true;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string windowsApps = Path.Combine(programFiles, "WindowsApps");
            try
            {
                return Directory.Exists(windowsApps) &&
                    Directory.EnumerateDirectories(windowsApps, "Microsoft.AzureVpn_*").Any();
            }
            catch
            {
                return false;
            }
        }

        private static void Log(string msg)
        {
            if (!DebugMode) return;
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[" + ts + "] " + msg);
            Console.ForegroundColor = prev;
        }
    }
}

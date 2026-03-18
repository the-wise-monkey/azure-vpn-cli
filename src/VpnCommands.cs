using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VpnCli
{
    internal class VpnCommands
    {
        private readonly string _pbkPath;
        private readonly Func<string> _getActiveConnections;
        private readonly Func<string, string, int> _invokeVpnAction;
        private readonly string _exportDir;
        private readonly TextWriter _output;

        internal VpnCommands(
            string pbkPath,
            Func<string> getActiveConnections,
            Func<string, string, int> invokeVpnAction,
            string exportDir,
            TextWriter output)
        {
            _pbkPath = pbkPath;
            _getActiveConnections = getActiveConnections;
            _invokeVpnAction = invokeVpnAction;
            _exportDir = exportDir;
            _output = output;
        }

        internal int CmdList()
        {
            var profiles = GetVpnProfiles();
            if (profiles.Count == 0)
            {
                _output.WriteLine("No VPN profiles found in Azure VPN Client.");
                return 0;
            }
            string active = _getActiveConnections();
            _output.WriteLine();
            foreach (var name in profiles)
            {
                string status = Regex.IsMatch(active, Regex.Escape(name))
                    ? "Connected" : "Disconnected";
                _output.WriteLine(string.Format("  {0,-36} {1}", name, status));
            }
            _output.WriteLine();
            return 0;
        }

        internal int CmdExport(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                _output.WriteLine("Usage: vpn export <name>");
                return 1;
            }
            var profiles = GetVpnProfiles();
            if (!profiles.Contains(name))
            {
                _output.WriteLine($"Profile '{name}' not found. Run: vpn list");
                return 1;
            }
            string xml = ExportVpnProfile(name);
            if (xml == null)
            {
                _output.WriteLine($"Failed to extract profile data for '{name}'.");
                return 1;
            }
            string outPath = Path.Combine(_exportDir, $"{name}.AzureVpnProfile.xml");
            File.WriteAllText(outPath, xml, Encoding.UTF8);
            _output.WriteLine($"Exported to {outPath}");
            return 0;
        }

        internal int CmdImport(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                _output.WriteLine("Usage: vpn import <name>");
                return 1;
            }
            var profiles = GetVpnProfiles();
            if (profiles.Contains(name))
            {
                _output.WriteLine($"Profile '{name}' already exists.");
                return 0;
            }
            _output.WriteLine($"Importing {name} from Desktop...");
            int code = _invokeVpnAction("import", name);
            if (code != 0)
            {
                _output.WriteLine($"Import failed. Make sure '{name}.AzureVpnProfile.xml' is on your Desktop.");
                return 1;
            }
            _output.WriteLine("Imported");
            return 0;
        }

        internal int CmdDefault(string name, string action)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(action))
            {
                ShowHelp();
                return 0;
            }
            var profiles = GetVpnProfiles();
            if (!profiles.Contains(name))
            {
                _output.WriteLine($"Profile '{name}' not found. Run: vpn list");
                return 1;
            }
            action = action.ToLowerInvariant();
            if (action == "status")
            {
                string active = _getActiveConnections();
                string s = Regex.IsMatch(active, Regex.Escape(name))
                    ? "Connected" : "Disconnected";
                _output.WriteLine(s);
                return 0;
            }
            if (action != "connect" && action != "disconnect")
            {
                _output.WriteLine($"Unknown action '{action}'. Use: connect, disconnect, status");
                return 1;
            }
            int code = _invokeVpnAction(action, name);
            if (code != 0)
            {
                _output.WriteLine("Action failed.");
                return 1;
            }
            return 0;
        }

        internal void ShowHelp()
        {
            _output.WriteLine();
            _output.WriteLine("  Usage:");
            _output.WriteLine("    vpn <name> connect        Connect a profile");
            _output.WriteLine("    vpn <name> disconnect     Disconnect a profile");
            _output.WriteLine("    vpn <name> status         Show connection status");
            _output.WriteLine("    vpn list                  List all profiles in Azure VPN Client");
            _output.WriteLine("    vpn import <name>         Import <name>.AzureVpnProfile.xml from Desktop");
            _output.WriteLine("    vpn export <name>         Export profile XML to Desktop");
            _output.WriteLine("    vpn setup                 Re-check and install prerequisites");
            _output.WriteLine();
            _output.WriteLine("  Examples:");
            _output.WriteLine("    vpn list");
            _output.WriteLine("    vpn my-vpn connect");
            _output.WriteLine("    vpn my-vpn status");
            _output.WriteLine("    vpn import my-vpn");
            _output.WriteLine();
        }

        internal List<string> GetVpnProfiles()
        {
            if (!File.Exists(_pbkPath))
                return new List<string>();
            var names = new List<string>();
            var regex = new Regex(@"^\[(.+)\]$");
            foreach (var line in File.ReadLines(_pbkPath))
            {
                var m = regex.Match(line);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        internal string ExportVpnProfile(string profileName)
        {
            if (!File.Exists(_pbkPath))
                return null;
            var lines = File.ReadAllLines(_pbkPath);
            bool inSection = false;
            var hex = new StringBuilder();
            var sectionPattern = new Regex($@"^\[{Regex.Escape(profileName)}\]$");
            foreach (var line in lines)
            {
                if (sectionPattern.IsMatch(line)) { inSection = true; continue; }
                if (inSection && line.StartsWith("[")) break;
                if (inSection)
                {
                    var m = Regex.Match(line, @"^ThirdPartyProfileInfo=(.+)");
                    if (m.Success) hex.Append(m.Groups[1].Value);
                }
            }
            if (hex.Length == 0)
                return null;
            string hexStr = hex.ToString();
            var bytes = new byte[hexStr.Length / 2];
            for (int i = 0; i < hexStr.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hexStr.Substring(i, 2), 16);
            string decoded = Encoding.Unicode.GetString(bytes);
            int xmlStart = decoded.IndexOf("<azvpnprofile>");
            if (xmlStart < 0)
                return null;
            return decoded.Substring(xmlStart).TrimEnd('\0');
        }
    }
}

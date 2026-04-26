using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VpnCli
{
    internal class VpnCommands
    {
        private readonly IVpnHelperClient _helper;
        private readonly string _exportDir;
        private readonly TextWriter _output;

        internal VpnCommands(
            IVpnHelperClient helper,
            string exportDir,
            TextWriter output)
        {
            _helper = helper;
            _exportDir = exportDir;
            _output = output;
        }

        internal int CmdList()
        {
            HelperResponse response = _helper.List();
            if (!EnsureOk(response))
            {
                return 1;
            }

            if (response.Profiles.Count == 0)
            {
                _output.WriteLine("No VPN profiles imported. Run: vpn import <profile.AzureVpnProfile.xml>");
                return 0;
            }

            _output.WriteLine();
            foreach (VpnProfileInfo profile in response.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                _output.WriteLine(string.Format("  {0,-36} {1}", profile.Name, profile.Status));
            }
            _output.WriteLine();
            return 0;
        }

        internal int CmdImport(string xmlPath, string name)
        {
            if (string.IsNullOrEmpty(xmlPath))
            {
                _output.WriteLine("Usage: vpn import <profile.AzureVpnProfile.xml> [name]");
                return 1;
            }

            string resolvedPath = ResolveImportPath(xmlPath);
            HelperResponse response = _helper.Import(resolvedPath, name);
            if (!EnsureOk(response))
            {
                return 1;
            }

            _output.WriteLine("Imported " + response.Profile);
            return 0;
        }

        internal int CmdExport(string name, string outputPath)
        {
            if (string.IsNullOrEmpty(name))
            {
                _output.WriteLine("Usage: vpn export <name> [output.AzureVpnProfile.xml]");
                return 1;
            }

            string resolvedOutput = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(_exportDir, name + ".AzureVpnProfile.xml")
                : Path.GetFullPath(outputPath);

            HelperResponse response = _helper.Export(name, resolvedOutput);
            if (!EnsureOk(response))
            {
                return 1;
            }

            _output.WriteLine("Exported to " + response.Path);
            return 0;
        }

        internal int CmdDelete(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                _output.WriteLine("Usage: vpn delete <name>");
                return 1;
            }

            HelperResponse response = _helper.Delete(name);
            if (!EnsureOk(response))
            {
                return 1;
            }

            _output.WriteLine("Deleted " + response.Profile);
            return 0;
        }

        internal int CmdDefault(string name, string action)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(action))
            {
                ShowHelp();
                return 0;
            }

            action = action.ToLowerInvariant();
            HelperResponse response;
            switch (action)
            {
                case "status":
                    response = _helper.Status(name);
                    if (!EnsureOk(response)) return 1;
                    _output.WriteLine(response.Status);
                    return 0;

                case "connect":
                    response = _helper.Connect(name);
                    if (!EnsureOk(response)) return 1;
                    _output.WriteLine(response.Result == "AlreadyConnected" ? "Already connected." :
                        response.Status == "Connected" ? "Connected" : response.Result);
                    return 0;

                case "disconnect":
                    response = _helper.Disconnect(name);
                    if (!EnsureOk(response)) return 1;
                    _output.WriteLine(response.Result == "AlreadyDisconnected" ? "Already disconnected." :
                        response.Status == "Disconnected" ? "Disconnected" : response.Result);
                    return 0;

                default:
                    _output.WriteLine("Unknown action '" + action + "'. Use: connect, disconnect, status");
                    return 1;
            }
        }

        internal void ShowHelp()
        {
            _output.WriteLine();
            _output.WriteLine("  Usage:");
            _output.WriteLine("    vpn import <xml> [name]      Import an Azure VPN Client XML profile into the packaged helper");
            _output.WriteLine("    vpn list                     List imported helper-owned profiles");
            _output.WriteLine("    vpn <name> connect           Connect a profile");
            _output.WriteLine("    vpn <name> disconnect        Disconnect a profile");
            _output.WriteLine("    vpn <name> status            Show connection status");
            _output.WriteLine("    vpn export <name> [xml]      Export imported profile XML");
            _output.WriteLine("    vpn delete <name>            Delete an imported profile");
            _output.WriteLine("    vpn setup                    Re-check prerequisites");
            _output.WriteLine();
            _output.WriteLine("  Examples:");
            _output.WriteLine("    vpn import .\\my-vpn.AzureVpnProfile.xml");
            _output.WriteLine("    vpn list");
            _output.WriteLine("    vpn my-vpn connect");
            _output.WriteLine("    vpn my-vpn status");
            _output.WriteLine();
        }

        private bool EnsureOk(HelperResponse response)
        {
            if (response != null && response.Ok)
            {
                return true;
            }

            string message = response == null
                ? "Helper failed without a response."
                : !string.IsNullOrEmpty(response.Message)
                    ? response.Message
                    : response.Code;
            _output.WriteLine(message);
            return false;
        }

        private string ResolveImportPath(string xmlPathOrName)
        {
            string expanded = Environment.ExpandEnvironmentVariables(xmlPathOrName);
            if (File.Exists(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            if (!expanded.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                string directory = Path.GetDirectoryName(expanded);
                if (string.IsNullOrEmpty(directory))
                {
                    return Path.Combine(_exportDir, expanded + ".AzureVpnProfile.xml");
                }

                return Path.GetFullPath(expanded + ".AzureVpnProfile.xml");
            }

            return Path.GetFullPath(expanded);
        }
    }

    internal interface IVpnHelperClient
    {
        HelperResponse Health();
        HelperResponse List();
        HelperResponse Import(string xmlPath, string name);
        HelperResponse Export(string name, string outputPath);
        HelperResponse Status(string name);
        HelperResponse Connect(string name);
        HelperResponse Disconnect(string name);
        HelperResponse Delete(string name);
    }

    internal class VpnProfileInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
    }

    internal class HelperResponse
    {
        public bool Ok { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Result { get; set; }
        public string Profile { get; set; }
        public string Status { get; set; }
        public string Path { get; set; }
        public List<VpnProfileInfo> Profiles { get; private set; }

        public HelperResponse()
        {
            Profiles = new List<VpnProfileInfo>();
        }
    }
}

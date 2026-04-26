using System;
using System.IO;
using System.Linq;

namespace VpnCli
{
    internal class VpnCommands
    {
        private readonly IVpnAgent _agent;
        private readonly string _exportDir;
        private readonly TextWriter _output;

        internal VpnCommands(IVpnAgent agent, string exportDir, TextWriter output)
        {
            _agent = agent;
            _exportDir = exportDir;
            _output = output;
        }

        internal int CmdList()
        {
            AgentResponse response = _agent.List();
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
            AgentResponse response = _agent.Import(resolvedPath, name);
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

            AgentResponse response = _agent.Export(name, resolvedOutput);
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

            AgentResponse response = _agent.Delete(name);
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
            AgentResponse response;
            switch (action)
            {
                case "status":
                    response = _agent.Status(name);
                    if (!EnsureOk(response)) return 1;
                    _output.WriteLine(response.Status);
                    return 0;

                case "connect":
                    response = _agent.Connect(name);
                    if (!EnsureOk(response)) return 1;
                    _output.WriteLine(response.Result == "AlreadyConnected" ? "Already connected." :
                        response.Status == "Connected" ? "Connected" : response.Result);
                    return 0;

                case "disconnect":
                    response = _agent.Disconnect(name);
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
            _output.WriteLine("    vpn import <xml> [name]      Import an Azure VPN profile XML");
            _output.WriteLine("    vpn list                     List imported profiles");
            _output.WriteLine("    vpn <name> connect           Connect a profile");
            _output.WriteLine("    vpn <name> disconnect        Disconnect a profile");
            _output.WriteLine("    vpn <name> status            Show connection status");
            _output.WriteLine("    vpn export <name> [xml]      Export profile XML");
            _output.WriteLine("    vpn delete <name>            Delete a profile");
            _output.WriteLine();
            _output.WriteLine("  Examples:");
            _output.WriteLine("    vpn import .\\my-vpn.AzureVpnProfile.xml");
            _output.WriteLine("    vpn list");
            _output.WriteLine("    vpn my-vpn connect");
            _output.WriteLine("    vpn my-vpn status");
            _output.WriteLine();
        }

        private bool EnsureOk(AgentResponse response)
        {
            if (response != null && response.Ok)
            {
                return true;
            }

            string message = response == null
                ? "Agent failed without a response."
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
}

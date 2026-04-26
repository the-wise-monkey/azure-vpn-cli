using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.Networking.Vpn;

namespace VpnCli
{
    internal class VpnAgent : IVpnAgent
    {
        private const string AzureVpnPackageFamilyName = "Microsoft.AzureVpn_8wekyb3d8bbwe";

        private readonly VpnManagementAgent _agent = new VpnManagementAgent();

        public AgentResponse List()
        {
            return Run(async () =>
            {
                var profiles = (await _agent.GetProfilesAsync().AsTask())
                    .OrderBy(p => p.ProfileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var response = new AgentResponse { Ok = true };
                foreach (var profile in profiles)
                {
                    response.Profiles.Add(new VpnProfileInfo
                    {
                        Name = profile.ProfileName,
                        Status = StatusOf(profile)
                    });
                }
                return response;
            });
        }

        public AgentResponse Import(string xmlPath, string name)
        {
            return Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(xmlPath))
                {
                    return Fail("MissingXmlPath", "XML path is required.");
                }

                string resolved = Path.GetFullPath(xmlPath);
                if (!File.Exists(resolved))
                {
                    return Fail("XmlNotFound", "XML file not found: " + resolved);
                }

                VpnPlugInProfile profile = CreateAzurePluginProfile(resolved, name);
                IVpnProfile existing = await FindProfile(profile.ProfileName);
                if (existing != null)
                {
                    var deleteStatus = await _agent.DeleteProfileAsync(existing).AsTask();
                    if (deleteStatus != VpnManagementErrorStatus.Ok)
                    {
                        return Fail("DeleteExistingFailed", deleteStatus.ToString(), profile.ProfileName);
                    }
                }

                var addStatus = await _agent.AddProfileFromObjectAsync(profile).AsTask();
                if (addStatus != VpnManagementErrorStatus.Ok)
                {
                    return Fail("ImportFailed", addStatus.ToString(), profile.ProfileName);
                }

                return new AgentResponse
                {
                    Ok = true,
                    Code = "Ok",
                    Result = "Ok",
                    Profile = profile.ProfileName,
                    Status = "Disconnected"
                };
            });
        }

        public AgentResponse Export(string name, string outputPath)
        {
            return Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Fail("MissingName", "Profile name is required.");
                }

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return Fail("MissingOutputPath", "Output path is required.");
                }

                IVpnProfile profile = await FindProfile(name);
                VpnPlugInProfile plugIn = profile as VpnPlugInProfile;
                if (plugIn == null)
                {
                    return Fail("ProfileNotFound", "Profile not found: " + name);
                }

                string resolved = Path.GetFullPath(outputPath);
                string directory = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(resolved, plugIn.CustomConfiguration ?? "", Encoding.UTF8);
                return new AgentResponse
                {
                    Ok = true,
                    Code = "Ok",
                    Result = "Ok",
                    Profile = name,
                    Path = resolved
                };
            });
        }

        public AgentResponse Status(string name)
        {
            return Run(async () =>
            {
                IVpnProfile profile = await FindProfile(name);
                if (profile == null)
                {
                    return Fail("ProfileNotFound", "Profile not found: " + name);
                }

                return new AgentResponse
                {
                    Ok = true,
                    Code = "Ok",
                    Result = "Ok",
                    Profile = profile.ProfileName,
                    Status = StatusOf(profile)
                };
            });
        }

        public AgentResponse Connect(string name)
        {
            return ConnectOrDisconnect(name, true);
        }

        public AgentResponse Disconnect(string name)
        {
            return ConnectOrDisconnect(name, false);
        }

        public AgentResponse Delete(string name)
        {
            return Run(async () =>
            {
                IVpnProfile profile = await FindProfile(name);
                if (profile == null)
                {
                    return Fail("ProfileNotFound", "Profile not found: " + name);
                }

                var result = await _agent.DeleteProfileAsync(profile).AsTask();
                if (result != VpnManagementErrorStatus.Ok)
                {
                    return Fail("DeleteFailed", result.ToString(), profile.ProfileName);
                }

                return new AgentResponse
                {
                    Ok = true,
                    Code = "Ok",
                    Result = "Ok",
                    Profile = profile.ProfileName,
                    Status = "Deleted"
                };
            });
        }

        private AgentResponse ConnectOrDisconnect(string name, bool connect)
        {
            return Run(async () =>
            {
                IVpnProfile profile = await FindProfile(name);
                if (profile == null)
                {
                    return Fail("ProfileNotFound", "Profile not found: " + name);
                }

                string currentStatus = StatusOf(profile);
                if (connect && currentStatus == "Connected")
                {
                    return new AgentResponse
                    {
                        Ok = true,
                        Code = "AlreadyConnected",
                        Result = "AlreadyConnected",
                        Profile = profile.ProfileName,
                        Status = currentStatus
                    };
                }

                if (!connect && currentStatus == "Disconnected")
                {
                    return new AgentResponse
                    {
                        Ok = true,
                        Code = "AlreadyDisconnected",
                        Result = "AlreadyDisconnected",
                        Profile = profile.ProfileName,
                        Status = currentStatus
                    };
                }

                var result = connect
                    ? await _agent.ConnectProfileAsync(profile).AsTask()
                    : await _agent.DisconnectProfileAsync(profile).AsTask();

                IVpnProfile refreshed = await FindProfile(profile.ProfileName) ?? profile;
                if (result != VpnManagementErrorStatus.Ok)
                {
                    return Fail("ActionFailed", result.ToString(), profile.ProfileName, StatusOf(refreshed));
                }

                return new AgentResponse
                {
                    Ok = true,
                    Code = "Ok",
                    Result = "Ok",
                    Profile = profile.ProfileName,
                    Status = StatusOf(refreshed)
                };
            });
        }

        private async Task<IVpnProfile> FindProfile(string name)
        {
            var profiles = await _agent.GetProfilesAsync().AsTask();
            return profiles.FirstOrDefault(p => string.Equals(p.ProfileName, name, StringComparison.OrdinalIgnoreCase));
        }

        private static AgentResponse Run(Func<Task<AgentResponse>> action)
        {
            try
            {
                return action().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new AgentResponse
                {
                    Ok = false,
                    Code = "AgentException",
                    Message = ex.Message
                };
            }
        }

        private static AgentResponse Fail(string code, string message, string profile = null, string status = null)
        {
            return new AgentResponse
            {
                Ok = false,
                Code = code,
                Message = message,
                Profile = profile,
                Status = status
            };
        }

        private static string StatusOf(IVpnProfile profile)
        {
            VpnPlugInProfile plugIn = profile as VpnPlugInProfile;
            return plugIn != null ? plugIn.ConnectionStatus.ToString() : "Unknown";
        }

        private static VpnPlugInProfile CreateAzurePluginProfile(string xmlPath, string explicitProfileName)
        {
            string xml = File.ReadAllText(xmlPath);
            XmlDocument document = new XmlDocument();
            document.PreserveWhitespace = true;
            document.LoadXml(xml);

            string name = InferProfileName(document, xmlPath, explicitProfileName);
            var profile = new VpnPlugInProfile
            {
                ProfileName = name,
                VpnPluginPackageFamilyName = AzureVpnPackageFamilyName,
                CustomConfiguration = xml,
                RequireVpnClientAppUI = true,
                RememberCredentials = true
            };

            foreach (XmlNode fqdnNode in document.GetElementsByTagName("fqdn"))
            {
                Uri uri;
                if (TryCreateServerUri(fqdnNode.InnerText, out uri) &&
                    !profile.ServerUris.Any(existing => existing.Equals(uri)))
                {
                    profile.ServerUris.Add(uri);
                }
            }

            if (profile.ServerUris.Count == 0)
            {
                throw new InvalidOperationException("The Azure VPN profile XML does not contain any fqdn server entries.");
            }

            return profile;
        }

        private static string InferProfileName(XmlDocument document, string xmlPath, string explicitProfileName)
        {
            if (!string.IsNullOrWhiteSpace(explicitProfileName))
            {
                return explicitProfileName;
            }

            XmlNode nameNode = document.SelectSingleNode("//*[local-name()='name']");
            if (nameNode != null && !string.IsNullOrWhiteSpace(nameNode.InnerText))
            {
                return nameNode.InnerText.Trim();
            }

            if (document.DocumentElement != null && !string.IsNullOrWhiteSpace(document.DocumentElement.LocalName))
            {
                return document.DocumentElement.LocalName;
            }

            return Path.GetFileNameWithoutExtension(xmlPath).Replace(".AzureVpnProfile", "");
        }

        private static bool TryCreateServerUri(string server, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(server))
            {
                return false;
            }

            string value = server.Trim();
            if (!value.Contains("://"))
            {
                value = "https://" + value;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out uri);
        }
    }
}

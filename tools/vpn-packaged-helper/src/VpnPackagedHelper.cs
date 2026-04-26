using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.ApplicationModel;
using Windows.Networking.Vpn;

internal static class VpnPackagedHelper
{
    private const string AzureVpnPackageFamilyName = "Microsoft.AzureVpn_8wekyb3d8bbwe";

    public static int Main(string[] args)
    {
        try
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            WriteError("UnhandledException", ex.Message, ex.HResult);
            return 1;
        }
    }

    private static async Task<int> MainAsync(string[] args)
    {
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var agent = new VpnManagementAgent();

        switch (command)
        {
            case "health":
                return Health();

            case "list":
                return await List(agent);

            case "import":
                return await Import(agent, args);

            case "export":
                return await Export(agent, args);

            case "status":
                return await Status(agent, args);

            case "connect":
                return await ConnectOrDisconnect(agent, args, true);

            case "disconnect":
                return await ConnectOrDisconnect(agent, args, false);

            case "delete":
                return await Delete(agent, args);

            default:
                WriteError("Usage", "Usage: VpnPackagedHelper.exe health|list|import <xml-path> [name]|export <name> <xml-path>|status <name>|connect <name>|disconnect <name>|delete <name>", 0);
                return 64;
        }
    }

    private static int Health()
    {
        Package package = Package.Current;
        WriteRaw("{\"ok\":true,\"packageFullName\":" + Json(package.Id.FullName) +
            ",\"packageFamilyName\":" + Json(package.Id.FamilyName) + "}");
        return 0;
    }

    private static async Task<int> List(VpnManagementAgent agent)
    {
        List<IVpnProfile> profiles = (await GetProfiles(agent))
            .OrderBy(p => p.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sb = new StringBuilder();
        sb.Append("{\"ok\":true,\"profiles\":[");

        for (int i = 0; i < profiles.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(",");
            }

            VpnPlugInProfile plugIn = profiles[i] as VpnPlugInProfile;
            string status = plugIn != null ? plugIn.ConnectionStatus.ToString() : "Unknown";
            sb.Append("{\"name\":").Append(Json(profiles[i].ProfileName))
                .Append(",\"status\":").Append(Json(status))
                .Append(",\"type\":").Append(Json(profiles[i].GetType().FullName))
                .Append("}");
        }

        sb.Append("]}");
        WriteRaw(sb.ToString());
        return 0;
    }

    private static async Task<int> Import(VpnManagementAgent agent, string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            WriteError("MissingXmlPath", "Usage: import <xml-path> [name]", 0);
            return 64;
        }

        string xmlPath = Path.GetFullPath(args[1]);
        if (!File.Exists(xmlPath))
        {
            WriteError("XmlNotFound", "XML file not found: " + xmlPath, 0);
            return 2;
        }

        string explicitName = args.Length > 2 ? args[2] : null;
        VpnPlugInProfile profile = CreateAzurePluginProfile(xmlPath, explicitName);

        IVpnProfile existing = await FindProfile(agent, profile.ProfileName);
        if (existing != null)
        {
            VpnManagementErrorStatus deleteStatus = await agent.DeleteProfileAsync(existing).AsTask();
            if (deleteStatus != VpnManagementErrorStatus.Ok)
            {
                WriteResult(false, "DeleteExistingFailed", deleteStatus.ToString(), profile.ProfileName, StatusOf(existing));
                return 3;
            }
        }

        VpnManagementErrorStatus addStatus = await agent.AddProfileFromObjectAsync(profile).AsTask();
        bool ok = addStatus == VpnManagementErrorStatus.Ok;
        WriteResult(ok, ok ? null : "ImportFailed", addStatus.ToString(), profile.ProfileName, ok ? "Disconnected" : "Unknown");
        return ok ? 0 : 3;
    }

    private static async Task<int> Export(VpnManagementAgent agent, string[] args)
    {
        if (args.Length < 3)
        {
            WriteError("Usage", "Usage: export <name> <xml-path>", 0);
            return 64;
        }

        string name = args[1];
        string xmlPath = Path.GetFullPath(args[2]);
        IVpnProfile profile = await FindProfile(agent, name);
        VpnPlugInProfile plugIn = profile as VpnPlugInProfile;
        if (plugIn == null)
        {
            WriteError("ProfileNotFound", "Profile not found: " + name, 0);
            return 2;
        }

        string directory = Path.GetDirectoryName(xmlPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(xmlPath, plugIn.CustomConfiguration ?? "", Encoding.UTF8);
        WriteRaw("{\"ok\":true,\"profile\":" + Json(name) + ",\"path\":" + Json(xmlPath) + "}");
        return 0;
    }

    private static async Task<int> Status(VpnManagementAgent agent, string[] args)
    {
        if (args.Length < 2)
        {
            WriteError("Usage", "Usage: status <name>", 0);
            return 64;
        }

        IVpnProfile profile = await FindProfile(agent, args[1]);
        if (profile == null)
        {
            WriteError("ProfileNotFound", "Profile not found: " + args[1], 0);
            return 2;
        }

        WriteResult(true, null, "Ok", profile.ProfileName, StatusOf(profile));
        return 0;
    }

    private static async Task<int> ConnectOrDisconnect(VpnManagementAgent agent, string[] args, bool connect)
    {
        if (args.Length < 2)
        {
            WriteError("Usage", "Usage: " + (connect ? "connect" : "disconnect") + " <name>", 0);
            return 64;
        }

        IVpnProfile profile = await FindProfile(agent, args[1]);
        if (profile == null)
        {
            WriteError("ProfileNotFound", "Profile not found: " + args[1], 0);
            return 2;
        }

        string currentStatus = StatusOf(profile);
        if (connect && currentStatus == "Connected")
        {
            WriteResult(true, null, "AlreadyConnected", profile.ProfileName, currentStatus);
            return 0;
        }

        if (!connect && currentStatus == "Disconnected")
        {
            WriteResult(true, null, "AlreadyDisconnected", profile.ProfileName, currentStatus);
            return 0;
        }

        VpnManagementErrorStatus result = connect
            ? await agent.ConnectProfileAsync(profile).AsTask()
            : await agent.DisconnectProfileAsync(profile).AsTask();

        bool ok = result == VpnManagementErrorStatus.Ok;
        IVpnProfile refreshed = await FindProfile(agent, profile.ProfileName);
        WriteResult(ok, ok ? null : "ActionFailed", result.ToString(), profile.ProfileName, StatusOf(refreshed ?? profile));
        return ok ? 0 : 3;
    }

    private static async Task<int> Delete(VpnManagementAgent agent, string[] args)
    {
        if (args.Length < 2)
        {
            WriteError("Usage", "Usage: delete <name>", 0);
            return 64;
        }

        IVpnProfile profile = await FindProfile(agent, args[1]);
        if (profile == null)
        {
            WriteError("ProfileNotFound", "Profile not found: " + args[1], 0);
            return 2;
        }

        VpnManagementErrorStatus result = await agent.DeleteProfileAsync(profile).AsTask();
        bool ok = result == VpnManagementErrorStatus.Ok;
        WriteResult(ok, ok ? null : "DeleteFailed", result.ToString(), profile.ProfileName, "Deleted");
        return ok ? 0 : 3;
    }

    private static async Task<IReadOnlyList<IVpnProfile>> GetProfiles(VpnManagementAgent agent)
    {
        return await agent.GetProfilesAsync().AsTask();
    }

    private static async Task<IVpnProfile> FindProfile(VpnManagementAgent agent, string name)
    {
        IReadOnlyList<IVpnProfile> profiles = await GetProfiles(agent);
        return profiles.FirstOrDefault(p => string.Equals(p.ProfileName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static VpnPlugInProfile CreateAzurePluginProfile(string xmlPath, string explicitProfileName)
    {
        string xml = File.ReadAllText(xmlPath);
        XmlDocument document = new XmlDocument();
        document.PreserveWhitespace = true;
        document.LoadXml(xml);

        string inferredName = InferProfileName(document, xmlPath, explicitProfileName);
        var profile = new VpnPlugInProfile();
        profile.ProfileName = inferredName;
        profile.VpnPluginPackageFamilyName = AzureVpnPackageFamilyName;
        profile.CustomConfiguration = xml;
        profile.RequireVpnClientAppUI = true;
        profile.RememberCredentials = true;

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

    private static string StatusOf(IVpnProfile profile)
    {
        VpnPlugInProfile plugIn = profile as VpnPlugInProfile;
        return plugIn != null ? plugIn.ConnectionStatus.ToString() : "Unknown";
    }

    private static void WriteResult(bool ok, string errorCode, string result, string profileName, string status)
    {
        WriteRaw("{\"ok\":" + (ok ? "true" : "false") +
            ",\"code\":" + Json(errorCode ?? result) +
            ",\"result\":" + Json(result) +
            ",\"profile\":" + Json(profileName) +
            ",\"status\":" + Json(status) + "}");
    }

    private static void WriteError(string code, string message, int hresult)
    {
        WriteRaw("{\"ok\":false,\"code\":" + Json(code) +
            ",\"message\":" + Json(message) +
            ",\"hresult\":" + hresult.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
    }

    private static void WriteRaw(string json)
    {
        Console.WriteLine(json);
    }

    private static string Json(string value)
    {
        if (value == null)
        {
            return "null";
        }

        var sb = new StringBuilder();
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

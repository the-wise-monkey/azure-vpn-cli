using System.Collections.Generic;

namespace VpnCli
{
    internal interface IVpnAgent
    {
        AgentResponse List();
        AgentResponse Import(string xmlPath, string name);
        AgentResponse Export(string name, string outputPath);
        AgentResponse Status(string name);
        AgentResponse Connect(string name);
        AgentResponse Disconnect(string name);
        AgentResponse Delete(string name);
    }

    internal class VpnProfileInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
    }

    internal class AgentResponse
    {
        public bool Ok { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Result { get; set; }
        public string Profile { get; set; }
        public string Status { get; set; }
        public string Path { get; set; }
        public List<VpnProfileInfo> Profiles { get; private set; }

        public AgentResponse()
        {
            Profiles = new List<VpnProfileInfo>();
        }
    }
}

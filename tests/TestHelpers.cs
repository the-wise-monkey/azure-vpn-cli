using System;
using System.IO;
using VpnCli;

namespace VpnCli.Tests
{
    internal static class TestHelpers
    {
        internal static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vpn-cli-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static (VpnCommands cmds, StringWriter output, FakeAgent agent) Build(
            string exportDir = null,
            FakeAgent agent = null)
        {
            var output = new StringWriter();
            agent = agent ?? new FakeAgent();
            var cmds = new VpnCommands(
                agent,
                exportDir ?? Path.GetTempPath(),
                output);
            return (cmds, output, agent);
        }
    }

    internal class FakeAgent : IVpnAgent
    {
        public AgentResponse NextResponse { get; set; }
        public string LastCommand { get; private set; }
        public string LastName { get; private set; }
        public string LastPath { get; private set; }
        public string LastImportName { get; private set; }

        public AgentResponse List()
        {
            LastCommand = "list";
            return Response();
        }

        public AgentResponse Import(string xmlPath, string name)
        {
            LastCommand = "import";
            LastPath = xmlPath;
            LastImportName = name;
            return Response(profile: name ?? "imported-vpn");
        }

        public AgentResponse Export(string name, string outputPath)
        {
            LastCommand = "export";
            LastName = name;
            LastPath = outputPath;
            return Response(profile: name, path: outputPath);
        }

        public AgentResponse Status(string name)
        {
            LastCommand = "status";
            LastName = name;
            return Response(profile: name, status: "Disconnected");
        }

        public AgentResponse Connect(string name)
        {
            LastCommand = "connect";
            LastName = name;
            return Response(profile: name, status: "Connected");
        }

        public AgentResponse Disconnect(string name)
        {
            LastCommand = "disconnect";
            LastName = name;
            return Response(profile: name, status: "Disconnected");
        }

        public AgentResponse Delete(string name)
        {
            LastCommand = "delete";
            LastName = name;
            return Response(profile: name, status: "Deleted");
        }

        private AgentResponse Response(string profile = "my-vpn", string status = "Disconnected", string path = null)
        {
            if (NextResponse != null)
            {
                AgentResponse response = NextResponse;
                NextResponse = null;
                return response;
            }

            return new AgentResponse
            {
                Ok = true,
                Code = "Ok",
                Result = "Ok",
                Profile = profile,
                Status = status,
                Path = path
            };
        }

        internal void SetProfiles(params VpnProfileInfo[] profiles)
        {
            var response = new AgentResponse { Ok = true };
            response.Profiles.AddRange(profiles);
            NextResponse = response;
        }

        internal void Fail(string message = "failed")
        {
            NextResponse = new AgentResponse
            {
                Ok = false,
                Code = "Failed",
                Message = message
            };
        }
    }
}

using System;
using System.Collections.Generic;
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

        internal static (VpnCommands cmds, StringWriter output, FakeHelper helper) Build(
            string exportDir = null,
            FakeHelper helper = null)
        {
            var output = new StringWriter();
            helper = helper ?? new FakeHelper();
            var cmds = new VpnCommands(
                helper,
                exportDir ?? Path.GetTempPath(),
                output);
            return (cmds, output, helper);
        }
    }

    internal class FakeHelper : IVpnHelperClient
    {
        public HelperResponse NextResponse { get; set; }
        public string LastCommand { get; private set; }
        public string LastName { get; private set; }
        public string LastPath { get; private set; }
        public string LastImportName { get; private set; }

        public HelperResponse Health()
        {
            LastCommand = "health";
            return Response();
        }

        public HelperResponse List()
        {
            LastCommand = "list";
            return Response();
        }

        public HelperResponse Import(string xmlPath, string name)
        {
            LastCommand = "import";
            LastPath = xmlPath;
            LastImportName = name;
            return Response(profile: name ?? "imported-vpn");
        }

        public HelperResponse Export(string name, string outputPath)
        {
            LastCommand = "export";
            LastName = name;
            LastPath = outputPath;
            return Response(profile: name, path: outputPath);
        }

        public HelperResponse Status(string name)
        {
            LastCommand = "status";
            LastName = name;
            return Response(profile: name, status: "Disconnected");
        }

        public HelperResponse Connect(string name)
        {
            LastCommand = "connect";
            LastName = name;
            return Response(profile: name, status: "Connected");
        }

        public HelperResponse Disconnect(string name)
        {
            LastCommand = "disconnect";
            LastName = name;
            return Response(profile: name, status: "Disconnected");
        }

        public HelperResponse Delete(string name)
        {
            LastCommand = "delete";
            LastName = name;
            return Response(profile: name, status: "Deleted");
        }

        private HelperResponse Response(string profile = "my-vpn", string status = "Disconnected", string path = null)
        {
            if (NextResponse != null)
            {
                HelperResponse response = NextResponse;
                NextResponse = null;
                return response;
            }

            return new HelperResponse
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
            var response = new HelperResponse { Ok = true };
            response.Profiles.AddRange(profiles);
            NextResponse = response;
        }

        internal void Fail(string message = "failed")
        {
            NextResponse = new HelperResponse
            {
                Ok = false,
                Code = "Failed",
                Message = message
            };
        }
    }
}

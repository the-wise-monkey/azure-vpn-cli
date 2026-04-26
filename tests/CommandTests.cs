using System.IO;
using Xunit;
using static VpnCli.Tests.TestHelpers;

namespace VpnCli.Tests
{
    public class HelpTests
    {
        [Fact]
        public void ShowsUsageText()
        {
            var (cmds, output, _) = Build();
            cmds.CmdDefault(null, null);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void ListsAllCommands()
        {
            var (cmds, output, _) = Build();
            cmds.CmdDefault(null, null);
            string text = output.ToString();
            Assert.Contains("connect", text);
            Assert.Contains("disconnect", text);
            Assert.Contains("status", text);
            Assert.Contains("list", text);
            Assert.Contains("import", text);
            Assert.Contains("export", text);
            Assert.Contains("delete", text);
        }
    }

    public class ListTests
    {
        [Fact]
        public void NoProfiles_SaysImport()
        {
            var agent = new FakeAgent();
            agent.SetProfiles();
            var (cmds, output, _) = Build(agent: agent);
            int code = cmds.CmdList();
            Assert.Equal(0, code);
            Assert.Contains("No VPN profiles imported", output.ToString());
        }

        [Fact]
        public void ShowsProfilesWithStatus()
        {
            var agent = new FakeAgent();
            agent.SetProfiles(
                new VpnProfileInfo { Name = "vpn-prod", Status = "Disconnected" },
                new VpnProfileInfo { Name = "my-vpn", Status = "Connected" });
            var (cmds, output, _) = Build(agent: agent);
            cmds.CmdList();
            string text = output.ToString();
            Assert.Contains("vpn-prod", text);
            Assert.Contains("Disconnected", text);
            Assert.Contains("my-vpn", text);
            Assert.Contains("Connected", text);
            Assert.True(text.IndexOf("my-vpn") < text.IndexOf("vpn-prod"));
        }

        [Fact]
        public void AgentFailure_ReturnsOne()
        {
            var agent = new FakeAgent();
            agent.Fail("agent missing");
            var (cmds, output, _) = Build(agent: agent);
            int code = cmds.CmdList();
            Assert.Equal(1, code);
            Assert.Contains("agent missing", output.ToString());
        }
    }

    public class ImportTests
    {
        [Fact]
        public void NoPath_ReturnsOne()
        {
            var (cmds, output, _) = Build();
            int code = cmds.CmdImport(null, null);
            Assert.Equal(1, code);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void ImportsXml()
        {
            var (cmds, output, agent) = Build();
            int code = cmds.CmdImport("my-vpn.AzureVpnProfile.xml", null);
            Assert.Equal(0, code);
            Assert.Equal("import", agent.LastCommand);
            Assert.Contains("my-vpn.AzureVpnProfile.xml", agent.LastPath);
            Assert.Contains("Imported", output.ToString());
        }

        [Fact]
        public void ImportsNameFromDefaultDesktopPath()
        {
            string dir = CreateTempDir();
            try
            {
                var (cmds, _, agent) = Build(exportDir: dir);
                int code = cmds.CmdImport("my-vpn", null);
                Assert.Equal(0, code);
                Assert.Equal(Path.Combine(dir, "my-vpn.AzureVpnProfile.xml"), agent.LastPath);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ImportsWithExplicitName()
        {
            var (cmds, _, agent) = Build();
            int code = cmds.CmdImport("profile.xml", "renamed");
            Assert.Equal(0, code);
            Assert.Equal("renamed", agent.LastImportName);
        }
    }

    public class ExportTests
    {
        [Fact]
        public void NoName_ReturnsOne()
        {
            var (cmds, output, _) = Build();
            int code = cmds.CmdExport(null, null);
            Assert.Equal(1, code);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void ExportsToDefaultDesktopPath()
        {
            string dir = CreateTempDir();
            try
            {
                var (cmds, output, agent) = Build(exportDir: dir);
                int code = cmds.CmdExport("my-vpn", null);
                Assert.Equal(0, code);
                Assert.Equal(Path.Combine(dir, "my-vpn.AzureVpnProfile.xml"), agent.LastPath);
                Assert.Contains("Exported to", output.ToString());
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }

    public class ConnectDisconnectStatusTests
    {
        [Fact]
        public void Connect_Succeeds()
        {
            var (cmds, output, agent) = Build();
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(0, code);
            Assert.Equal("connect", agent.LastCommand);
            Assert.Contains("Connected", output.ToString());
        }

        [Fact]
        public void Connect_AlreadyConnected_PrintsIdempotentMessage()
        {
            var agent = new FakeAgent();
            agent.NextResponse = new AgentResponse
            {
                Ok = true,
                Result = "AlreadyConnected",
                Status = "Connected",
                Profile = "my-vpn"
            };
            var (cmds, output, _) = Build(agent: agent);
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(0, code);
            Assert.Contains("Already connected.", output.ToString());
        }

        [Fact]
        public void Disconnect_Succeeds()
        {
            var (cmds, output, agent) = Build();
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(0, code);
            Assert.Equal("disconnect", agent.LastCommand);
            Assert.Contains("Disconnected", output.ToString());
        }

        [Fact]
        public void Disconnect_AlreadyDisconnected_PrintsIdempotentMessage()
        {
            var agent = new FakeAgent();
            agent.NextResponse = new AgentResponse
            {
                Ok = true,
                Result = "AlreadyDisconnected",
                Status = "Disconnected",
                Profile = "my-vpn"
            };
            var (cmds, output, _) = Build(agent: agent);
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(0, code);
            Assert.Contains("Already disconnected.", output.ToString());
        }

        [Fact]
        public void Status_Succeeds()
        {
            var (cmds, output, agent) = Build();
            int code = cmds.CmdDefault("my-vpn", "status");
            Assert.Equal(0, code);
            Assert.Equal("status", agent.LastCommand);
            Assert.Contains("Disconnected", output.ToString());
        }

        [Fact]
        public void UnknownAction()
        {
            var (cmds, output, _) = Build();
            int code = cmds.CmdDefault("my-vpn", "restart");
            Assert.Equal(1, code);
            Assert.Contains("Unknown action", output.ToString());
        }
    }

    public class DeleteTests
    {
        [Fact]
        public void Delete_Succeeds()
        {
            var (cmds, output, agent) = Build();
            int code = cmds.CmdDelete("my-vpn");
            Assert.Equal(0, code);
            Assert.Equal("delete", agent.LastCommand);
            Assert.Contains("Deleted", output.ToString());
        }
    }
}

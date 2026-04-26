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
            var helper = new FakeHelper();
            helper.SetProfiles();
            var (cmds, output, _) = Build(helper: helper);
            int code = cmds.CmdList();
            Assert.Equal(0, code);
            Assert.Contains("No VPN profiles imported", output.ToString());
        }

        [Fact]
        public void ShowsProfilesWithStatus()
        {
            var helper = new FakeHelper();
            helper.SetProfiles(
                new VpnProfileInfo { Name = "vpn-prod", Status = "Disconnected" },
                new VpnProfileInfo { Name = "my-vpn", Status = "Connected" });
            var (cmds, output, _) = Build(helper: helper);
            cmds.CmdList();
            string text = output.ToString();
            Assert.Contains("vpn-prod", text);
            Assert.Contains("Disconnected", text);
            Assert.Contains("my-vpn", text);
            Assert.Contains("Connected", text);
            Assert.True(text.IndexOf("my-vpn") < text.IndexOf("vpn-prod"));
        }

        [Fact]
        public void HelperFailure_ReturnsOne()
        {
            var helper = new FakeHelper();
            helper.Fail("helper missing");
            var (cmds, output, _) = Build(helper: helper);
            int code = cmds.CmdList();
            Assert.Equal(1, code);
            Assert.Contains("helper missing", output.ToString());
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
            var (cmds, output, helper) = Build();
            int code = cmds.CmdImport("my-vpn.AzureVpnProfile.xml", null);
            Assert.Equal(0, code);
            Assert.Equal("import", helper.LastCommand);
            Assert.Contains("my-vpn.AzureVpnProfile.xml", helper.LastPath);
            Assert.Contains("Imported", output.ToString());
        }

        [Fact]
        public void ImportsNameFromDefaultDesktopPath()
        {
            string dir = CreateTempDir();
            try
            {
                var (cmds, _, helper) = Build(exportDir: dir);
                int code = cmds.CmdImport("my-vpn", null);
                Assert.Equal(0, code);
                Assert.Equal(Path.Combine(dir, "my-vpn.AzureVpnProfile.xml"), helper.LastPath);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ImportsWithExplicitName()
        {
            var (cmds, _, helper) = Build();
            int code = cmds.CmdImport("profile.xml", "renamed");
            Assert.Equal(0, code);
            Assert.Equal("renamed", helper.LastImportName);
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
                var (cmds, output, helper) = Build(exportDir: dir);
                int code = cmds.CmdExport("my-vpn", null);
                Assert.Equal(0, code);
                Assert.Equal(Path.Combine(dir, "my-vpn.AzureVpnProfile.xml"), helper.LastPath);
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
            var (cmds, output, helper) = Build();
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(0, code);
            Assert.Equal("connect", helper.LastCommand);
            Assert.Contains("Connected", output.ToString());
        }

        [Fact]
        public void Connect_AlreadyConnected_PrintsIdempotentMessage()
        {
            var helper = new FakeHelper();
            helper.NextResponse = new HelperResponse
            {
                Ok = true,
                Result = "AlreadyConnected",
                Status = "Connected",
                Profile = "my-vpn"
            };
            var (cmds, output, _) = Build(helper: helper);
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(0, code);
            Assert.Contains("Already connected.", output.ToString());
        }

        [Fact]
        public void Disconnect_Succeeds()
        {
            var (cmds, output, helper) = Build();
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(0, code);
            Assert.Equal("disconnect", helper.LastCommand);
            Assert.Contains("Disconnected", output.ToString());
        }

        [Fact]
        public void Disconnect_AlreadyDisconnected_PrintsIdempotentMessage()
        {
            var helper = new FakeHelper();
            helper.NextResponse = new HelperResponse
            {
                Ok = true,
                Result = "AlreadyDisconnected",
                Status = "Disconnected",
                Profile = "my-vpn"
            };
            var (cmds, output, _) = Build(helper: helper);
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(0, code);
            Assert.Contains("Already disconnected.", output.ToString());
        }

        [Fact]
        public void Status_Succeeds()
        {
            var (cmds, output, helper) = Build();
            int code = cmds.CmdDefault("my-vpn", "status");
            Assert.Equal(0, code);
            Assert.Equal("status", helper.LastCommand);
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
            var (cmds, output, helper) = Build();
            int code = cmds.CmdDelete("my-vpn");
            Assert.Equal(0, code);
            Assert.Equal("delete", helper.LastCommand);
            Assert.Contains("Deleted", output.ToString());
        }
    }
}

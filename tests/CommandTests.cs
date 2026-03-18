using System;
using System.Collections.Generic;
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
            var (cmds, output) = Build("nonexistent.pbk");
            cmds.CmdDefault(null, null);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void ReturnsZero()
        {
            var (cmds, output) = Build("nonexistent.pbk");
            int code = cmds.CmdDefault(null, null);
            Assert.Equal(0, code);
        }

        [Fact]
        public void ListsAllCommands()
        {
            var (cmds, output) = Build("nonexistent.pbk");
            cmds.CmdDefault(null, null);
            string text = output.ToString();
            Assert.Contains("connect", text);
            Assert.Contains("disconnect", text);
            Assert.Contains("status", text);
            Assert.Contains("list", text);
            Assert.Contains("import", text);
            Assert.Contains("export", text);
            Assert.Contains("setup", text);
        }
    }

    public class ListTests : IDisposable
    {
        private readonly string _dir;

        public ListTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void NoPhonebook_SaysNoProfiles()
        {
            var (cmds, output) = Build(Path.Combine(_dir, "missing.pbk"));
            int code = cmds.CmdList();
            Assert.Equal(0, code);
            Assert.Contains("No VPN profiles found", output.ToString());
        }

        [Fact]
        public void ShowsProfilesWithStatus()
        {
            string pbk = WritePhonebook(_dir, new[] { "vpn-prod", "my-vpn" });
            string active = "Connected to\nmy-vpn\nCommand completed successfully.";
            var (cmds, output) = Build(pbk, activeOutput: active);
            cmds.CmdList();
            string text = output.ToString();
            Assert.Contains("vpn-prod", text);
            Assert.Contains("Disconnected", text);
            Assert.Contains("my-vpn", text);
            Assert.Contains("Connected", text);
        }

        [Fact]
        public void AllDisconnected()
        {
            string pbk = WritePhonebook(_dir, new[] { "vpn-prod", "my-vpn" });
            var (cmds, output) = Build(pbk);
            cmds.CmdList();
            string text = output.ToString();
            Assert.Contains("vpn-prod", text);
            Assert.Contains("my-vpn", text);
            Assert.DoesNotContain("Connected\n", text.Replace("Disconnected", ""));
        }

        [Fact]
        public void MultipleProfiles()
        {
            string pbk = WritePhonebook(_dir, new[] { "vpn-prod", "my-vpn", "vpn-dev" });
            var (cmds, output) = Build(pbk);
            cmds.CmdList();
            string text = output.ToString();
            Assert.Contains("vpn-prod", text);
            Assert.Contains("my-vpn", text);
            Assert.Contains("vpn-dev", text);
        }

        [Fact]
        public void SortsAlphabetically()
        {
            string pbk = WritePhonebook(_dir, new[] { "vpn-zulu", "vpn-alpha" });
            var (cmds, output) = Build(pbk);
            cmds.CmdList();
            string text = output.ToString();
            int alphaPos = text.IndexOf("vpn-alpha");
            int zuluPos = text.IndexOf("vpn-zulu");
            Assert.True(alphaPos < zuluPos, "Profiles should be sorted alphabetically");
        }
    }

    public class ExportTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _exportDir;

        public ExportTests()
        {
            _dir = CreateTempDir();
            _exportDir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            if (Directory.Exists(_exportDir)) Directory.Delete(_exportDir, true);
        }

        [Fact]
        public void NoName_ReturnsOne()
        {
            var (cmds, output) = Build(Path.Combine(_dir, "test.pbk"));
            int code = cmds.CmdExport(null);
            Assert.Equal(1, code);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void UnknownProfile_ReturnsOne()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdExport("nope");
            Assert.Equal(1, code);
            Assert.Contains("not found", output.ToString());
        }

        [Fact]
        public void ExportsProfileXml()
        {
            string testXml = "<azvpnprofile><name>my-vpn</name></azvpnprofile>";
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" },
                new Dictionary<string, string> { { "my-vpn", testXml } });
            var (cmds, output) = Build(pbk, exportDir: _exportDir);
            int code = cmds.CmdExport("my-vpn");
            Assert.Equal(0, code);
            Assert.Contains("Exported to", output.ToString());

            string outPath = Path.Combine(_exportDir, "my-vpn.AzureVpnProfile.xml");
            Assert.True(File.Exists(outPath));
            string content = File.ReadAllText(outPath);
            Assert.Contains("<azvpnprofile>", content);
            Assert.Contains("my-vpn", content);
        }

        [Fact]
        public void NoThirdPartyInfo_ReturnsOne()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdExport("my-vpn");
            Assert.Equal(1, code);
            Assert.Contains("Failed to extract", output.ToString());
        }

        [Fact]
        public void MissingPhonebook_ReturnsOne()
        {
            var (cmds, output) = Build(Path.Combine(_dir, "missing.pbk"));
            int code = cmds.CmdExport("my-vpn");
            Assert.Equal(1, code);
            Assert.Contains("not found", output.ToString());
        }
    }

    public class ImportTests : IDisposable
    {
        private readonly string _dir;

        public ImportTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void NoName_ReturnsOne()
        {
            var (cmds, output) = Build(Path.Combine(_dir, "test.pbk"));
            int code = cmds.CmdImport(null);
            Assert.Equal(1, code);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void NewProfile_Succeeds()
        {
            string pbk = WritePhonebook(_dir, new string[0]);
            var (cmds, output) = Build(pbk, actionReturnCode: 0);
            int code = cmds.CmdImport("my-vpn");
            Assert.Equal(0, code);
            Assert.Contains("Imported", output.ToString());
        }

        [Fact]
        public void AlreadyExists_Skips()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdImport("my-vpn");
            Assert.Equal(0, code);
            Assert.Contains("already exists", output.ToString());
        }

        [Fact]
        public void ActionFails_ReturnsOne()
        {
            string pbk = WritePhonebook(_dir, new string[0]);
            var (cmds, output) = Build(pbk, actionReturnCode: 1);
            int code = cmds.CmdImport("my-vpn");
            Assert.Equal(1, code);
            Assert.Contains("Import failed", output.ToString());
        }
    }

    public class ConnectDisconnectTests : IDisposable
    {
        private readonly string _dir;

        public ConnectDisconnectTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Connect_Succeeds()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk, actionReturnCode: 0);
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(0, code);
        }

        [Fact]
        public void Connect_Fails_ReturnsOne()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk, actionReturnCode: 1);
            int code = cmds.CmdDefault("my-vpn", "connect");
            Assert.Equal(1, code);
            Assert.Contains("Action failed", output.ToString());
        }

        [Fact]
        public void Disconnect_Succeeds()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk, actionReturnCode: 0);
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(0, code);
        }

        [Fact]
        public void Disconnect_Fails_ReturnsOne()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk, actionReturnCode: 1);
            int code = cmds.CmdDefault("my-vpn", "disconnect");
            Assert.Equal(1, code);
            Assert.Contains("Action failed", output.ToString());
        }
    }

    public class StatusTests : IDisposable
    {
        private readonly string _dir;

        public StatusTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void Connected()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            string active = "Connected to\nmy-vpn\nCommand completed successfully.";
            var (cmds, output) = Build(pbk, activeOutput: active);
            int code = cmds.CmdDefault("my-vpn", "status");
            Assert.Equal(0, code);
            Assert.Contains("Connected", output.ToString());
        }

        [Fact]
        public void Disconnected()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdDefault("my-vpn", "status");
            Assert.Equal(0, code);
            Assert.Contains("Disconnected", output.ToString());
        }
    }

    public class ErrorHandlingTests : IDisposable
    {
        private readonly string _dir;

        public ErrorHandlingTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void UnknownProfile_Connect()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdDefault("nope", "connect");
            Assert.Equal(1, code);
            Assert.Contains("not found", output.ToString());
        }

        [Fact]
        public void UnknownAction()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdDefault("my-vpn", "restart");
            Assert.Equal(1, code);
            Assert.Contains("Unknown action", output.ToString());
        }

        [Fact]
        public void UnknownProfile_Status()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdDefault("nope", "status");
            Assert.Equal(1, code);
            Assert.Contains("not found", output.ToString());
        }

        [Fact]
        public void UnknownProfile_Disconnect()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, output) = Build(pbk);
            int code = cmds.CmdDefault("nope", "disconnect");
            Assert.Equal(1, code);
            Assert.Contains("not found", output.ToString());
        }
    }
}

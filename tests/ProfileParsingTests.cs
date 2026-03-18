using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using static VpnCli.Tests.TestHelpers;

namespace VpnCli.Tests
{
    public class ProfileParsingTests : IDisposable
    {
        private readonly string _dir;

        public ProfileParsingTests()
        {
            _dir = CreateTempDir();
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void MissingPhonebook_ReturnsEmpty()
        {
            var (cmds, _) = Build(Path.Combine(_dir, "missing.pbk"));
            var profiles = cmds.GetVpnProfiles();
            Assert.Empty(profiles);
        }

        [Fact]
        public void EmptyPhonebook_ReturnsEmpty()
        {
            string pbk = WritePhonebook(_dir, new string[0]);
            var (cmds, _) = Build(pbk);
            var profiles = cmds.GetVpnProfiles();
            Assert.Empty(profiles);
        }

        [Fact]
        public void SingleProfile()
        {
            string pbk = WritePhonebook(_dir, new[] { "my-vpn" });
            var (cmds, _) = Build(pbk);
            var profiles = cmds.GetVpnProfiles();
            Assert.Single(profiles);
            Assert.Equal("my-vpn", profiles[0]);
        }

        [Fact]
        public void MultipleProfiles_Sorted()
        {
            string pbk = WritePhonebook(_dir, new[] { "zulu", "alpha", "mid" });
            var (cmds, _) = Build(pbk);
            var profiles = cmds.GetVpnProfiles();
            Assert.Equal(3, profiles.Count);
            Assert.Equal("alpha", profiles[0]);
            Assert.Equal("mid", profiles[1]);
            Assert.Equal("zulu", profiles[2]);
        }

        [Fact]
        public void ExportProfile_DecodesXml()
        {
            string testXml = "<azvpnprofile><name>test</name></azvpnprofile>";
            string pbk = WritePhonebook(_dir, new[] { "test" },
                new Dictionary<string, string> { { "test", testXml } });
            var (cmds, _) = Build(pbk);
            string xml = cmds.ExportVpnProfile("test");
            Assert.NotNull(xml);
            Assert.Contains("<azvpnprofile>", xml);
            Assert.Contains("test", xml);
        }

        [Fact]
        public void ExportProfile_NoHexData_ReturnsNull()
        {
            string pbk = WritePhonebook(_dir, new[] { "test" });
            var (cmds, _) = Build(pbk);
            string xml = cmds.ExportVpnProfile("test");
            Assert.Null(xml);
        }

        [Fact]
        public void ExportProfile_MissingPhonebook_ReturnsNull()
        {
            var (cmds, _) = Build(Path.Combine(_dir, "missing.pbk"));
            string xml = cmds.ExportVpnProfile("test");
            Assert.Null(xml);
        }
    }
}

using Xunit;
using VpnCli;

namespace VpnCli.Tests
{
    public class HelperJsonTests
    {
        [Fact]
        public void ParsesProfiles()
        {
            string json = "{\"ok\":true,\"profiles\":[{\"name\":\"vpn-a\",\"status\":\"Connected\",\"type\":\"x\"},{\"name\":\"vpn-b\",\"status\":\"Disconnected\",\"type\":\"x\"}]}";
            HelperResponse response = HelperJson.Parse(json);
            Assert.True(response.Ok);
            Assert.Equal(2, response.Profiles.Count);
            Assert.Equal("vpn-a", response.Profiles[0].Name);
            Assert.Equal("Connected", response.Profiles[0].Status);
        }

        [Fact]
        public void ParsesError()
        {
            string json = "{\"ok\":false,\"code\":\"ProfileNotFound\",\"message\":\"Profile not found: test\"}";
            HelperResponse response = HelperJson.Parse(json);
            Assert.False(response.Ok);
            Assert.Equal("ProfileNotFound", response.Code);
            Assert.Contains("test", response.Message);
        }

        [Fact]
        public void UnescapesStrings()
        {
            string json = "{\"ok\":true,\"profile\":\"vpn \\\"quoted\\\"\",\"status\":\"Disconnected\"}";
            HelperResponse response = HelperJson.Parse(json);
            Assert.Equal("vpn \"quoted\"", response.Profile);
        }

        [Fact]
        public void QuotesWindowsPathsWithoutDoublingSeparators()
        {
            string quoted = VpnHelperClient.Quote(@"C:\Users\me\Desktop\profile.AzureVpnProfile.xml");
            Assert.Equal(@"""C:\Users\me\Desktop\profile.AzureVpnProfile.xml""", quoted);
        }

        [Fact]
        public void QuotesEmbeddedQuotes()
        {
            string quoted = VpnHelperClient.Quote("vpn \"prod\"");
            Assert.Equal("\"vpn \\\"prod\\\"\"", quoted);
        }
    }
}

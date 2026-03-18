using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        internal static string WritePhonebook(string dir, string[] names, Dictionary<string, string> xmlData = null)
        {
            string pbkPath = Path.Combine(dir, "rasphone.pbk");
            var sb = new StringBuilder();
            foreach (var name in names)
            {
                sb.AppendLine($"[{name}]");
                sb.AppendLine("Encoding=1");
                if (xmlData != null && xmlData.ContainsKey(name))
                {
                    string hex = EncodeProfileXml(xmlData[name]);
                    sb.AppendLine($"ThirdPartyProfileInfo={hex}");
                }
                sb.AppendLine();
            }
            File.WriteAllText(pbkPath, sb.ToString());
            return pbkPath;
        }

        internal static string EncodeProfileXml(string xml)
        {
            var bytes = Encoding.Unicode.GetBytes(xml);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        internal static VpnCommands BuildCommands(
            string pbkPath,
            string activeOutput = "No connections",
            int actionReturnCode = 0,
            string exportDir = null)
        {
            var output = new StringWriter();
            return new VpnCommands(
                pbkPath,
                () => activeOutput,
                (action, name) => actionReturnCode,
                exportDir ?? Path.GetTempPath(),
                output);
        }

        internal static string GetOutput(VpnCommands cmds)
        {
            // Access the TextWriter via reflection to get output
            var field = typeof(VpnCommands).GetField("_output",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var writer = (StringWriter)field.GetValue(cmds);
            return writer.ToString();
        }

        internal static (VpnCommands cmds, StringWriter output) Build(
            string pbkPath,
            string activeOutput = "No connections",
            int actionReturnCode = 0,
            string exportDir = null)
        {
            var output = new StringWriter();
            var cmds = new VpnCommands(
                pbkPath,
                () => activeOutput,
                (action, name) => actionReturnCode,
                exportDir ?? Path.GetTempPath(),
                output);
            return (cmds, output);
        }
    }
}

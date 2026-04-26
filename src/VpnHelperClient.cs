using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VpnCli
{
    internal class VpnHelperClient : IVpnHelperClient
    {
        private readonly string _helperExe;
        private readonly Action<string> _log;

        internal VpnHelperClient(string helperExe, Action<string> log)
        {
            _helperExe = string.IsNullOrEmpty(helperExe) ? "VpnPackagedHelper.exe" : helperExe;
            _log = log ?? (_ => { });
        }

        public HelperResponse Health()
        {
            return Run("health");
        }

        public HelperResponse List()
        {
            return Run("list");
        }

        public HelperResponse Import(string xmlPath, string name)
        {
            return string.IsNullOrEmpty(name)
                ? Run("import", xmlPath)
                : Run("import", xmlPath, name);
        }

        public HelperResponse Export(string name, string outputPath)
        {
            return Run("export", name, outputPath);
        }

        public HelperResponse Status(string name)
        {
            return Run("status", name);
        }

        public HelperResponse Connect(string name)
        {
            return Run("connect", name);
        }

        public HelperResponse Disconnect(string name)
        {
            return Run("disconnect", name);
        }

        public HelperResponse Delete(string name)
        {
            return Run("delete", name);
        }

        private HelperResponse Run(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _helperExe,
                    Arguments = BuildArguments(args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _log("Helper: " + psi.FileName + " " + psi.Arguments);
                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    _log("Helper exit=" + process.ExitCode + " stdout=" + output.Trim() + " stderr=" + error.Trim());

                    HelperResponse response = HelperJson.Parse(output);
                    if (response == null)
                    {
                        response = new HelperResponse
                        {
                            Ok = false,
                            Code = "InvalidHelperResponse",
                            Message = string.IsNullOrWhiteSpace(error)
                                ? "Invalid helper response."
                                : error.Trim()
                        };
                    }

                    if (process.ExitCode != 0 && response.Ok)
                    {
                        response.Ok = false;
                    }

                    return response;
                }
            }
            catch (Win32Exception)
            {
                return new HelperResponse
                {
                    Ok = false,
                    Code = "HelperNotInstalled",
                    Message = "VPN packaged helper is not installed. Install/register VpnPackagedHelper before using vpn.exe."
                };
            }
            catch (Exception ex)
            {
                return new HelperResponse
                {
                    Ok = false,
                    Code = "HelperLaunchFailed",
                    Message = ex.Message
                };
            }
        }

        private static string BuildArguments(IEnumerable<string> args)
        {
            var parts = new List<string>();
            foreach (string arg in args)
            {
                parts.Add(Quote(arg ?? ""));
            }
            return string.Join(" ", parts);
        }

        internal static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            var sb = new StringBuilder();
            sb.Append('"');

            int backslashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (ch == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }

                sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(ch);
            }

            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }

    internal static class HelperJson
    {
        internal static HelperResponse Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string json = text.Trim();
            int firstBrace = json.IndexOf('{');
            int lastBrace = json.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < firstBrace)
            {
                return null;
            }
            json = json.Substring(firstBrace, lastBrace - firstBrace + 1);

            var response = new HelperResponse
            {
                Ok = Regex.IsMatch(json, "\"ok\"\\s*:\\s*true"),
                Code = GetString(json, "code"),
                Message = GetString(json, "message"),
                Result = GetString(json, "result"),
                Profile = GetString(json, "profile"),
                Status = GetString(json, "status"),
                Path = GetString(json, "path")
            };

            ParseProfiles(json, response);
            return response;
        }

        private static void ParseProfiles(string json, HelperResponse response)
        {
            Match profilesMatch = Regex.Match(json, "\"profiles\"\\s*:\\s*\\[(.*)\\]\\s*\\}", RegexOptions.Singleline);
            if (!profilesMatch.Success)
            {
                return;
            }

            foreach (Match objectMatch in Regex.Matches(profilesMatch.Groups[1].Value, "\\{[^{}]*\\}"))
            {
                string obj = objectMatch.Value;
                string name = GetString(obj, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                response.Profiles.Add(new VpnProfileInfo
                {
                    Name = name,
                    Status = GetString(obj, "status") ?? "Unknown"
                });
            }
        }

        private static string GetString(string json, string property)
        {
            Match match = Regex.Match(json,
                "\"" + Regex.Escape(property) + "\"\\s*:\\s*(null|\"((?:\\\\.|[^\"])*)\")",
                RegexOptions.Singleline);
            if (!match.Success || match.Groups[1].Value == "null")
            {
                return null;
            }

            return Unescape(match.Groups[2].Value);
        }

        private static string Unescape(string value)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch != '\\' || i == value.Length - 1)
                {
                    sb.Append(ch);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            string hex = value.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default:
                        sb.Append(next);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

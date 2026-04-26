using System;
using System.Linq;

namespace VpnCli
{
    class Program
    {
        static int Main(string[] args)
        {
            var argList = args.ToList();
            argList.Remove("-d");
            string arg1 = argList.Count > 0 ? argList[0] : null;
            string arg2 = argList.Count > 1 ? argList[1] : null;
            string arg3 = argList.Count > 2 ? argList[2] : null;

            var agent = new VpnAgent();
            var cmds = new VpnCommands(
                agent,
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Console.Out);

            switch ((arg1 ?? "").ToLowerInvariant())
            {
                case "list":
                    return cmds.CmdList();

                case "import":
                    return cmds.CmdImport(arg2, arg3);

                case "export":
                    return cmds.CmdExport(arg2, arg3);

                case "delete":
                    return cmds.CmdDelete(arg2);

                default:
                    return cmds.CmdDefault(arg1, arg2);
            }
        }
    }
}

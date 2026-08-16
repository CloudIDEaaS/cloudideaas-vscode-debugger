using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace VSCodeDebugger.CommandHandlers
{
    public class DotNetCommandHandler : BaseWindowsCommandHandler
    {
        public int ProcessId => process?.Id ?? -1;

        public DotNetCommandHandler() : base("dotnet.exe")
        {
        }

        public void Serve(string workspaceDirectory, int port)
        {
            base.RunCommand("serve", workspaceDirectory, $"--port { port }");
        }

        public void Build(string directory, string solutionFile, string verbosity = "normal")
        {
            base.RunCommand("build", directory, solutionFile, "--verbosity", verbosity);
        }

        internal void Restore(string solutionFile, string verbosity = "normal")
        {
            var directory = System.IO.Path.GetDirectoryName(solutionFile);

            base.RunCommand("restore", directory, "--verbosity", verbosity);
        }
    }
}

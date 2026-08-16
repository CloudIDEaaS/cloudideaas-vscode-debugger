using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace VSCodeDebugger.CommandHandlers
{
    public class ChromeCommandHandler : BaseWindowsCommandHandler
    {
        public ChromeCommandHandler() : base(ChromeLocator.FindChrome())
        {
            NoWait = true;
        }

        public void LaunchDebugMode(Uri browserUri, Uri debuggerUri, DirectoryInfo userDataDirectory)
        {
            var address = IPAddress.Parse(debuggerUri.Host);
            var port = debuggerUri.Port;

            LaunchDebugMode(browserUri, address, port, userDataDirectory);
        }

        public void LaunchDebugMode(Uri url, IPAddress debuggerAddress, int debuggerPort, DirectoryInfo userDataDirectory)
        {
            var args = new string[]
            {
                $"--remote-debugging-port={debuggerPort}",
                $"--remote-debugging-address={debuggerAddress.ToString()}",
                $"--user-data-dir={userDataDirectory.FullName.SurroundWithQuotes()}",
                $"--no-first-run",
                $"--no-default-browser-check",
                url.ToString()
             };

            base.Run(userDataDirectory.FullName, args);
        }
    }
}

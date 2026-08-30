using System.Diagnostics;
using System.Windows.Forms;
using Utils;
using Utils.VisualStudio;

namespace ChromeDebugger
{  
    public class Program
    {
        private static StandardStreamService streamService;
        private static string logPath;
        private static string workingDirectory;
        private static LogWriter logWriter;

        public static void Main(string[] args)
        {
            var parentProcess = Process.GetCurrentProcess().GetParent();
            var currentDirectory = Environment.CurrentDirectory;

            if (args.Length < 1)
            {
                Console.WriteLine("Missing working directory argument.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine("Usage: ChromeDebugger.exe <working_directory>");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();

                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            try
            {
                workingDirectory = args[0];

                logPath = Path.Combine(args[0], @".vscode\logs\" + DateTime.Now.ToSortableShortDateTimeText() + "_VSCodeDebugger.log");

                logWriter = new LogWriter(logPath);

                VisualStudioExtensions.DebugAttach(false, true);

                streamService = new StandardStreamService(logWriter, workingDirectory);

                streamService.Start(parentProcess, currentDirectory);

                Console.Error.WriteLine("About to enter streamService.Wait().");

                streamService.Wait();

                Console.Error.WriteLine("streamService.Wait() returned.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Exception: " + ex.ToString());
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSCodeDebugger.CommandHandlers
{
    using Microsoft.Win32;

    public static class ChromeLocator
    {
        public static string FindChrome()
        {
            // Common installation locations
            var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\Application\chrome.exe"),

                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Google\Chrome\Application\chrome.exe"),

                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Google\Chrome\Application\chrome.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Registry
            var registryPath = FindChromeInRegistry();

            if (registryPath != null)
                return registryPath;

            throw new FileNotFoundException("Google Chrome could not be located.");
        }

        private static string? FindChromeInRegistry()
        {
            var registryKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
            };

            foreach (var keyName in registryKeys)
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyName) ??  Registry.LocalMachine.OpenSubKey(keyName))
                {
                    if (key?.GetValue(null) is string path && File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }
    }
}

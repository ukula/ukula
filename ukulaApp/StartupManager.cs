using Microsoft.Win32;
using System;

namespace UkulaApp
{
    public static class StartupManager
    {
        private const string AppName = "Ukula";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var value = key?.GetValue(AppName) as string;

                return !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex)
            {
                Logger.Log("Startup state check failed", ex);
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

                if (enabled)
                {
                    var executablePath = Environment.ProcessPath;

                    if (!string.IsNullOrWhiteSpace(executablePath))
                        key?.SetValue(AppName, $"\"{executablePath}\" --minimized");
                }
                else
                {
                    key?.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Startup state update failed", ex);
            }
        }
    }
}

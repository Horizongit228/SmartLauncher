using System;
using System.IO;
using System.Text;

namespace SmartLauncher.UI.Services
{
    public static class AppLogService
    {
        private static readonly object SyncRoot = new();

        private static readonly string LogDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SmartLauncher",
                "Logs");

        public static string CurrentLogPath =>
            Path.Combine(
                LogDirectory,
                $"smart-launcher-{DateTime.Now:yyyy-MM-dd}.log");

        public static void Initialize()
        {
            Directory.CreateDirectory(LogDirectory);
            CleanupOldLogs();
            Info("Smart Launcher started.");
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warning(string message)
        {
            Write("WARN", message);
        }

        public static void Error(
            string message,
            Exception? exception = null)
        {
            string details =
                exception == null
                    ? message
                    : message
                      + Environment.NewLine
                      + exception;

            Write("ERROR", details);
        }

        private static void Write(
            string level,
            string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(
                        CurrentLogPath,
                        $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never terminate the launcher.
            }
        }

        private static void CleanupOldLogs()
        {
            try
            {
                foreach (string filePath
                         in Directory.EnumerateFiles(
                             LogDirectory,
                             "smart-launcher-*.log"))
                {
                    if (File.GetLastWriteTimeUtc(filePath)
                        < DateTime.UtcNow.AddDays(-14))
                    {
                        File.Delete(filePath);
                    }
                }
            }
            catch
            {
                // Old logs can be cleaned up on a later launch.
            }
        }
    }
}

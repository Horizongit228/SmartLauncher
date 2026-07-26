using Microsoft.Win32;
using System;

namespace SmartLauncher.UI.Services
{
    public static class AutoStartService
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string AppName = "SmartLauncher";

        public static void SetEnabled(bool isEnabled)
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    RunKey,
                    writable: true);

            if (key == null)
            {
                throw new InvalidOperationException(
                    "Не удалось открыть раздел автозапуска Windows.");
            }

            if (!isEnabled)
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                return;
            }

            string executablePath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Не удалось определить путь приложения.");

            key.SetValue(
                AppName,
                $"\"{executablePath}\"");
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;

namespace SmartLauncher.UI.Services
{
    public static class DesktopShortcutService
    {
        private const string ShortcutFileName =
            "Smart Launcher.lnk";

        public static string ShortcutPath =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory),
                ShortcutFileName);

        public static bool IsCreated =>
            File.Exists(ShortcutPath);

        public static void RefreshIconIfCreated()
        {
            if (!IsCreated)
            {
                return;
            }

            try
            {
                UpdateShortcut(
                    (shortcutType, shortcut) =>
                        SetProperty(
                            shortcutType,
                            shortcut,
                            "IconLocation",
                            EnsureShortcutIcon()
                            + ",0"));
            }
            catch
            {
                // Ярлык будет обновлён при следующем сохранении настроек.
            }
        }

        public static void SetEnabled(bool isEnabled)
        {
            if (!isEnabled)
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                }

                return;
            }

            string executablePath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Не удалось определить путь Smart Launcher.");

            UpdateShortcut(
                (shortcutType, shortcut) =>
                {
                SetProperty(
                    shortcutType,
                    shortcut,
                    "TargetPath",
                    executablePath);

                SetProperty(
                    shortcutType,
                    shortcut,
                    "WorkingDirectory",
                    Path.GetDirectoryName(executablePath)
                    ?? string.Empty);

                SetProperty(
                    shortcutType,
                    shortcut,
                    "Description",
                    "Запустить Smart Launcher");

                SetProperty(
                    shortcutType,
                    shortcut,
                    "IconLocation",
                    EnsureShortcutIcon() + ",0");
                });
        }

        private static string EnsureShortcutIcon()
        {
            var resourceUri =
                new Uri(
                    "pack://application:,,,/Assets/Icons/SL.ico",
                    UriKind.Absolute);

            using Stream resourceStream =
                System.Windows.Application
                    .GetResourceStream(resourceUri)?.Stream
                ?? throw new FileNotFoundException(
                    "Ресурс иконки Smart Launcher не найден.");

            using var memoryStream =
                new MemoryStream();

            resourceStream.CopyTo(memoryStream);
            byte[] iconBytes =
                memoryStream.ToArray();
            string iconHash =
                Convert.ToHexString(
                    SHA256.HashData(iconBytes))[..16];

            string iconDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "SmartLauncher",
                    "Icons");

            Directory.CreateDirectory(iconDirectory);

            string iconPath =
                Path.Combine(
                    iconDirectory,
                    $"SmartLauncher-{iconHash}.ico");

            if (!File.Exists(iconPath))
            {
                File.WriteAllBytes(
                    iconPath,
                    iconBytes);
            }

            return iconPath;
        }

        private static void UpdateShortcut(
            Action<Type, object> update)
        {
            Type shellType =
                Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException(
                    "Компонент Windows Script Host недоступен.");

            object? shell = null;
            object? shortcut = null;

            try
            {
                shell =
                    Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException(
                        "Не удалось создать ярлык.");

                shortcut =
                    shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[]
                        {
                            ShortcutPath
                        })
                    ?? throw new InvalidOperationException(
                        "Не удалось создать ярлык.");

                Type shortcutType =
                    shortcut.GetType();

                update(
                    shortcutType,
                    shortcut);

                shortcutType.InvokeMember(
                    "Save",
                    BindingFlags.InvokeMethod,
                    null,
                    shortcut,
                    null);
            }
            finally
            {
                if (shortcut != null
                    && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(
                        shortcut);
                }

                if (shell != null
                    && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private static void SetProperty(
            Type targetType,
            object target,
            string propertyName,
            string value)
        {
            targetType.InvokeMember(
                propertyName,
                BindingFlags.SetProperty,
                null,
                target,
                new object[]
                {
                    value
                });
        }
    }
}

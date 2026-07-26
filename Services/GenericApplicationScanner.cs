using Microsoft.Win32;
using SmartLauncher.UI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartLauncher.UI.Services
{
    public sealed class GenericApplicationScanner
    {
        private const string UninstallRegistryPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public List<InstalledApplication> Scan()
        {
            var applications =
                new List<InstalledApplication>();

            applications.AddRange(
                ScanUninstallRegistry());
            applications.AddRange(
                ScanStartMenuShortcuts());
            applications.AddRange(
                ScanPackagedApplications());

            return applications
                .Where(application =>
                    application.IsFound)
                .GroupBy(
                    application =>
                        application.LaunchKind
                            == ApplicationLaunchKind.WebApplication
                            ? application.Id
                            : NormalizeIdentity(
                                application.EffectiveLaunchValue),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderBy(application =>
                            application.EffectiveLaunchValue.Contains(
                                "\\Startup\\",
                                StringComparison.OrdinalIgnoreCase))
                        .First())
                .OrderBy(application =>
                    application.Name)
                .ToList();
        }

        private static IEnumerable<InstalledApplication>
            ScanUninstallRegistry()
        {
            RegistryHive[] hives =
            {
                RegistryHive.CurrentUser,
                RegistryHive.LocalMachine
            };

            RegistryView[] views =
            {
                RegistryView.Registry64,
                RegistryView.Registry32
            };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    using RegistryKey baseKey =
                        RegistryKey.OpenBaseKey(
                            hive,
                            view);

                    using RegistryKey? uninstallKey =
                        baseKey.OpenSubKey(
                            UninstallRegistryPath);

                    if (uninstallKey == null)
                    {
                        continue;
                    }

                    foreach (string subKeyName
                             in uninstallKey.GetSubKeyNames())
                    {
                        using RegistryKey? applicationKey =
                            uninstallKey.OpenSubKey(
                                subKeyName);

                        if (applicationKey == null)
                        {
                            continue;
                        }

                        string name =
                            applicationKey.GetValue(
                                "DisplayName") as string
                            ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(name)
                            || applicationKey.GetValue(
                                "SystemComponent") is int systemComponent
                            && systemComponent == 1)
                        {
                            continue;
                        }

                        string displayIcon =
                            applicationKey.GetValue(
                                "DisplayIcon") as string
                            ?? string.Empty;

                        string executablePath =
                            NormalizeExecutablePath(
                                displayIcon);

                        if (!IsExecutableFile(
                                executablePath)
                            || IsLikelyMaintenanceExecutable(
                                executablePath))
                        {
                            executablePath =
                                FindExecutableInInstallLocation(
                                    applicationKey.GetValue(
                                        "InstallLocation") as string
                                    ?? string.Empty,
                                    name);
                        }

                        if (!IsExecutableFile(
                                executablePath)
                            || IsLikelyMaintenanceExecutable(
                                executablePath))
                        {
                            continue;
                        }

                        yield return
                            CreateApplication(
                                name,
                                executablePath,
                                ApplicationLaunchKind.Executable,
                                "Registry");
                    }
                }
            }
        }

        private static IEnumerable<InstalledApplication>
            ScanStartMenuShortcuts()
        {
            string[] roots =
            {
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.StartMenu),
                    "Programs"),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonStartMenu),
                    "Programs")
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> shortcuts;

                try
                {
                    shortcuts =
                        Directory.EnumerateFiles(
                                root,
                                "*.lnk",
                                SearchOption.AllDirectories)
                            .ToList();
                }
                catch (Exception exception)
                {
                    AppLogService.Warning(
                        $"Could not scan Start Menu {root}: {exception.Message}");
                    continue;
                }

                foreach (string shortcutPath
                         in shortcuts)
                {
                    ShortcutDetails? details =
                        ReadShortcut(shortcutPath);

                    if (details == null
                        || !File.Exists(details.TargetPath))
                    {
                        continue;
                    }

                    string extension =
                        Path.GetExtension(
                            details.TargetPath);

                    if (!string.Equals(
                            extension,
                            ".exe",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string name =
                        Path.GetFileNameWithoutExtension(
                            shortcutPath);

                    if (IsMaintenanceShortcutName(name))
                    {
                        continue;
                    }

                    bool isChromePwa =
                        details.TargetPath.Contains(
                            "chrome",
                            StringComparison.OrdinalIgnoreCase)
                        && (details.TargetPath.EndsWith(
                                "chrome.exe",
                                StringComparison.OrdinalIgnoreCase)
                            || details.TargetPath.EndsWith(
                                "chrome_proxy.exe",
                                StringComparison.OrdinalIgnoreCase))
                        && details.Arguments.Contains(
                            "--app-id=",
                            StringComparison.OrdinalIgnoreCase);

                    bool isEdgePwa =
                        details.TargetPath.Contains(
                            "msedge",
                            StringComparison.OrdinalIgnoreCase)
                        && (details.TargetPath.EndsWith(
                                "msedge.exe",
                                StringComparison.OrdinalIgnoreCase)
                            || details.TargetPath.EndsWith(
                                "msedge_proxy.exe",
                                StringComparison.OrdinalIgnoreCase))
                        && details.Arguments.Contains(
                            "--app-id=",
                            StringComparison.OrdinalIgnoreCase);

                    ApplicationLaunchKind launchKind =
                        isChromePwa || isEdgePwa
                            ? ApplicationLaunchKind.WebApplication
                            : string.IsNullOrWhiteSpace(
                                details.Arguments)
                                ? ApplicationLaunchKind.Executable
                                : ApplicationLaunchKind.Shortcut;

                    string launchValue =
                        launchKind
                            == ApplicationLaunchKind.Executable
                            ? details.TargetPath
                            : shortcutPath;

                    var application =
                        CreateApplication(
                            name,
                            launchValue,
                            launchKind,
                            isChromePwa
                                ? "ChromePwa"
                                : isEdgePwa
                                    ? "EdgePwa"
                                    : "StartMenu");

                    if (isChromePwa || isEdgePwa)
                    {
                        string webAppId =
                            ReadArgumentValue(
                                details.Arguments,
                                "--app-id=");
                        application.Id =
                            "pwa-"
                            + CreateStableId(
                                details.TargetPath
                                + "|"
                                + webAppId);
                    }

                    application.IconPath =
                        details.IconPath;

                    yield return application;
                }
            }
        }

        private static List<InstalledApplication>
            ScanPackagedApplications()
        {
            var applications =
                new List<InstalledApplication>();

            const string command =
                "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; "
                + "$OutputEncoding=[System.Text.Encoding]::UTF8; "
                + "Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress";

            var startInfo =
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \""
                        + command
                        + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding =
                        Encoding.UTF8,
                    StandardErrorEncoding =
                        Encoding.UTF8
                };

            try
            {
                using Process? process =
                    Process.Start(startInfo);

                if (process == null)
                {
                    return applications;
                }

                string output =
                    process.StandardOutput.ReadToEnd();

                if (!process.WaitForExit(10000))
                {
                    process.Kill(
                        entireProcessTree: true);
                    return applications;
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    return applications;
                }

                using JsonDocument document =
                    JsonDocument.Parse(output);

                IEnumerable<JsonElement> items =
                    document.RootElement.ValueKind
                    == JsonValueKind.Array
                        ? document.RootElement.EnumerateArray()
                        : new[]
                        {
                            document.RootElement
                        };

                foreach (JsonElement item in items)
                {
                    string name =
                        item.TryGetProperty(
                            "Name",
                            out JsonElement nameElement)
                            ? nameElement.GetString()
                              ?? string.Empty
                            : string.Empty;

                    string appId =
                        item.TryGetProperty(
                            "AppID",
                            out JsonElement idElement)
                            ? idElement.GetString()
                              ?? string.Empty
                            : string.Empty;

                    if (string.IsNullOrWhiteSpace(name)
                        || string.IsNullOrWhiteSpace(appId)
                        || !appId.Contains('!'))
                    {
                        continue;
                    }

                    applications.Add(
                        CreateApplication(
                            name,
                            @"shell:AppsFolder\" + appId,
                            ApplicationLaunchKind.PackagedApp,
                            "StartApps"));
                }
            }
            catch (Exception exception)
            {
                AppLogService.Warning(
                    $"Packaged app scan failed: {exception.Message}");
            }

            return applications;
        }

        private static InstalledApplication CreateApplication(
            string name,
            string launchValue,
            ApplicationLaunchKind launchKind,
            string source)
        {
            var application =
                new InstalledApplication
            {
                Id =
                    "app-"
                    + CreateStableId(
                        launchValue),
                Name = name.Trim(),
                ExecutablePath =
                    launchKind
                    is ApplicationLaunchKind.Executable
                        ? launchValue
                        : string.Empty,
                LaunchValue = launchValue,
                LaunchKind = launchKind,
                Source = source,
                Category =
                    InferCategory(
                        name,
                        launchKind
                            == ApplicationLaunchKind.WebApplication
                            ? name
                            : launchValue)
            };

            if (launchKind
                == ApplicationLaunchKind.PackagedApp)
            {
                application.IconPath =
                    "/Assets/Icons/Apps.png";
            }

            return application;
        }

        private static string InferCategory(
            string name,
            string path)
        {
            string value =
                (name + " " + path).ToLowerInvariant();

            if (ContainsAny(
                    value,
                    "visual studio",
                    "code",
                    "rider",
                    "github",
                    "git",
                    "docker",
                    "postman",
                    "terminal",
                    "powershell"))
            {
                return "Разработка";
            }

            if (ContainsAny(
                    value,
                    "steam",
                    "epic",
                    "game",
                    "xbox",
                    "battle.net",
                    "gog"))
            {
                return "Игры";
            }

            if (ContainsAny(
                    value,
                    "telegram",
                    "discord",
                    "slack",
                    "teams",
                    "zoom",
                    "skype"))
            {
                return "Общение";
            }

            if (ContainsAny(
                    value,
                    "youtube",
                    "spotify",
                    "music",
                    "video",
                    "vlc",
                    "media"))
            {
                return "Мультимедиа";
            }

            if (ContainsAny(
                    value,
                    "chrome",
                    "edge",
                    "browser",
                    "firefox",
                    "opera",
                    "yandex"))
            {
                return "Браузеры";
            }

            if (ContainsAny(
                    value,
                    "word",
                    "excel",
                    "powerpoint",
                    "office",
                    "notion",
                    "obsidian"))
            {
                return "Работа";
            }

            return "Другое";
        }

        private static bool ContainsAny(
            string value,
            params string[] terms)
        {
            return terms.Any(term =>
                value.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string FindExecutableInInstallLocation(
            string installLocation,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(installLocation)
                || !Directory.Exists(installLocation))
            {
                return string.Empty;
            }

            try
            {
                string normalizedName =
                    new string(
                        displayName
                            .Where(char.IsLetterOrDigit)
                            .ToArray());

                return Directory
                    .EnumerateFiles(
                        installLocation,
                        "*.exe",
                        SearchOption.TopDirectoryOnly)
                    .Where(path =>
                        !IsLikelyMaintenanceExecutable(
                            path))
                    .OrderByDescending(path =>
                        new string(
                            Path.GetFileNameWithoutExtension(path)
                                .Where(char.IsLetterOrDigit)
                                .ToArray())
                            .Contains(
                                normalizedName,
                                StringComparison.OrdinalIgnoreCase))
                    .ThenBy(path =>
                        path.Length)
                    .FirstOrDefault()
                    ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeExecutablePath(
            string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return string.Empty;
            }

            string value =
                Environment.ExpandEnvironmentVariables(
                    rawPath.Trim());

            if (value.StartsWith(
                    "\"",
                    StringComparison.Ordinal))
            {
                int closingQuote =
                    value.IndexOf(
                        '"',
                        1);

                if (closingQuote > 1)
                {
                    value =
                        value[1..closingQuote];
                }
            }
            else
            {
                int iconIndex =
                    value.LastIndexOf(',');

                if (iconIndex > 2)
                {
                    value =
                        value[..iconIndex];
                }
            }

            return value.Trim().Trim('"');
        }

        private static bool IsExecutableFile(
            string path)
        {
            return File.Exists(path)
                   && string.Equals(
                       Path.GetExtension(path),
                       ".exe",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool
            IsLikelyMaintenanceExecutable(
                string path)
        {
            string fileName =
                Path.GetFileNameWithoutExtension(path)
                    .ToLowerInvariant();

            return fileName.StartsWith(
                       "unins",
                       StringComparison.Ordinal)
                   || fileName.Contains(
                       "uninstall",
                       StringComparison.Ordinal)
                   || fileName is "update"
                       or "updater"
                       or "setup"
                       or "installer"
                       or "install";
        }

        private static bool IsMaintenanceShortcutName(
            string name)
        {
            string normalized =
                name.Trim().ToLowerInvariant();

            return normalized.StartsWith(
                       "uninstall",
                       StringComparison.Ordinal)
                   || normalized.StartsWith(
                       "unins",
                       StringComparison.Ordinal)
                   || normalized.StartsWith(
                       "деинстал",
                       StringComparison.CurrentCulture)
                   || normalized.StartsWith(
                       "удалить ",
                       StringComparison.CurrentCulture);
        }

        private static string ReadArgumentValue(
            string arguments,
            string argumentName)
        {
            int start =
                arguments.IndexOf(
                    argumentName,
                    StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            start += argumentName.Length;
            int end =
                arguments.IndexOfAny(
                    new[]
                    {
                        ' ',
                        '\t',
                        '"'
                    },
                    start);

            return (end < 0
                    ? arguments[start..]
                    : arguments[start..end])
                .Trim()
                .Trim('"');
        }

        private static ShortcutDetails? ReadShortcut(
            string shortcutPath)
        {
            Type? shellType =
                Type.GetTypeFromProgID(
                    "WScript.Shell");

            if (shellType == null)
            {
                return null;
            }

            object? shell = null;
            object? shortcut = null;

            try
            {
                shell =
                    Activator.CreateInstance(shellType);

                if (shell == null)
                {
                    return null;
                }

                shortcut =
                    shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[]
                        {
                            shortcutPath
                        });

                if (shortcut == null)
                {
                    return null;
                }

                Type shortcutType =
                    shortcut.GetType();

                string targetPath =
                    shortcutType.InvokeMember(
                        "TargetPath",
                        BindingFlags.GetProperty,
                        null,
                        shortcut,
                        null) as string
                    ?? string.Empty;

                string arguments =
                    shortcutType.InvokeMember(
                        "Arguments",
                        BindingFlags.GetProperty,
                        null,
                        shortcut,
                        null) as string
                    ?? string.Empty;

                string iconLocation =
                    shortcutType.InvokeMember(
                        "IconLocation",
                        BindingFlags.GetProperty,
                        null,
                        shortcut,
                        null) as string
                    ?? string.Empty;

                return new ShortcutDetails(
                    Environment.ExpandEnvironmentVariables(
                        targetPath),
                    arguments,
                    NormalizeExecutablePath(
                        iconLocation));
            }
            catch
            {
                return null;
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
                    Marshal.FinalReleaseComObject(
                        shell);
                }
            }
        }

        private static string CreateStableId(
            string value)
        {
            byte[] bytes =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value.ToLowerInvariant()));

            return Convert.ToHexString(
                bytes)[..16]
                .ToLowerInvariant();
        }

        private static string NormalizeIdentity(
            string value)
        {
            return value.Trim()
                .Replace(
                    '/',
                    '\\')
                .ToLowerInvariant();
        }

        private sealed record ShortcutDetails(
            string TargetPath,
            string Arguments,
            string IconPath);
    }
}

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

        private const string AppPathsRegistryPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

        public List<InstalledApplication> Scan()
        {
            var applications =
                new List<InstalledApplication>();

            applications.AddRange(
                ScanAppPathsRegistry());
            applications.AddRange(
                ScanUninstallRegistry());
            applications.AddRange(
                ScanApplicationShortcuts());
            applications.AddRange(
                ScanPackagedApplications());
            applications.AddRange(
                ScanRunningApplications());
            applications.AddRange(
                ScanApplicationDirectories());
            applications.AddRange(
                new GameLibraryScanner().Scan());

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
                .GroupBy(
                    application =>
                        NormalizeProductIdentity(
                            application.Name),
                    StringComparer
                        .CurrentCultureIgnoreCase)
                .Select(group =>
                    group
                        .OrderBy(
                            application =>
                                GetSourcePriority(
                                    application.Source))
                        .ThenBy(application =>
                            application
                                .EffectiveLaunchValue
                                .Length)
                        .First())
                .OrderBy(application =>
                    application.Name)
                .ToList();
        }

        private static IEnumerable<InstalledApplication>
            ScanAppPathsRegistry()
        {
            foreach (RegistryHive hive
                     in new[]
                     {
                         RegistryHive.CurrentUser,
                         RegistryHive.LocalMachine
                     })
            {
                foreach (RegistryView view
                         in new[]
                         {
                             RegistryView.Registry64,
                             RegistryView.Registry32
                         })
                {
                    RegistryKey? appPathsKey = null;
                    try
                    {
                        using RegistryKey baseKey =
                            RegistryKey.OpenBaseKey(
                                hive,
                                view);
                        appPathsKey =
                            baseKey.OpenSubKey(
                                AppPathsRegistryPath);
                        if (appPathsKey == null)
                        {
                            continue;
                        }

                        foreach (string subKeyName
                                 in appPathsKey
                                     .GetSubKeyNames())
                        {
                            using RegistryKey? applicationKey =
                                appPathsKey.OpenSubKey(
                                    subKeyName);
                            string executablePath =
                                NormalizeExecutablePath(
                                    applicationKey
                                        ?.GetValue(null)
                                    as string
                                    ?? string.Empty);

                            if (!IsExecutableFile(
                                    executablePath)
                                || IsLikelyMaintenanceExecutable(
                                    executablePath)
                                || IsSystemExecutablePath(
                                    executablePath)
                                || IsPackagedExecutablePath(
                                    executablePath)
                                || executablePath.Contains(
                                    "\\Common Files\\",
                                    StringComparison
                                        .OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            yield return
                                CreateApplicationFromExecutable(
                                    executablePath,
                                    Path.GetFileNameWithoutExtension(
                                        subKeyName),
                                    "AppPaths");
                        }
                    }
                    finally
                    {
                        appPathsKey?.Dispose();
                    }
                }
            }
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
                            && systemComponent == 1
                            || IsNonApplicationRegistration(
                                name))
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
                                executablePath)
                            || executablePath.Contains(
                                "\\Package Cache\\",
                                StringComparison
                                    .OrdinalIgnoreCase))
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
            ScanApplicationShortcuts()
        {
            (string Path, string Source)[] roots =
            {
                (
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.StartMenu),
                        "Programs"),
                    "StartMenu"),
                (
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.CommonStartMenu),
                        "Programs"),
                    "StartMenu"),
                (
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    "Desktop"),
                (
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonDesktopDirectory),
                    "Desktop")
            };

            foreach ((string root, string source)
                     in roots.Distinct())
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
                                    : source);

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

        private static IEnumerable<InstalledApplication>
            ScanRunningApplications()
        {
            var applications =
                new List<InstalledApplication>();

            foreach (Process process
                     in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle
                            == IntPtr.Zero
                        || process.Id
                            == Environment.ProcessId)
                    {
                        continue;
                    }

                    string executablePath =
                        process.MainModule?.FileName
                        ?? string.Empty;
                    if (!IsExecutableFile(
                            executablePath)
                        || IsLikelyMaintenanceExecutable(
                            executablePath)
                        || IsSystemExecutablePath(
                            executablePath)
                        || IsPackagedExecutablePath(
                            executablePath))
                    {
                        continue;
                    }

                    applications.Add(
                        CreateApplicationFromExecutable(
                            executablePath,
                            process.ProcessName,
                            "RunningProcess"));
                }
                catch
                {
                    // Защищённый системный процесс пропускается.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return applications;
        }

        private static IEnumerable<InstalledApplication>
            ScanApplicationDirectories()
        {
            string[] roots =
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .ProgramFiles),
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .ProgramFilesX86),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData),
                    "Programs"),
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData),
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .ApplicationData)
            };

            var applications =
                new List<InstalledApplication>();

            foreach (string root
                     in roots
                         .Where(Directory.Exists)
                         .Distinct(
                             StringComparer
                                 .OrdinalIgnoreCase))
            {
                try
                {
                    var options =
                        new EnumerationOptions
                        {
                            RecurseSubdirectories =
                                true,
                            MaxRecursionDepth =
                                IsUserDataDirectory(
                                    root)
                                    ? 2
                                    : 3,
                            IgnoreInaccessible = true,
                            AttributesToSkip =
                                FileAttributes
                                    .ReparsePoint
                        };

                    foreach (string executablePath
                             in Directory
                                 .EnumerateFiles(
                                     root,
                                     "*.exe",
                                     options)
                                 .Take(6000))
                    {
                        if (!IsLikelyUserApplication(
                                executablePath))
                        {
                            continue;
                        }

                        applications.Add(
                            CreateApplicationFromExecutable(
                                executablePath,
                                string.Empty,
                                "FolderScan"));

                        if (applications.Count
                            >= 800)
                        {
                            return
                                ReduceDirectoryScanResults(
                                    applications);
                        }
                    }
                }
                catch (Exception exception)
                {
                    AppLogService.Warning(
                        "Не удалось полностью просканировать "
                        + $"{root}: {exception.Message}");
                }
            }

            return ReduceDirectoryScanResults(
                applications);
        }

        private static InstalledApplication
            CreateApplicationFromExecutable(
                string executablePath,
                string fallbackName,
                string source)
        {
            string name =
                GetApplicationDisplayName(
                    executablePath,
                    fallbackName);

            return CreateApplication(
                name,
                executablePath,
                ApplicationLaunchKind.Executable,
                source);
        }

        private static InstalledApplication CreateApplication(
            string name,
            string launchValue,
            ApplicationLaunchKind launchKind,
            string source)
        {
            string category =
                ApplicationCategories.Infer(
                    name,
                    launchKind
                        == ApplicationLaunchKind.WebApplication
                        ? name
                        : launchValue);

            return
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
                    Category = category,
                    IconPath =
                        AssetIconService
                            .GetApplicationIcon(
                                category)
                };
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

        private static string GetApplicationDisplayName(
            string executablePath,
            string fallbackName)
        {
            try
            {
                FileVersionInfo versionInfo =
                    FileVersionInfo.GetVersionInfo(
                        executablePath);

                string name =
                    versionInfo.ProductName
                    ?? versionInfo.FileDescription
                    ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(name)
                    && !IsGenericProductName(name))
                {
                    return name.Trim();
                }
            }
            catch
            {
                // Метаданные EXE могут отсутствовать.
            }

            return string.IsNullOrWhiteSpace(
                    fallbackName)
                ? Path.GetFileNameWithoutExtension(
                    executablePath)
                : fallbackName.Trim();
        }

        private static bool IsLikelyUserApplication(
            string executablePath)
        {
            if (!IsExecutableFile(executablePath)
                || IsLikelyMaintenanceExecutable(
                    executablePath))
            {
                return false;
            }

            string normalizedPath =
                executablePath
                    .Replace('/', '\\')
                    .ToLowerInvariant();
            string fileName =
                Path.GetFileNameWithoutExtension(
                        executablePath)
                    .ToLowerInvariant();

            string[] excludedPathParts =
            {
                "\\appdata\\local\\temp\\",
                "\\appdata\\local\\packages\\",
                "\\cache\\",
                "\\caches\\",
                "\\common files\\",
                "\\crashpad\\",
                "\\debug\\",
                "\\dotnet\\",
                "\\edgecore\\",
                "\\edgeupdate\\",
                "\\edgewebview\\",
                "\\git\\bin\\",
                "\\git\\cmd\\",
                "\\git\\mingw64\\",
                "\\git\\usr\\",
                "\\installer\\",
                "\\locales\\",
                "\\node_modules\\",
                "\\package cache\\",
                "\\pending\\",
                "\\plugins\\",
                "\\redist\\",
                "\\reference assemblies\\",
                "\\resources\\",
                "\\runtimes\\",
                "\\sdk\\",
                "\\updates\\",
                "\\ruxim\\",
                "\\windows defender\\",
                "\\windows kits\\",
                "\\windows photo viewer\\",
                "\\windowsapps\\"
            };

            if (excludedPathParts.Any(
                    normalizedPath.Contains))
            {
                return false;
            }

            string[] excludedNames =
            {
                "bootstrap",
                "broker",
                "ccxprocess",
                "cefsharp",
                "clidmgr",
                "crash",
                "debug",
                "diagnostic",
                "experience_",
                "handler",
                "helper",
                "notification",
                "renderer",
                "rollback",
                "report",
                "service",
                "telemetry",
                "updater",
                "vc_redist"
            };

            if (excludedNames.Any(
                    fileName.Contains))
            {
                return false;
            }

            try
            {
                FileVersionInfo versionInfo =
                    FileVersionInfo.GetVersionInfo(
                        executablePath);
                string productName =
                    versionInfo.ProductName
                    ?? string.Empty;
                string description =
                    versionInfo.FileDescription
                    ?? string.Empty;
                string metadata =
                    (productName
                     + " "
                     + description)
                    .ToLowerInvariant();

                if (new[]
                    {
                        " crash ",
                        " driver",
                        " helper",
                        " module",
                        " rollback",
                        " service",
                        " telemetry",
                        " updater",
                        " update "
                    }.Any(metadata.Contains))
                {
                    return false;
                }

                return (!string.IsNullOrWhiteSpace(
                            productName)
                        && !IsGenericProductName(
                            productName))
                       || (!string.IsNullOrWhiteSpace(
                               description)
                           && !IsGenericProductName(
                               description));
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<InstalledApplication>
            ReduceDirectoryScanResults(
                IEnumerable<InstalledApplication>
                    applications)
        {
            return applications
                .GroupBy(
                    application =>
                        NormalizeProductIdentity(
                            application.Name),
                    StringComparer
                        .CurrentCultureIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            GetExecutableCandidateScore)
                        .ThenBy(application =>
                            application
                                .EffectiveLaunchValue
                                .Length)
                        .First())
                .ToList();
        }

        private static int GetExecutableCandidateScore(
            InstalledApplication application)
        {
            string fileName =
                NormalizeProductIdentity(
                    Path.GetFileNameWithoutExtension(
                        application
                            .EffectiveLaunchValue));
            string productName =
                NormalizeProductIdentity(
                    application.Name);
            int score =
                productName == fileName
                    ? 100
                    : productName.Contains(
                        fileName,
                        StringComparison
                            .OrdinalIgnoreCase)
                      || fileName.Contains(
                          productName,
                          StringComparison
                              .OrdinalIgnoreCase)
                        ? 55
                        : 0;

            int depth =
                application.EffectiveLaunchValue
                    .Count(character =>
                        character
                        == Path.DirectorySeparatorChar);

            return score
                   - depth;
        }

        private static string NormalizeProductIdentity(
            string value) =>
            string.Concat(
                value.Where(
                    char.IsLetterOrDigit))
            .ToLowerInvariant();

        private static bool IsSystemExecutablePath(
            string executablePath)
        {
            string windowsDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .Windows)
                .TrimEnd(
                    Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return executablePath.StartsWith(
                windowsDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPackagedExecutablePath(
            string executablePath) =>
            executablePath.Contains(
                "\\WindowsApps\\",
                StringComparison.OrdinalIgnoreCase);

        private static bool IsNonApplicationRegistration(
            string displayName)
        {
            string value =
                displayName.ToLowerInvariant();

            string[] excludedTerms =
            {
                " redistributable",
                " runtime",
                " framework",
                " language pack",
                " webview2",
                " update",
                " driver",
                " sdk",
                " targeting pack",
                " hostfxr",
                " apphost pack",
                " windows desktop runtime",
                "пакет обновления",
                "драйвер"
            };

            return excludedTerms.Any(
                value.Contains);
        }

        private static int GetSourcePriority(
            string source) =>
            source switch
            {
                "SteamGame"
                    or "EpicGame"
                    or "GogGame"
                    or "GameShortcut"
                    or "ChromePwa"
                    or "EdgePwa" => 0,
                "StartApps" => 1,
                "StartMenu"
                    or "Desktop" => 2,
                "AppPaths" => 3,
                "Registry" => 4,
                "RunningProcess" => 5,
                "FolderScan" => 6,
                _ => 7
            };

        private static bool IsGenericProductName(
            string value)
        {
            string normalized =
                value.Trim().ToLowerInvariant();

            return normalized
                is "microsoft® windows® operating system"
                    or "microsoft windows operating system"
                    or "java platform se binary"
                    or ".net"
                    or ".net runtime"
                    or "setup"
                    or "installer";
        }

        private static bool IsUserDataDirectory(
            string root)
        {
            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData);
            string roamingAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .ApplicationData);

            return root.StartsWith(
                       localAppData,
                       StringComparison.OrdinalIgnoreCase)
                   || root.StartsWith(
                       roamingAppData,
                       StringComparison.OrdinalIgnoreCase);
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
                       StringComparison.CurrentCulture)
                   || normalized.StartsWith(
                       "удаление ",
                       StringComparison.CurrentCulture)
                   || normalized.StartsWith(
                       "check for update",
                       StringComparison.Ordinal)
                   || normalized.StartsWith(
                       "проверить обнов",
                       StringComparison.CurrentCulture)
                   || normalized.StartsWith(
                       "about ",
                       StringComparison.Ordinal);
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

using Microsoft.Win32;
using SmartLauncher.UI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SmartLauncher.UI.Services
{
    public class ApplicationScanner
    {
        private const string UninstallRegistryPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        private const int MaximumDirectoriesPerSearch = 500;

        private const int MaximumSearchDepth = 5;


        public List<InstalledApplication> ScanKnownApplications()
        {
            List<ApplicationDefinition> definitions =
                BuildDefinitions();

            List<RegistryApplicationEntry> registryEntries =
                ReadUninstallRegistryEntries();

            var applications =
                new List<InstalledApplication>();

            foreach (ApplicationDefinition definition
                     in definitions)
            {
                applications.Add(
                    ScanApplication(
                        definition,
                        registryEntries));
            }

            return applications;
        }

        public List<InstalledApplication> ScanAllApplications()
        {
            List<InstalledApplication> knownApplications =
                ScanKnownApplications();

            List<InstalledApplication> genericApplications =
                new GenericApplicationScanner()
                    .Scan();

            return knownApplications
                .Concat(genericApplications)
                .GroupBy(
                    application =>
                        string.IsNullOrWhiteSpace(
                            application.EffectiveLaunchValue)
                            ? application.Id
                            : application.EffectiveLaunchValue,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .GroupBy(
                    application =>
                        NormalizeApplicationName(
                            application.Name),
                    StringComparer
                        .CurrentCultureIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            application =>
                                application.IsFound)
                        .ThenBy(application =>
                            application.Source
                                == "NotFound")
                        .First())
                .OrderBy(application =>
                    application.Name)
                .ToList();
        }

        private static string NormalizeApplicationName(
            string value) =>
            string.Concat(
                value.Where(
                    char.IsLetterOrDigit))
            .ToLowerInvariant();


        private InstalledApplication ScanApplication(
            ApplicationDefinition definition,
            List<RegistryApplicationEntry> registryEntries)
        {
            string executablePath;


            executablePath =
                FindFirstExistingPath(
                    definition.CandidatePaths,
                    definition.ExecutableNames);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "KnownPath");
            }

            executablePath =
                FindInRunningProcesses(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "RunningProcess");
            }

            executablePath =
                FindInPackagedAppsRegistry(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "Package");
            }


            executablePath =
                FindInAppPathsRegistry(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "AppPaths");
            }


            executablePath =
                FindInRegisteredCommands(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "RegisteredCommand");
            }


            executablePath =
                FindSpecialRegistryPath(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "Registry");
            }


            executablePath =
                FindInUninstallRegistry(
                    definition,
                    registryEntries);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "Registry");
            }


            executablePath =
                FindInEnvironmentPath(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "EnvironmentPath");
            }


            executablePath =
                FindInStartMenu(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "StartMenu");
            }


            executablePath =
                FindInLikelyFolders(definition);

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                return CreateFoundApplication(
                    definition,
                    executablePath,
                    "FolderScan");
            }


            return new InstalledApplication
            {
                Id = definition.Id,
                Name = definition.Name,
                ExecutablePath = string.Empty,
                Source = "NotFound",
                Category =
                    ApplicationCategories.Infer(
                        definition.Name,
                        definition.Id)
            };
        }


        private static InstalledApplication CreateFoundApplication(
            ApplicationDefinition definition,
            string executablePath,
            string source)
        {
            return new InstalledApplication
            {
                Id = definition.Id,
                Name = definition.Name,
                ExecutablePath = executablePath,
                LaunchValue = executablePath,
                LaunchKind =
                    ApplicationLaunchKind.Executable,
                Source = source,
                Category =
                    ApplicationCategories.Infer(
                        definition.Name,
                        executablePath),
                IconPath =
                    AssetIconService.GetApplicationIcon(
                        ApplicationCategories.Infer(
                            definition.Name,
                            executablePath))
            };
        }


        private static List<ApplicationDefinition> BuildDefinitions()
        {
            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            string roamingAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string userProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);


            string newestDiscordExecutable =
                FindNewestVersionExecutable(
                    CombinePath(
                        localAppData,
                        "Discord"),
                    "Discord.exe");


            return new List<ApplicationDefinition>
            {
                new ApplicationDefinition
                {
                    Id = "vscode",
                    Name = "Visual Studio Code",

                    ExecutableNames = new[]
                    {
                        "Code.exe"
                    },

                    DisplayNameKeywords = new[]
                    {
                        "Visual Studio Code",
                        "Microsoft VS Code"
                    },

                    FolderKeywords = new[]
                    {
                        "Microsoft VS Code",
                        "VS Code",
                        "vscode"
                    },

                    ShortcutKeywords = new[]
                    {
                        "Visual Studio Code",
                        "VS Code"
                    },

                    ProtocolNames = new[]
                    {
                        "vscode"
                    },

                    CandidatePaths = new[]
                    {
                        CombinePath(
                            localAppData,
                            "Programs",
                            "Microsoft VS Code",
                            "Code.exe"),

                        CombinePath(
                            programFiles,
                            "Microsoft VS Code",
                            "Code.exe"),

                        CombinePath(
                            programFilesX86,
                            "Microsoft VS Code",
                            "Code.exe"),

                        CombinePath(
                            userProfile,
                            "scoop",
                            "apps",
                            "vscode",
                            "current",
                            "Code.exe")
                    }
                },


                new ApplicationDefinition
                {
                    Id = "yandex",
                    Name = "Яндекс Браузер",

                    ExecutableNames = new[]
                    {
                        "browser.exe"
                    },

                    DisplayNameKeywords = new[]
                    {
                        "Yandex Browser",
                        "Яндекс Браузер"
                    },

                    FolderKeywords = new[]
                    {
                        "Yandex",
                        "YandexBrowser",
                        "Яндекс"
                    },

                    ShortcutKeywords = new[]
                    {
                        "Yandex",
                        "Яндекс"
                    },

                    ProtocolNames = new[]
                    {
                        "yandexbrowser"
                    },

                    CandidatePaths = new[]
                    {
                        CombinePath(
                            localAppData,
                            "Yandex",
                            "YandexBrowser",
                            "Application",
                            "browser.exe"),

                        CombinePath(
                            programFiles,
                            "Yandex",
                            "YandexBrowser",
                            "Application",
                            "browser.exe"),

                        CombinePath(
                            programFilesX86,
                            "Yandex",
                            "YandexBrowser",
                            "Application",
                            "browser.exe")
                    }
                },


                new ApplicationDefinition
                {
                    Id = "steam",
                    Name = "Steam",

                    ExecutableNames = new[]
                    {
                        "steam.exe"
                    },

                    DisplayNameKeywords = new[]
                    {
                        "Steam"
                    },

                    FolderKeywords = new[]
                    {
                        "Steam"
                    },

                    ShortcutKeywords = new[]
                    {
                        "Steam"
                    },

                    ProtocolNames = new[]
                    {
                        "steam"
                    },

                    CandidatePaths = new[]
                    {
                        CombinePath(
                            programFilesX86,
                            "Steam",
                            "steam.exe"),

                        CombinePath(
                            programFiles,
                            "Steam",
                            "steam.exe"),

                        CombinePath(
                            userProfile,
                            "scoop",
                            "apps",
                            "steam",
                            "current",
                            "steam.exe")
                    }
                },


                new ApplicationDefinition
                {
                    Id = "discord",
                    Name = "Discord",

                    ExecutableNames = new[]
                    {
                        "Discord.exe"
                    },

                    DisplayNameKeywords = new[]
                    {
                        "Discord"
                    },

                    FolderKeywords = new[]
                    {
                        "Discord"
                    },

                    ShortcutKeywords = new[]
                    {
                        "Discord"
                    },

                    ProtocolNames = new[]
                    {
                        "discord"
                    },

                    CandidatePaths = new[]
                    {
                        newestDiscordExecutable,

                        CombinePath(
                            localAppData,
                            "Programs",
                            "Discord",
                            "Discord.exe"),

                        CombinePath(
                            userProfile,
                            "scoop",
                            "apps",
                            "discord",
                            "current",
                            "Discord.exe")
                    }
                },


                new ApplicationDefinition
                {
                    Id = "telegram",
                    Name = "Telegram",

                    ExecutableNames = new[]
                    {
                        "Telegram.exe"
                    },

                    DisplayNameKeywords = new[]
                    {
                        "Telegram Desktop",
                        "Telegram"
                    },

                    FolderKeywords = new[]
                    {
                        "Telegram Desktop",
                        "Telegram",
                        "tdesktop"
                    },

                    ShortcutKeywords = new[]
                    {
                        "Telegram"
                    },

                    ProtocolNames = new[]
                    {
                        "tg",
                        "tdesktop.tg"
                    },

                    CandidatePaths = new[]
                    {
                        CombinePath(
                            roamingAppData,
                            "Telegram Desktop",
                            "Telegram.exe"),

                        CombinePath(
                            localAppData,
                            "Programs",
                            "Telegram Desktop",
                            "Telegram.exe"),

                        CombinePath(
                            programFiles,
                            "Telegram Desktop",
                            "Telegram.exe"),

                        CombinePath(
                            programFilesX86,
                            "Telegram Desktop",
                            "Telegram.exe"),

                        CombinePath(
                            userProfile,
                            "scoop",
                            "apps",
                            "telegram",
                            "current",
                            "Telegram.exe")
                    }
                }
            };
        }


        private static string FindFirstExistingPath(
            IEnumerable<string> candidatePaths,
            string[] executableNames)
        {
            foreach (string candidatePath
                     in candidatePaths)
            {
                string executablePath =
                    NormalizeAndValidateCandidate(
                        candidatePath,
                        executableNames);

                if (!string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    return executablePath;
                }
            }

            return string.Empty;
        }


        private static string FindInRunningProcesses(
            ApplicationDefinition definition)
        {
            foreach (string executableName
                     in definition.ExecutableNames)
            {
                string processName =
                    Path.GetFileNameWithoutExtension(
                        executableName);

                Process[] processes;

                try
                {
                    processes =
                        Process.GetProcessesByName(
                            processName);
                }
                catch
                {
                    continue;
                }

                string foundPath =
                    string.Empty;

                foreach (Process process in processes)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(
                                foundPath))
                        {
                            continue;
                        }

                        string path =
                            process.MainModule?.FileName
                            ?? string.Empty;

                        string normalized =
                            NormalizeAndValidateCandidate(
                                path,
                                definition.ExecutableNames);

                        if (!string.IsNullOrWhiteSpace(
                                normalized))
                        {
                            foundPath = normalized;
                        }
                    }
                    catch
                    {
                        // Системный процесс может не раскрывать путь.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                if (!string.IsNullOrWhiteSpace(
                        foundPath))
                {
                    return foundPath;
                }
            }

            return string.Empty;
        }


        private static string FindInPackagedAppsRegistry(
            ApplicationDefinition definition)
        {
            const string packagesRegistryPath =
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

            try
            {
                using RegistryKey? packagesKey =
                    Registry.CurrentUser.OpenSubKey(
                        packagesRegistryPath);

                if (packagesKey == null)
                {
                    return string.Empty;
                }

                IEnumerable<string> keywords =
                    definition.DisplayNameKeywords
                        .Concat(definition.FolderKeywords)
                        .Where(keyword =>
                            !string.IsNullOrWhiteSpace(keyword))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase);

                foreach (string packageKeyName
                         in packagesKey.GetSubKeyNames())
                {
                    if (!keywords.Any(keyword =>
                            packageKeyName.Contains(
                                keyword,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    using RegistryKey? packageKey =
                        packagesKey.OpenSubKey(
                            packageKeyName);

                    string packageRoot =
                        packageKey?.GetValue(
                            "PackageRootFolder")
                        as string
                        ?? string.Empty;

                    string executablePath =
                        NormalizeAndValidateCandidate(
                            packageRoot,
                            definition.ExecutableNames);

                    if (!string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        return executablePath;
                    }
                }
            }
            catch
            {
                // Реестр пакетов может быть недоступен.
            }

            return string.Empty;
        }


        private static string FindInAppPathsRegistry(
            ApplicationDefinition definition)
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


            foreach (string executableName
                     in definition.ExecutableNames)
            {
                string registryPath =
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"
                    + executableName;

                foreach (RegistryHive hive in hives)
                {
                    foreach (RegistryView view in views)
                    {
                        try
                        {
                            using RegistryKey baseKey =
                                RegistryKey.OpenBaseKey(
                                    hive,
                                    view);

                            using RegistryKey? applicationKey =
                                baseKey.OpenSubKey(
                                    registryPath);

                            if (applicationKey == null)
                            {
                                continue;
                            }

                            string defaultValue =
                                applicationKey.GetValue(null)
                                    as string
                                ?? string.Empty;

                            string executablePath =
                                NormalizeAndValidateCandidate(
                                    defaultValue,
                                    definition.ExecutableNames);

                            if (!string.IsNullOrWhiteSpace(
                                    executablePath))
                            {
                                return executablePath;
                            }


                            string directoryPath =
                                applicationKey.GetValue("Path")
                                    as string
                                ?? string.Empty;

                            foreach (string expectedExecutable
                                     in definition.ExecutableNames)
                            {
                                executablePath =
                                    NormalizeAndValidateCandidate(
                                        CombinePath(
                                            directoryPath,
                                            expectedExecutable),
                                        definition.ExecutableNames);

                                if (!string.IsNullOrWhiteSpace(
                                        executablePath))
                                {
                                    return executablePath;
                                }
                            }
                        }
                        catch
                        {
                            // Недоступный раздел пропускаем.
                        }
                    }
                }
            }

            return string.Empty;
        }


        private static string FindInRegisteredCommands(
            ApplicationDefinition definition)
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
                    foreach (string executableName
                             in definition.ExecutableNames)
                    {
                        string applicationsCommandPath =
                            @"SOFTWARE\Classes\Applications\"
                            + executableName
                            + @"\shell\open\command";

                        string result =
                            ReadRegisteredCommand(
                                hive,
                                view,
                                applicationsCommandPath,
                                definition.ExecutableNames);

                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            return result;
                        }
                    }


                    foreach (string protocolName
                             in definition.ProtocolNames)
                    {
                        string protocolCommandPath =
                            @"SOFTWARE\Classes\"
                            + protocolName
                            + @"\shell\open\command";

                        string result =
                            ReadRegisteredCommand(
                                hive,
                                view,
                                protocolCommandPath,
                                definition.ExecutableNames);

                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            return result;
                        }
                    }
                }
            }

            return string.Empty;
        }


        private static string ReadRegisteredCommand(
            RegistryHive hive,
            RegistryView view,
            string registryPath,
            string[] executableNames)
        {
            try
            {
                using RegistryKey baseKey =
                    RegistryKey.OpenBaseKey(
                        hive,
                        view);

                using RegistryKey? commandKey =
                    baseKey.OpenSubKey(
                        registryPath);

                if (commandKey == null)
                {
                    return string.Empty;
                }

                string command =
                    commandKey.GetValue(null)
                        as string
                    ?? string.Empty;

                return NormalizeAndValidateCandidate(
                    command,
                    executableNames);
            }
            catch
            {
                return string.Empty;
            }
        }


        private static string FindSpecialRegistryPath(
            ApplicationDefinition definition)
        {
            if (!string.Equals(
                    definition.Id,
                    "steam",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }


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

            string[] valueNames =
            {
                "SteamExe",
                "InstallPath",
                "SteamPath"
            };


            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using RegistryKey baseKey =
                            RegistryKey.OpenBaseKey(
                                hive,
                                view);

                        using RegistryKey? steamKey =
                            baseKey.OpenSubKey(
                                @"SOFTWARE\Valve\Steam");

                        if (steamKey == null)
                        {
                            continue;
                        }


                        foreach (string valueName
                                 in valueNames)
                        {
                            string value =
                                steamKey.GetValue(valueName)
                                    as string
                                ?? string.Empty;

                            string executablePath =
                                NormalizeAndValidateCandidate(
                                    value,
                                    definition.ExecutableNames);

                            if (!string.IsNullOrWhiteSpace(
                                    executablePath))
                            {
                                return executablePath;
                            }


                            executablePath =
                                NormalizeAndValidateCandidate(
                                    CombinePath(
                                        value,
                                        "steam.exe"),
                                    definition.ExecutableNames);

                            if (!string.IsNullOrWhiteSpace(
                                    executablePath))
                            {
                                return executablePath;
                            }
                        }
                    }
                    catch
                    {
                        // Раздел может отсутствовать.
                    }
                }
            }

            return string.Empty;
        }


        private static List<RegistryApplicationEntry>
            ReadUninstallRegistryEntries()
        {
            var entries =
                new List<RegistryApplicationEntry>();

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
                    try
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
                            try
                            {
                                using RegistryKey? applicationKey =
                                    uninstallKey.OpenSubKey(
                                        subKeyName);

                                if (applicationKey == null)
                                {
                                    continue;
                                }

                                string displayName =
                                    applicationKey
                                        .GetValue("DisplayName")
                                        as string
                                    ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(
                                        displayName))
                                {
                                    continue;
                                }

                                entries.Add(
                                    new RegistryApplicationEntry
                                    {
                                        DisplayName =
                                            displayName,

                                        DisplayIcon =
                                            applicationKey
                                                .GetValue("DisplayIcon")
                                                as string
                                            ?? string.Empty,

                                        InstallLocation =
                                            applicationKey
                                                .GetValue("InstallLocation")
                                                as string
                                            ?? string.Empty
                                    });
                            }
                            catch
                            {
                                // Повреждённую запись пропускаем.
                            }
                        }
                    }
                    catch
                    {
                        // Недоступный раздел пропускаем.
                    }
                }
            }


            return entries
                .GroupBy(
                    entry =>
                        entry.DisplayName
                        + "|"
                        + entry.InstallLocation
                        + "|"
                        + entry.DisplayIcon,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }


        private static string FindInUninstallRegistry(
            ApplicationDefinition definition,
            IEnumerable<RegistryApplicationEntry> entries)
        {
            foreach (RegistryApplicationEntry entry
                     in entries)
            {
                if (!ContainsAnyKeyword(
                        entry.DisplayName,
                        definition.DisplayNameKeywords))
                {
                    continue;
                }


                string executablePath =
                    NormalizeAndValidateCandidate(
                        entry.DisplayIcon,
                        definition.ExecutableNames);

                if (!string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    return executablePath;
                }


                executablePath =
                    FindExecutableInsideLocation(
                        entry.InstallLocation,
                        definition.ExecutableNames,
                        MaximumSearchDepth);

                if (!string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    return executablePath;
                }
            }

            return string.Empty;
        }


        private static string FindInEnvironmentPath(
            ApplicationDefinition definition)
        {
            string pathEnvironment =
                Environment.GetEnvironmentVariable("PATH")
                ?? string.Empty;

            string[] directories =
                pathEnvironment.Split(
                    new[]
                    {
                        Path.PathSeparator
                    },
                    StringSplitOptions.RemoveEmptyEntries);


            foreach (string directory
                     in directories)
            {
                string expandedDirectory =
                    Environment.ExpandEnvironmentVariables(
                        directory.Trim().Trim('"'));

                foreach (string executableName
                         in definition.ExecutableNames)
                {
                    string executablePath =
                        NormalizeAndValidateCandidate(
                            CombinePath(
                                expandedDirectory,
                                executableName),
                            definition.ExecutableNames);

                    if (!string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        return executablePath;
                    }
                }
            }

            return string.Empty;
        }


        private static string FindInStartMenu(
            ApplicationDefinition definition)
        {
            string[] startMenuRoots =
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.StartMenu),

                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonStartMenu)
            };


            foreach (string startMenuRoot
                     in startMenuRoots)
            {
                string programsDirectory =
                    CombinePath(
                        startMenuRoot,
                        "Programs");

                foreach (string shortcutPath
                         in EnumerateFilesSafely(
                             programsDirectory,
                             "*.lnk",
                             1500))
                {
                    string shortcutName =
                        Path.GetFileNameWithoutExtension(
                            shortcutPath);

                    if (!ContainsAnyKeyword(
                            shortcutName,
                            definition.ShortcutKeywords))
                    {
                        continue;
                    }


                    string shortcutTarget =
                        ResolveShortcutTarget(
                            shortcutPath);

                    string executablePath =
                        NormalizeAndValidateCandidate(
                            shortcutTarget,
                            definition.ExecutableNames);

                    if (!string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        return executablePath;
                    }


                    string targetDirectory =
                        GetDirectoryNameSafely(
                            shortcutTarget);

                    executablePath =
                        FindExecutableInsideLocation(
                            targetDirectory,
                            definition.ExecutableNames,
                            3);

                    if (!string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        return executablePath;
                    }
                }
            }

            return string.Empty;
        }


        private static string FindInLikelyFolders(
            ApplicationDefinition definition)
        {
            List<string> searchRoots =
                BuildSearchRoots();

            var checkedDirectories =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);


            foreach (string searchRoot
                     in searchRoots)
            {
                if (string.IsNullOrWhiteSpace(searchRoot)
                    || !Directory.Exists(searchRoot))
                {
                    continue;
                }

                string normalizedRoot;

                try
                {
                    normalizedRoot =
                        Path.GetFullPath(searchRoot);
                }
                catch
                {
                    continue;
                }

                if (!checkedDirectories.Add(normalizedRoot))
                {
                    continue;
                }


                string directResult =
                    FindDirectExecutable(
                        normalizedRoot,
                        definition.ExecutableNames);

                if (!string.IsNullOrWhiteSpace(
                        directResult))
                {
                    return directResult;
                }


                if (ContainsAnyKeyword(
                        normalizedRoot,
                        definition.FolderKeywords))
                {
                    string rootResult =
                        FindExecutableInsideLocation(
                            normalizedRoot,
                            definition.ExecutableNames,
                            MaximumSearchDepth);

                    if (!string.IsNullOrWhiteSpace(
                            rootResult))
                    {
                        return rootResult;
                    }
                }


                foreach (string childDirectory
                         in EnumerateDirectoriesSafely(
                             normalizedRoot))
                {
                    string directoryName =
                        Path.GetFileName(
                            childDirectory);

                    if (!ContainsAnyKeyword(
                            directoryName,
                            definition.FolderKeywords))
                    {
                        continue;
                    }

                    string executablePath =
                        FindExecutableInsideLocation(
                            childDirectory,
                            definition.ExecutableNames,
                            MaximumSearchDepth);

                    if (!string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        return executablePath;
                    }
                }
            }

            return string.Empty;
        }


        private static List<string> BuildSearchRoots()
        {
            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            string roamingAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string commonApplicationData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);

            string userProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

            string desktop =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);

            string documents =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);


            var roots =
                new List<string>
                {
                    localAppData,
                    CombinePath(
                        localAppData,
                        "Programs"),

                    roamingAppData,
                    programFiles,
                    programFilesX86,
                    desktop,
                    documents,

                    CombinePath(
                        userProfile,
                        "scoop",
                        "apps"),

                    CombinePath(
                        commonApplicationData,
                        "chocolatey",
                        "bin")
                };


            try
            {
                foreach (DriveInfo drive
                         in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady
                        || drive.DriveType
                           != DriveType.Fixed)
                    {
                        continue;
                    }

                    string driveRoot =
                        drive.RootDirectory.FullName;

                    roots.Add(driveRoot);

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "Apps"));

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "Applications"));

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "Programs"));

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "PortableApps"));

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "Tools"));

                    roots.Add(
                        CombinePath(
                            driveRoot,
                            "Games"));
                }
            }
            catch
            {
                // Недоступные диски пропускаем.
            }


            return roots
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private static string FindExecutableInsideLocation(
            string rootDirectory,
            string[] executableNames,
            int maximumDepth)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)
                || !Directory.Exists(rootDirectory))
            {
                return string.Empty;
            }


            string directExecutable =
                FindDirectExecutable(
                    rootDirectory,
                    executableNames);

            if (!string.IsNullOrWhiteSpace(
                    directExecutable))
            {
                return directExecutable;
            }


            var queue =
                new Queue<DirectorySearchItem>();

            queue.Enqueue(
                new DirectorySearchItem
                {
                    DirectoryPath = rootDirectory,
                    Depth = 0
                });

            var visitedDirectories =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            var foundExecutables =
                new List<string>();

            int visitedCount = 0;


            while (queue.Count > 0
                   && visitedCount
                   < MaximumDirectoriesPerSearch)
            {
                DirectorySearchItem current =
                    queue.Dequeue();

                if (!visitedDirectories.Add(
                        current.DirectoryPath))
                {
                    continue;
                }

                visitedCount++;


                string executablePath =
                    FindDirectExecutable(
                        current.DirectoryPath,
                        executableNames);

                if (!string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    foundExecutables.Add(
                        executablePath);
                }


                if (current.Depth >= maximumDepth)
                {
                    continue;
                }


                foreach (string childDirectory
                         in EnumerateDirectoriesSafely(
                             current.DirectoryPath))
                {
                    if (ShouldSkipDirectory(
                            childDirectory))
                    {
                        continue;
                    }

                    queue.Enqueue(
                        new DirectorySearchItem
                        {
                            DirectoryPath =
                                childDirectory,

                            Depth =
                                current.Depth + 1
                        });
                }
            }


            return foundExecutables
                .OrderByDescending(
                    GetLastWriteTimeUtcSafely)
                .FirstOrDefault()
                ?? string.Empty;
        }


        private static string FindDirectExecutable(
            string directoryPath,
            IEnumerable<string> executableNames)
        {
            if (string.IsNullOrWhiteSpace(directoryPath)
                || !Directory.Exists(directoryPath))
            {
                return string.Empty;
            }


            foreach (string executableName
                     in executableNames)
            {
                string candidatePath =
                    CombinePath(
                        directoryPath,
                        executableName);

                string executablePath =
                    NormalizeAndValidateCandidate(
                        candidatePath,
                        new[]
                        {
                            executableName
                        });

                if (!string.IsNullOrWhiteSpace(
                        executablePath))
                {
                    return executablePath;
                }
            }

            return string.Empty;
        }


        private static string NormalizeAndValidateCandidate(
            string rawCandidate,
            string[] executableNames)
        {
            if (string.IsNullOrWhiteSpace(rawCandidate))
            {
                return string.Empty;
            }


            string candidate =
                Environment.ExpandEnvironmentVariables(
                    rawCandidate.Trim());

            candidate =
                ExtractExecutablePath(candidate);

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }


            try
            {
                if (Directory.Exists(candidate))
                {
                    return FindExecutableInsideLocation(
                        candidate,
                        executableNames,
                        3);
                }

                if (!File.Exists(candidate))
                {
                    return string.Empty;
                }

                string fileName =
                    Path.GetFileName(candidate);

                bool fileNameMatches =
                    executableNames.Any(
                        executableName =>
                            string.Equals(
                                fileName,
                                executableName,
                                StringComparison.OrdinalIgnoreCase));

                if (!fileNameMatches)
                {
                    return string.Empty;
                }

                return Path.GetFullPath(candidate);
            }
            catch
            {
                return string.Empty;
            }
        }


        private static string ExtractExecutablePath(
            string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return string.Empty;
            }

            string value =
                rawValue
                    .Trim()
                    .Replace(
                        @"\\",
                        @"\");


            if (value.StartsWith("\""))
            {
                int closingQuote =
                    value.IndexOf('"', 1);

                if (closingQuote > 1)
                {
                    return value
                        .Substring(
                            1,
                            closingQuote - 1)
                        .Trim();
                }
            }


            int executableEnd =
                value.IndexOf(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase);

            if (executableEnd >= 0)
            {
                return value
                    .Substring(
                        0,
                        executableEnd + 4)
                    .Trim()
                    .Trim('"');
            }


            return value
                .Trim()
                .Trim('"')
                .TrimEnd(',');
        }


        private static string ResolveShortcutTarget(
            string shortcutPath)
        {
            object? shell = null;
            object? shortcut = null;

            try
            {
                Type? shellType =
                    Type.GetTypeFromProgID(
                        "WScript.Shell");

                if (shellType == null)
                {
                    return string.Empty;
                }

                shell =
                    Activator.CreateInstance(
                        shellType);

                if (shell == null)
                {
                    return string.Empty;
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
                    return string.Empty;
                }

                object? targetPath =
                    shortcut.GetType().InvokeMember(
                        "TargetPath",
                        BindingFlags.GetProperty,
                        null,
                        shortcut,
                        null);

                return targetPath as string
                       ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                try
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
                catch
                {
                    // Освобождение COM-объекта не должно
                    // мешать работе сканера.
                }
            }
        }


        private static string FindNewestVersionExecutable(
            string applicationRoot,
            string executableName)
        {
            if (string.IsNullOrWhiteSpace(applicationRoot)
                || !Directory.Exists(applicationRoot))
            {
                return string.Empty;
            }

            try
            {
                foreach (DirectoryInfo directory
                         in new DirectoryInfo(
                                 applicationRoot)
                             .GetDirectories("app-*")
                             .OrderByDescending(
                                 item =>
                                     item.LastWriteTimeUtc))
                {
                    string executablePath =
                        CombinePath(
                            directory.FullName,
                            executableName);

                    if (File.Exists(executablePath))
                    {
                        return executablePath;
                    }
                }
            }
            catch
            {
                // Недоступную папку пропускаем.
            }

            return string.Empty;
        }


        private static IEnumerable<string>
            EnumerateDirectoriesSafely(
                string directoryPath)
        {
            try
            {
                return Directory
                    .EnumerateDirectories(
                        directoryPath)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }


        private static IEnumerable<string>
            EnumerateFilesSafely(
                string rootDirectory,
                string searchPattern,
                int maximumFileCount)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)
                || !Directory.Exists(rootDirectory))
            {
                return Array.Empty<string>();
            }


            var result =
                new List<string>();

            var queue =
                new Queue<string>();

            var visited =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            queue.Enqueue(rootDirectory);


            while (queue.Count > 0
                   && result.Count < maximumFileCount)
            {
                string currentDirectory =
                    queue.Dequeue();

                if (!visited.Add(currentDirectory))
                {
                    continue;
                }

                try
                {
                    foreach (string filePath
                             in Directory.EnumerateFiles(
                                 currentDirectory,
                                 searchPattern))
                    {
                        result.Add(filePath);

                        if (result.Count
                            >= maximumFileCount)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // Недоступную папку пропускаем.
                }


                foreach (string childDirectory
                         in EnumerateDirectoriesSafely(
                             currentDirectory))
                {
                    if (!ShouldSkipDirectory(
                            childDirectory))
                    {
                        queue.Enqueue(
                            childDirectory);
                    }
                }
            }

            return result;
        }


        private static bool ShouldSkipDirectory(
            string directoryPath)
        {
            try
            {
                FileAttributes attributes =
                    File.GetAttributes(
                        directoryPath);

                if ((attributes
                     & FileAttributes.ReparsePoint)
                    != 0)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }


            string directoryName =
                Path.GetFileName(directoryPath);

            string[] skippedNames =
            {
                "node_modules",
                ".git",
                "Cache",
                "Caches",
                "Temp",
                "tmp",
                "Logs",
                "Packages",
                "Package Cache",
                "WindowsApps",
                "$Recycle.Bin",
                "System Volume Information"
            };


            return skippedNames.Any(
                skippedName =>
                    string.Equals(
                        directoryName,
                        skippedName,
                        StringComparison.OrdinalIgnoreCase));
        }


        private static bool ContainsAnyKeyword(
            string text,
            IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return keywords.Any(
                keyword =>
                    !string.IsNullOrWhiteSpace(keyword)
                    && text.Contains(
                        keyword,
                        StringComparison.OrdinalIgnoreCase));
        }


        private static DateTime GetLastWriteTimeUtcSafely(
            string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(
                    filePath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }


        private static string GetDirectoryNameSafely(
            string filePath)
        {
            try
            {
                return Path.GetDirectoryName(filePath)
                       ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        private static string CombinePath(
            string root,
            params string[] parts)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            try
            {
                string result = root;

                foreach (string part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        result =
                            Path.Combine(
                                result,
                                part);
                    }
                }

                return result;
            }
            catch
            {
                return string.Empty;
            }
        }


        private sealed class ApplicationDefinition
        {
            public string Id { get; set; } =
                string.Empty;

            public string Name { get; set; } =
                string.Empty;

            public string[] ExecutableNames { get; set; } =
                Array.Empty<string>();

            public string[] DisplayNameKeywords { get; set; } =
                Array.Empty<string>();

            public string[] FolderKeywords { get; set; } =
                Array.Empty<string>();

            public string[] ShortcutKeywords { get; set; } =
                Array.Empty<string>();

            public string[] ProtocolNames { get; set; } =
                Array.Empty<string>();

            public string[] CandidatePaths { get; set; } =
                Array.Empty<string>();
        }


        private sealed class RegistryApplicationEntry
        {
            public string DisplayName { get; set; } =
                string.Empty;

            public string DisplayIcon { get; set; } =
                string.Empty;

            public string InstallLocation { get; set; } =
                string.Empty;
        }


        private sealed class DirectorySearchItem
        {
            public string DirectoryPath { get; set; } =
                string.Empty;

            public int Depth { get; set; }
        }
    }
}

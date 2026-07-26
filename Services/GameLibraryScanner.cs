using Microsoft.Win32;
using SmartLauncher.UI.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartLauncher.UI.Services
{
    public sealed class GameLibraryScanner
    {
        private const string SteamRegistryPath =
            @"SOFTWARE\Valve\Steam";

        private const string GogRegistryPath =
            @"SOFTWARE\GOG.com\Games";

        public List<InstalledApplication> Scan()
        {
            var applications =
                new List<InstalledApplication>();

            ScanSafely(
                "Steam",
                () => applications.AddRange(
                    ScanSteamGames()));
            ScanSafely(
                "Epic Games",
                () => applications.AddRange(
                    ScanEpicGames()));
            ScanSafely(
                "GOG",
                () => applications.AddRange(
                    ScanGogGames()));
            ScanSafely(
                "game shortcuts",
                () => applications.AddRange(
                    ScanGameInternetShortcuts()));

            return applications
                .Where(application =>
                    application.IsFound)
                .GroupBy(
                    CreateGameIdentity,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(application =>
                    application.Name)
                .ToList();
        }

        private static void ScanSafely(
            string source,
            Action scanAction)
        {
            try
            {
                scanAction();
            }
            catch (Exception exception)
            {
                AppLogService.Warning(
                    $"Game scan ({source}) failed: "
                    + exception.Message);
            }
        }

        private static IEnumerable<InstalledApplication>
            ScanSteamGames()
        {
            HashSet<string> steamRoots =
                FindSteamRoots();
            var libraryRoots =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string steamRoot
                     in steamRoots)
            {
                AddDirectoryIfExists(
                    libraryRoots,
                    steamRoot);
                AddSteamLibrariesFromVdf(
                    steamRoot,
                    libraryRoots);
            }

            foreach (string libraryRoot
                     in libraryRoots)
            {
                string steamAppsDirectory =
                    Path.Combine(
                        libraryRoot,
                        "steamapps");
                if (!Directory.Exists(
                        steamAppsDirectory))
                {
                    continue;
                }

                IEnumerable<string> manifests;
                try
                {
                    manifests =
                        Directory.EnumerateFiles(
                                steamAppsDirectory,
                                "appmanifest_*.acf",
                                SearchOption.TopDirectoryOnly)
                            .ToList();
                }
                catch
                {
                    continue;
                }

                foreach (string manifestPath
                         in manifests)
                {
                    string text;
                    try
                    {
                        text =
                            File.ReadAllText(
                                manifestPath);
                    }
                    catch
                    {
                        continue;
                    }

                    string appId =
                        ReadVdfValue(
                            text,
                            "appid");
                    string name =
                        ReadVdfValue(
                            text,
                            "name");
                    string installDirectoryName =
                        ReadVdfValue(
                            text,
                            "installdir");

                    if (string.IsNullOrWhiteSpace(appId)
                        || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string gameDirectory =
                        string.IsNullOrWhiteSpace(
                            installDirectoryName)
                            ? string.Empty
                            : Path.Combine(
                                steamAppsDirectory,
                                "common",
                                installDirectoryName);
                    string executablePath =
                        FindLikelyGameExecutable(
                            gameDirectory,
                            name);
                    yield return
                        new InstalledApplication
                        {
                            Id =
                                "game-steam-"
                                + appId,
                            Name = name.Trim(),
                            ExecutablePath =
                                executablePath,
                            LaunchValue =
                                "steam://rungameid/"
                                + appId,
                            LaunchKind =
                                ApplicationLaunchKind
                                    .Protocol,
                            Source = "SteamGame",
                            IconPath =
                                AssetIconService
                                    .GamingIcon,
                            Category = "Игры"
                        };
                }
            }
        }

        private static HashSet<string>
            FindSteamRoots()
        {
            var roots =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            AddDirectoryIfExists(
                roots,
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .ProgramFilesX86),
                    "Steam"));
            AddDirectoryIfExists(
                roots,
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .ProgramFiles),
                    "Steam"));

            foreach (RegistryView view
                     in new[]
                     {
                         RegistryView.Registry64,
                         RegistryView.Registry32
                     })
            {
                try
                {
                    using RegistryKey baseKey =
                        RegistryKey.OpenBaseKey(
                            RegistryHive.CurrentUser,
                            view);
                    using RegistryKey? steamKey =
                        baseKey.OpenSubKey(
                            SteamRegistryPath);

                    string steamPath =
                        steamKey?.GetValue(
                            "SteamPath") as string
                        ?? string.Empty;
                    AddDirectoryIfExists(
                        roots,
                        steamPath);

                    string steamExe =
                        steamKey?.GetValue(
                            "SteamExe") as string
                        ?? string.Empty;
                    AddDirectoryIfExists(
                        roots,
                        Path.GetDirectoryName(
                            steamExe)
                        ?? string.Empty);
                }
                catch
                {
                    // Другой registry view может быть доступен.
                }
            }

            return roots;
        }

        private static void AddSteamLibrariesFromVdf(
            string steamRoot,
            HashSet<string> libraryRoots)
        {
            string libraryFile =
                Path.Combine(
                    steamRoot,
                    "steamapps",
                    "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                return;
            }

            string text;
            try
            {
                text =
                    File.ReadAllText(
                        libraryFile);
            }
            catch
            {
                return;
            }

            foreach (Match match
                     in Regex.Matches(
                         text,
                         "\"path\"\\s+\"(?<path>[^\"]+)\"",
                         RegexOptions.IgnoreCase))
            {
                AddDirectoryIfExists(
                    libraryRoots,
                    UnescapeVdfPath(
                        match.Groups["path"].Value));
            }

            foreach (Match match
                     in Regex.Matches(
                         text,
                         "\"\\d+\"\\s+\"(?<path>[A-Za-z]:[^\"]+)\"",
                         RegexOptions.IgnoreCase))
            {
                AddDirectoryIfExists(
                    libraryRoots,
                    UnescapeVdfPath(
                        match.Groups["path"].Value));
            }
        }

        private static string FindSteamIcon(
            IEnumerable<string> steamRoots,
            string appId)
        {
            foreach (string steamRoot
                     in steamRoots)
            {
                string cacheDirectory =
                    Path.Combine(
                        steamRoot,
                        "appcache",
                        "librarycache");

                foreach (string extension
                         in new[]
                         {
                             ".jpg",
                             ".png",
                             ".ico"
                         })
                {
                    string iconPath =
                        Path.Combine(
                            cacheDirectory,
                            appId + "_icon"
                            + extension);
                    if (File.Exists(iconPath))
                    {
                        return iconPath;
                    }
                }

                string appCacheDirectory =
                    Path.Combine(
                        cacheDirectory,
                        appId);
                if (!Directory.Exists(
                        appCacheDirectory))
                {
                    continue;
                }

                try
                {
                    string? iconPath =
                        Directory.EnumerateFiles(
                                appCacheDirectory,
                                "*",
                                SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(path =>
                                Path.GetFileName(path)
                                    .Contains(
                                        "icon",
                                        StringComparison
                                            .OrdinalIgnoreCase)
                                && IsImageFile(path));

                    if (!string.IsNullOrWhiteSpace(
                            iconPath))
                    {
                        return iconPath;
                    }
                }
                catch
                {
                    // Кэш иконок необязателен.
                }
            }

            return string.Empty;
        }

        private static IEnumerable<InstalledApplication>
            ScanEpicGames()
        {
            string manifestDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .CommonApplicationData),
                    "Epic",
                    "EpicGamesLauncher",
                    "Data",
                    "Manifests");
            if (!Directory.Exists(
                    manifestDirectory))
            {
                yield break;
            }

            IEnumerable<string> manifestFiles;
            try
            {
                manifestFiles =
                    Directory.EnumerateFiles(
                            manifestDirectory,
                            "*.item",
                            SearchOption.TopDirectoryOnly)
                        .ToList();
            }
            catch
            {
                yield break;
            }

            foreach (string manifestPath
                     in manifestFiles)
            {
                JsonDocument document;
                try
                {
                    document =
                        JsonDocument.Parse(
                            File.ReadAllText(
                                manifestPath));
                }
                catch
                {
                    continue;
                }

                using (document)
                {
                    JsonElement root =
                        document.RootElement;
                    string name =
                        ReadJsonString(
                            root,
                            "DisplayName");
                    string appName =
                        ReadJsonString(
                            root,
                            "AppName");
                    string catalogItemId =
                        ReadJsonString(
                            root,
                            "CatalogItemId");
                    string installLocation =
                        ReadJsonString(
                            root,
                            "InstallLocation");
                    string launchExecutable =
                        ReadJsonString(
                            root,
                            "LaunchExecutable");

                    if (string.IsNullOrWhiteSpace(name)
                        || string.IsNullOrWhiteSpace(
                            appName))
                    {
                        continue;
                    }

                    string executablePath =
                        ResolveGameExecutable(
                            installLocation,
                            launchExecutable,
                            name);
                    string identity =
                        string.IsNullOrWhiteSpace(
                            catalogItemId)
                            ? appName
                            : catalogItemId;

                    yield return
                        new InstalledApplication
                        {
                            Id =
                                "game-epic-"
                                + CreateStableId(
                                    identity),
                            Name = name.Trim(),
                            ExecutablePath =
                                executablePath,
                            LaunchValue =
                                "com.epicgames.launcher://apps/"
                                + Uri.EscapeDataString(
                                    appName)
                                + "?action=launch&silent=true",
                            LaunchKind =
                                ApplicationLaunchKind
                                    .Protocol,
                            Source = "EpicGame",
                            Category = "Игры"
                        };
                }
            }
        }

        private static IEnumerable<InstalledApplication>
            ScanGogGames()
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
                    RegistryKey? gamesKey = null;
                    try
                    {
                        RegistryKey baseKey =
                            RegistryKey.OpenBaseKey(
                                hive,
                                view);
                        gamesKey =
                            baseKey.OpenSubKey(
                                GogRegistryPath);

                        if (gamesKey == null)
                        {
                            baseKey.Dispose();
                            continue;
                        }

                        foreach (string gameId
                                 in gamesKey
                                     .GetSubKeyNames())
                        {
                            using RegistryKey? gameKey =
                                gamesKey.OpenSubKey(
                                    gameId);
                            if (gameKey == null)
                            {
                                continue;
                            }

                            string name =
                                gameKey.GetValue(
                                    "gameName") as string
                                ?? string.Empty;
                            string gamePath =
                                gameKey.GetValue(
                                    "path") as string
                                ?? string.Empty;
                            string executable =
                                gameKey.GetValue(
                                    "exe") as string
                                ?? string.Empty;
                            string launchCommand =
                                gameKey.GetValue(
                                    "launchCommand") as string
                                ?? string.Empty;

                            string executablePath =
                                ResolveGameExecutable(
                                    gamePath,
                                    string.IsNullOrWhiteSpace(
                                        executable)
                                        ? ReadExecutableFromCommand(
                                            launchCommand)
                                        : executable,
                                    name);

                            if (string.IsNullOrWhiteSpace(name)
                                || !File.Exists(
                                    executablePath))
                            {
                                continue;
                            }

                            yield return
                                new InstalledApplication
                                {
                                    Id =
                                        "game-gog-"
                                        + CreateStableId(
                                            gameId),
                                    Name = name.Trim(),
                                    ExecutablePath =
                                        executablePath,
                                    LaunchValue =
                                        executablePath,
                                    LaunchKind =
                                        ApplicationLaunchKind
                                            .Executable,
                                    Source = "GogGame",
                                    Category = "Игры"
                                };
                        }

                        baseKey.Dispose();
                    }
                    finally
                    {
                        gamesKey?.Dispose();
                    }
                }
            }
        }

        private static IEnumerable<InstalledApplication>
            ScanGameInternetShortcuts()
        {
            string[] roots =
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .DesktopDirectory),
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .CommonDesktopDirectory),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .StartMenu),
                    "Programs"),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .CommonStartMenu),
                    "Programs")
            };

            foreach (string root
                     in roots.Distinct(
                         StringComparer.OrdinalIgnoreCase))
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
                                "*.url",
                                SearchOption.AllDirectories)
                            .ToList();
                }
                catch
                {
                    continue;
                }

                foreach (string shortcutPath
                         in shortcuts)
                {
                    string[] lines;
                    try
                    {
                        lines =
                            File.ReadAllLines(
                                shortcutPath);
                    }
                    catch
                    {
                        continue;
                    }

                    string launchValue =
                        ReadInternetShortcutValue(
                            lines,
                            "URL");
                    if (!Uri.TryCreate(
                            launchValue,
                            UriKind.Absolute,
                            out Uri? uri)
                        || !IsSupportedGameProtocol(
                            uri.Scheme))
                    {
                        continue;
                    }

                    yield return
                        new InstalledApplication
                        {
                            Id =
                                "game-shortcut-"
                                + CreateStableId(
                                    launchValue),
                            Name =
                                Path.GetFileNameWithoutExtension(
                                    shortcutPath),
                            ExecutablePath =
                                string.Empty,
                            LaunchValue =
                                launchValue,
                            LaunchKind =
                                ApplicationLaunchKind
                                    .Protocol,
                            Source = "GameShortcut",
                            IconPath =
                                AssetIconService
                                    .GamingIcon,
                            Category = "Игры"
                        };
                }
            }
        }

        private static string ResolveGameExecutable(
            string installDirectory,
            string executableValue,
            string gameName)
        {
            string normalizedValue =
                executableValue
                    .Trim()
                    .Trim('"');

            if (!string.IsNullOrWhiteSpace(
                    normalizedValue))
            {
                string candidate =
                    Path.IsPathFullyQualified(
                        normalizedValue)
                        ? normalizedValue
                        : Path.Combine(
                            installDirectory,
                            normalizedValue);

                try
                {
                    candidate =
                        Path.GetFullPath(candidate);
                    if (File.Exists(candidate)
                        && string.Equals(
                            Path.GetExtension(candidate),
                            ".exe",
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Используем поиск по папке.
                }
            }

            return FindLikelyGameExecutable(
                installDirectory,
                gameName);
        }

        private static string FindLikelyGameExecutable(
            string gameDirectory,
            string gameName)
        {
            if (string.IsNullOrWhiteSpace(
                    gameDirectory)
                || !Directory.Exists(
                    gameDirectory))
            {
                return string.Empty;
            }

            try
            {
                var options =
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true,
                        AttributesToSkip =
                            FileAttributes.ReparsePoint
                    };

                string normalizedGameName =
                    NormalizeName(gameName);

                return Directory
                    .EnumerateFiles(
                        gameDirectory,
                        "*.exe",
                        options)
                    .Take(500)
                    .Where(path =>
                        !IsMaintenanceExecutable(
                            path))
                    .OrderByDescending(path =>
                        NormalizeName(
                            Path.GetFileNameWithoutExtension(
                                path))
                            .Contains(
                                normalizedGameName,
                                StringComparison
                                    .OrdinalIgnoreCase))
                    .ThenBy(path =>
                        path.Count(character =>
                            character
                                == Path.DirectorySeparatorChar))
                    .ThenByDescending(path =>
                        new FileInfo(path).Length)
                    .FirstOrDefault()
                    ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsMaintenanceExecutable(
            string path)
        {
            string name =
                Path.GetFileNameWithoutExtension(path)
                    .ToLowerInvariant();

            return new[]
            {
                "unins",
                "uninstall",
                "setup",
                "crash",
                "report",
                "redist",
                "vcredist",
                "dxsetup",
                "easyanticheat",
                "unitycrashhandler"
            }.Any(name.Contains);
        }

        private static string ReadVdfValue(
            string text,
            string key)
        {
            Match match =
                Regex.Match(
                    text,
                    "\""
                    + Regex.Escape(key)
                    + "\"\\s+\"(?<value>[^\"]*)\"",
                    RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups["value"].Value
                : string.Empty;
        }

        private static string ReadJsonString(
            JsonElement element,
            string propertyName)
        {
            return element.TryGetProperty(
                       propertyName,
                       out JsonElement value)
                   && value.ValueKind
                       == JsonValueKind.String
                ? value.GetString()
                  ?? string.Empty
                : string.Empty;
        }

        private static string ReadExecutableFromCommand(
            string command)
        {
            string value =
                command.Trim();
            if (value.StartsWith('"'))
            {
                int closingQuote =
                    value.IndexOf('"', 1);
                return closingQuote > 1
                    ? value[1..closingQuote]
                    : string.Empty;
            }

            int executableEnd =
                value.IndexOf(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase);
            return executableEnd >= 0
                ? value[..(executableEnd + 4)]
                : value;
        }

        private static string ReadInternetShortcutValue(
            IEnumerable<string> lines,
            string key)
        {
            string prefix = key + "=";
            string? line =
                lines.FirstOrDefault(value =>
                    value.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase));

            return line == null
                ? string.Empty
                : line[prefix.Length..].Trim();
        }

        private static bool IsSupportedGameProtocol(
            string scheme) =>
            scheme.Equals(
                "steam",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "com.epicgames.launcher",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "goggalaxy",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "uplay",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "origin2",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "link2ea",
                StringComparison.OrdinalIgnoreCase)
            || scheme.Equals(
                "battlenet",
                StringComparison.OrdinalIgnoreCase);

        private static void AddDirectoryIfExists(
            HashSet<string> directories,
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            try
            {
                string fullPath =
                    Path.GetFullPath(
                        directory.Replace(
                            '/',
                            Path.DirectorySeparatorChar));
                if (Directory.Exists(fullPath))
                {
                    directories.Add(fullPath);
                }
            }
            catch
            {
                // Некорректный путь пропускается.
            }
        }

        private static string UnescapeVdfPath(
            string value) =>
            value.Replace(
                    @"\\",
                    @"\")
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar);

        private static bool IsImageFile(
            string path) =>
            Path.GetExtension(path)
                .Equals(
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path)
                .Equals(
                    ".png",
                    StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path)
                .Equals(
                    ".ico",
                    StringComparison.OrdinalIgnoreCase);

        private static string NormalizeName(
            string value) =>
            string.Concat(
                value.Where(
                    char.IsLetterOrDigit))
            .ToLowerInvariant();

        private static string CreateStableId(
            string value)
        {
            byte[] hash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value.ToLowerInvariant()));
            return Convert.ToHexString(hash)
                [..16]
                .ToLowerInvariant();
        }

        private static string CreateGameIdentity(
            InstalledApplication application)
        {
            string launchValue =
                application.EffectiveLaunchValue.Trim();

            if (!string.IsNullOrWhiteSpace(launchValue))
            {
                return application.LaunchKind
                       + ":"
                       + launchValue;
            }

            return application.Id;
        }
    }
}

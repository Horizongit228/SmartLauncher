using SmartLauncher.UI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SmartLauncher.UI.Services
{
    public class LauncherDataService
    {
        private readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };

        private readonly string _storageDirectory;

        public LauncherDataService()
        {
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SmartLauncher");

            DataFilePath = Path.Combine(
                _storageDirectory,
                "modes.json");

            SettingsFilePath = Path.Combine(
                _storageDirectory,
                "settings.json");
        }

        public string DataFilePath { get; }

        public string SettingsFilePath { get; }

        public LauncherData LoadOrCreate(AppCatalog catalog)
        {
            LauncherData? data =
                AtomicJsonStorage.ReadWithBackup<LauncherData>(
                    DataFilePath,
                    _jsonOptions,
                    out bool recovered);

            if (data?.Modes?.Count > 0)
            {
                Normalize(data);

                if (recovered)
                {
                    Save(data);
                }

                return data;
            }

            LauncherData defaultData =
                CreateDefaultData(catalog);

            Save(defaultData);
            return defaultData;
        }

        public AppSettings LoadSettings()
        {
            AppSettings? settings =
                AtomicJsonStorage.ReadWithBackup<AppSettings>(
                    SettingsFilePath,
                    _jsonOptions,
                    out bool recovered);

            if (settings != null)
            {
                bool migrated =
                    settings.LegacyMinimizeToTray.HasValue;

                if (settings.LegacyMinimizeToTray
                    is bool legacyValue)
                {
                    settings.CloseToTray = legacyValue;
                    settings.LegacyMinimizeToTray = null;
                }

                settings.LaunchDelayMilliseconds =
                    Math.Clamp(
                        settings.LaunchDelayMilliseconds,
                        0,
                        10000);

                settings.WindowTransparency =
                    Math.Clamp(
                        settings.WindowTransparency,
                        0.78,
                        1.0);

                settings.UpdateManifestUrl ??=
                    string.Empty;

                if (recovered || migrated)
                {
                    SaveSettings(settings);
                }

                return settings;
            }

            return new AppSettings();
        }

        public void Save(LauncherData data)
        {
            Normalize(data);
            EnsureStorageDirectory();

            AtomicJsonStorage.Write(
                DataFilePath,
                data,
                _jsonOptions);
        }

        public void SaveSettings(AppSettings settings)
        {
            EnsureStorageDirectory();

            settings.LegacyMinimizeToTray = null;

            AtomicJsonStorage.Write(
                SettingsFilePath,
                settings,
                _jsonOptions);
        }

        public bool UpgradeKnownApplications(
            LauncherData data,
            AppCatalog catalog)
        {
            if (data.Version >= 2)
            {
                return false;
            }

            InstalledApplication? telegram =
                catalog.Applications.FirstOrDefault(
                    application =>
                        string.Equals(
                            application.Id,
                            "telegram",
                            StringComparison.OrdinalIgnoreCase)
                        && application.IsFound);

            if (telegram == null)
            {
                return false;
            }

            LauncherMode? workMode =
                data.Modes.FirstOrDefault(mode =>
                    string.Equals(
                        mode.Name,
                        "Work Mode",
                        StringComparison.OrdinalIgnoreCase));

            if (workMode != null
                && !workMode.Targets.Any(target =>
                    string.Equals(
                        target.Name,
                        "Telegram",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.GetFileName(target.Value),
                        "Telegram.exe",
                        StringComparison.OrdinalIgnoreCase)))
            {
                var telegramTarget =
                    new LaunchTarget
                    {
                        Name = "Telegram",
                        Type =
                            LaunchTargetType.Application,
                        Value =
                            telegram.ExecutablePath
                    };

                int firstWebTarget =
                    workMode.Targets.FindIndex(target =>
                        target.Type
                        == LaunchTargetType.Website);

                if (firstWebTarget >= 0)
                {
                    workMode.Targets.Insert(
                        firstWebTarget,
                        telegramTarget);
                }
                else
                {
                    workMode.Targets.Add(
                        telegramTarget);
                }
            }

            data.Version = 2;
            Save(data);
            return true;
        }

        public bool UpgradeYouTubeWebApplication(
            LauncherData data,
            AppCatalog catalog)
        {
            if (data.Version >= 4)
            {
                return false;
            }

            InstalledApplication? youtubeApplication =
                catalog.Applications
                    .Where(application =>
                        application.IsFound
                        && application.LaunchKind
                            == ApplicationLaunchKind.WebApplication)
                    .FirstOrDefault(application =>
                        string.Equals(
                            application.Name,
                            "YouTube",
                            StringComparison.CurrentCultureIgnoreCase));

            if (youtubeApplication == null)
            {
                return false;
            }

            LauncherMode? relaxMode =
                data.Modes.FirstOrDefault(mode =>
                    string.Equals(
                        mode.Name,
                        "Relax Mode",
                        StringComparison.OrdinalIgnoreCase));

            LaunchTarget? youtubeTarget =
                relaxMode?.Targets.FirstOrDefault(target =>
                    string.Equals(
                        target.Name,
                        "YouTube",
                        StringComparison.OrdinalIgnoreCase)
                    || target.Value.Contains(
                        "youtube.com",
                        StringComparison.OrdinalIgnoreCase));

            if (youtubeTarget != null)
            {
                youtubeTarget.Type =
                    LaunchTargetType.Application;
                youtubeTarget.Value =
                    youtubeApplication.EffectiveLaunchValue;
                youtubeTarget.ApplicationId =
                    youtubeApplication.Id;
            }

            data.Version = 4;
            Save(data);
            return youtubeTarget != null;
        }

        public void Export(string destinationPath, LauncherData data)
        {
            Normalize(data);
            AtomicJsonStorage.Write(
                destinationPath,
                data,
                _jsonOptions);
        }

        public LauncherData Import(string sourcePath)
        {
            string json = File.ReadAllText(sourcePath);
            LauncherData? data =
                JsonSerializer.Deserialize<LauncherData>(
                    json,
                    _jsonOptions);

            if (data?.Modes == null)
            {
                throw new InvalidDataException(
                    "Файл не содержит режимы Smart Launcher.");
            }

            Normalize(data);

            foreach (LaunchTarget command
                     in data.Modes
                         .SelectMany(mode => mode.Targets)
                         .Where(target =>
                             target.Type
                             == LaunchTargetType.Command))
            {
                command.IsTrusted = false;
            }

            Save(data);
            return data;
        }

        private LauncherData CreateDefaultData(AppCatalog catalog)
        {
            InstalledApplication? Find(string id) =>
                catalog.Applications.FirstOrDefault(
                    app => string.Equals(
                        app.Id,
                        id,
                        StringComparison.OrdinalIgnoreCase));

            LaunchTarget? AppTarget(
                string id,
                string displayName)
            {
                InstalledApplication? app = Find(id);
                return app?.IsFound == true
                    ? new LaunchTarget
                    {
                        Name = displayName,
                        Type = LaunchTargetType.Application,
                        Value = app.EffectiveLaunchValue,
                        ApplicationId = app.Id
                    }
                    : null;
            }

            List<LaunchTarget> WithoutNulls(
                params LaunchTarget?[] targets) =>
                targets.Where(target => target != null)
                    .Cast<LaunchTarget>()
                    .ToList();

            InstalledApplication? youtubeApplication =
                catalog.Applications
                    .Where(application =>
                        application.IsFound
                        && application.LaunchKind
                            == ApplicationLaunchKind.WebApplication)
                    .FirstOrDefault(application =>
                        string.Equals(
                            application.Name,
                            "YouTube",
                            StringComparison.CurrentCultureIgnoreCase));

            bool hasYouTubeApplication =
                youtubeApplication != null;

            return new LauncherData
            {
                Version =
                    hasYouTubeApplication ? 4 : 2,
                Modes = new List<LauncherMode>
                {
                    new()
                    {
                        Name = "Work Mode",
                        Description = "Инструменты для продуктивной работы",
                        Icon = "/Assets/Icons/Work.png",
                        AccentColor = "#3B7BFF",
                        Targets = WithoutNulls(
                            AppTarget("yandex", "Яндекс Браузер"),
                            AppTarget("vscode", "Visual Studio Code"),
                            AppTarget("telegram", "Telegram"),
                            new LaunchTarget
                            {
                                Name = "ChatGPT",
                                Type = LaunchTargetType.Website,
                                Value = "https://chatgpt.com"
                            })
                    },
                    new()
                    {
                        Name = "Gaming Mode",
                        Description = "Игры и общение",
                        Icon = "/Assets/Icons/Gaming.png",
                        AccentColor = "#8B5CF6",
                        Targets = WithoutNulls(
                            AppTarget("steam", "Steam"),
                            AppTarget("discord", "Discord"))
                    },
                    new()
                    {
                        Name = "Relax Mode",
                        Description = "Видео, музыка и отдых",
                        Icon = "/Assets/Icons/Relax.png",
                        AccentColor = "#16A085",
                        Targets = new List<LaunchTarget>
                        {
                            new()
                            {
                                Name = "YouTube",
                                Type =
                                    hasYouTubeApplication
                                        ? LaunchTargetType.Application
                                        : LaunchTargetType.Website,
                                Value =
                                    hasYouTubeApplication
                                        ? youtubeApplication!
                                            .EffectiveLaunchValue
                                        : "https://youtube.com",
                                ApplicationId =
                                    youtubeApplication?.Id
                                    ?? string.Empty
                            }
                        }
                    }
                }
            };
        }

        private static void Normalize(LauncherData data)
        {
            data.Modes ??= new List<LauncherMode>();

            foreach (LauncherMode mode in data.Modes)
            {
                mode.Id = string.IsNullOrWhiteSpace(mode.Id)
                    ? Guid.NewGuid().ToString("N")
                    : mode.Id;

                mode.Name ??= string.Empty;
                mode.Icon ??= string.Empty;
                mode.Description ??= string.Empty;
                mode.AccentColor =
                    string.IsNullOrWhiteSpace(mode.AccentColor)
                        ? "#2F6DF4"
                        : mode.AccentColor;

                mode.Targets ??= new List<LaunchTarget>();

                foreach (LaunchTarget target in mode.Targets)
                {
                    target.Id = string.IsNullOrWhiteSpace(target.Id)
                        ? Guid.NewGuid().ToString("N")
                        : target.Id;

                    target.Name ??= string.Empty;
                    target.Value ??= string.Empty;
                    target.ApplicationId ??= string.Empty;
                    target.ProjectFiles ??= new List<string>();
                    target.ProjectFileSets ??=
                        new List<ProjectFileSet>();

                    if (target.Type == LaunchTargetType.Project
                        && target.ProjectFileSets.Count == 0
                        && target.ProjectFiles.Count > 0)
                    {
                        target.ProjectFileSets.Add(
                            new ProjectFileSet
                            {
                                Name = "Основной набор",
                                Files =
                                    new List<string>(
                                        target.ProjectFiles)
                            });
                    }

                    foreach (ProjectFileSet fileSet
                             in target.ProjectFileSets)
                    {
                        fileSet.Id =
                            string.IsNullOrWhiteSpace(fileSet.Id)
                                ? Guid.NewGuid().ToString("N")
                                : fileSet.Id;

                        fileSet.Name =
                            string.IsNullOrWhiteSpace(fileSet.Name)
                                ? "Набор файлов"
                                : fileSet.Name;

                        fileSet.Files ??=
                            new List<string>();
                    }
                }
            }
        }

        private void EnsureStorageDirectory()
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }
}

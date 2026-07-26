using SmartLauncher.UI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmartLauncher.UI.Services
{
    public class AppCatalogService
    {
        private const int CurrentSchemaVersion = 10;

        private readonly ApplicationScanner _scanner;

        private readonly JsonSerializerOptions _jsonOptions;

        private readonly string _storageDirectory;


        public AppCatalogService()
        {
            _scanner =
                new ApplicationScanner();

            _jsonOptions =
                new JsonSerializerOptions
                {
                    WriteIndented = true,

                    PropertyNameCaseInsensitive = true,

                    Encoder =
                        JavaScriptEncoder
                            .UnsafeRelaxedJsonEscaping
                };

            _storageDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData),
                    "SmartLauncher");

            CatalogFilePath =
                Path.Combine(
                    _storageDirectory,
                    "apps.json");
        }


        public string CatalogFilePath { get; }


        public bool PerformedInitialScan { get; private set; }


        public AppCatalog LoadOrScan()
        {
            Directory.CreateDirectory(
                _storageDirectory);

            if (!File.Exists(CatalogFilePath))
            {
                PerformedInitialScan = true;

                return ScanAndSave();
            }


            AppCatalog? catalog =
                AtomicJsonStorage.ReadWithBackup<AppCatalog>(
                    CatalogFilePath,
                    _jsonOptions,
                    out bool recovered);

            if (catalog == null
                || catalog.Applications == null
                || catalog.Applications.Count == 0)
            {
                return ScanAndSave();
            }

            bool requiresCatalogUpgrade =
                catalog.SchemaVersion
                < CurrentSchemaVersion;
            NormalizeCatalog(catalog);

            if (recovered)
            {
                Save(catalog);
            }

            TimeSpan catalogAge =
                DateTime.UtcNow.Subtract(
                    catalog.ScannedAtUtc);

            bool catalogIsOld =
                catalogAge.TotalHours >= 24;

            bool missingApplicationsShouldBeChecked =
                catalog.Applications.Any(
                    application =>
                        !application.IsFound
                        && !application.IsUserAdded
                        && (catalogAge.TotalHours >= 6
                            || string.Equals(
                                application.Id,
                                "telegram",
                                StringComparison.OrdinalIgnoreCase)));

            bool storedPathBecameInvalid =
                catalog.Applications.Any(
                    application =>
                        application.LaunchKind
                        is not ApplicationLaunchKind.PackagedApp
                            and not ApplicationLaunchKind.Protocol
                        && !string.IsNullOrWhiteSpace(
                            application.EffectiveLaunchValue)
                        && !File.Exists(
                            application.EffectiveLaunchValue));

            if (requiresCatalogUpgrade
                || catalogIsOld
                || missingApplicationsShouldBeChecked
                || storedPathBecameInvalid)
            {
                return ScanAndSavePreservingPaths(
                    catalog,
                    preserveAutomaticFallback:
                        !requiresCatalogUpgrade);
            }

            return catalog;
        }


        public AppCatalog Refresh(
            AppCatalog currentCatalog)
        {
            if (currentCatalog == null)
            {
                return ScanAndSave();
            }

            return ScanAndSavePreservingPaths(
                currentCatalog,
                preserveAutomaticFallback: true);
        }


        public void SetManualPath(
            AppCatalog catalog,
            string applicationId,
            string applicationName,
            string executablePath)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(
                    nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(
                    applicationId))
            {
                throw new ArgumentException(
                    "Не указан идентификатор приложения.",
                    nameof(applicationId));
            }

            if (string.IsNullOrWhiteSpace(
                    executablePath)
                || !File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "Выбранный файл приложения не существует.",
                    executablePath);
            }


            string normalizedPath =
                Path.GetFullPath(
                    executablePath);

            if (!string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Необходимо выбрать файл с расширением .exe.");
            }


            InstalledApplication? application =
                catalog.Applications
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.Id,
                                applicationId,
                                StringComparison.OrdinalIgnoreCase));

            if (application == null)
            {
                application =
                    new InstalledApplication
                    {
                        Id = applicationId,
                        Name = applicationName
                    };

                catalog.Applications.Add(
                    application);
            }


            application.Name =
                applicationName;

            application.ExecutablePath =
                normalizedPath;

            application.LaunchValue =
                normalizedPath;

            application.LaunchKind =
                ApplicationLaunchKind.Executable;

            application.Source = "Manual";
            application.IsUserAdded = true;

            Save(catalog);
        }

        public InstalledApplication AddManualApplication(
            AppCatalog catalog,
            string applicationName,
            string executablePath,
            string category)
        {
            if (string.IsNullOrWhiteSpace(applicationName))
            {
                throw new InvalidOperationException(
                    "Укажите название приложения.");
            }

            string normalizedPath =
                Path.GetFullPath(executablePath);

            if (!File.Exists(normalizedPath)
                || !string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException(
                    "Необходимо выбрать существующий EXE-файл.",
                    normalizedPath);
            }

            InstalledApplication? existing =
                catalog.Applications.FirstOrDefault(
                    application =>
                        string.Equals(
                            application.EffectiveLaunchValue,
                            normalizedPath,
                            StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Name = applicationName.Trim();
                existing.Category =
                    string.IsNullOrWhiteSpace(category)
                        ? "Другое"
                        : category.Trim();
                existing.IsUserAdded = true;
                existing.Source = "User";
                Save(catalog);
                return existing;
            }

            var application =
                new InstalledApplication
                {
                    Id =
                        "user-"
                        + Guid.NewGuid().ToString("N"),
                    Name = applicationName.Trim(),
                    ExecutablePath = normalizedPath,
                    LaunchValue = normalizedPath,
                    LaunchKind =
                        ApplicationLaunchKind.Executable,
                    Source = "User",
                    Category =
                        string.IsNullOrWhiteSpace(category)
                            ? "Другое"
                            : category.Trim(),
                    IsUserAdded = true
                };

            catalog.Applications.Add(application);
            Save(catalog);
            return application;
        }

        public bool RemoveUserApplication(
            AppCatalog catalog,
            string applicationId)
        {
            InstalledApplication? application =
                catalog.Applications.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Id,
                            applicationId,
                            StringComparison.OrdinalIgnoreCase));

            if (application == null
                || !application.IsUserAdded)
            {
                return false;
            }

            catalog.Applications.Remove(application);
            Save(catalog);
            return true;
        }


        public void Save(
            AppCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(
                    nameof(catalog));
            }

            NormalizeCatalog(catalog);
            AtomicJsonStorage.Write(
                CatalogFilePath,
                catalog,
                _jsonOptions);
        }


        private AppCatalog ScanAndSave()
        {
            var catalog =
                new AppCatalog
                {
                    SchemaVersion =
                        CurrentSchemaVersion,
                    ScannedAtUtc =
                        DateTime.UtcNow,

                    Applications =
                        _scanner
                            .ScanAllApplications()
                };

            Save(catalog);

            return catalog;
        }


        private AppCatalog ScanAndSavePreservingPaths(
            AppCatalog previousCatalog,
            bool preserveAutomaticFallback)
        {
            List<InstalledApplication> scannedApplications =
                _scanner.ScanAllApplications();

            List<InstalledApplication> previousApplications =
                previousCatalog.Applications
                ?? new List<InstalledApplication>();


            foreach (InstalledApplication previousApplication
                     in previousApplications)
            {
                InstalledApplication? scannedApplication =
                    scannedApplications
                        .FirstOrDefault(
                            application =>
                                string.Equals(
                                    application.Id,
                                    previousApplication.Id,
                                    StringComparison.OrdinalIgnoreCase)
                                || (!string.IsNullOrWhiteSpace(
                                        application.EffectiveLaunchValue)
                                    && string.Equals(
                                        application.EffectiveLaunchValue,
                                        previousApplication.EffectiveLaunchValue,
                                        StringComparison.OrdinalIgnoreCase))
                                || (application.LaunchKind
                                        == ApplicationLaunchKind.WebApplication
                                    && previousApplication.LaunchKind
                                        == ApplicationLaunchKind.WebApplication
                                    && string.Equals(
                                        application.Name,
                                        previousApplication.Name,
                                        StringComparison.CurrentCultureIgnoreCase)));


                bool validManualPath =
                    previousApplication.IsUserAdded
                    && previousApplication.IsFound;

                if (scannedApplication != null
                    && File.Exists(
                        previousApplication.IconPath))
                {
                    scannedApplication.IconPath =
                        previousApplication.IconPath;
                }


                if (validManualPath)
                {
                    if (scannedApplication == null)
                    {
                        scannedApplications.Add(
                            CloneApplication(
                                previousApplication));
                    }
                    else
                    {
                        scannedApplication.Name =
                            previousApplication.Name;

                        scannedApplication.ExecutablePath =
                            previousApplication.ExecutablePath;

                        scannedApplication.LaunchValue =
                            previousApplication.LaunchValue;

                        scannedApplication.LaunchKind =
                            previousApplication.LaunchKind;

                        scannedApplication.Category =
                            previousApplication.Category;

                        scannedApplication.IsUserAdded = true;
                        scannedApplication.Source = "User";
                    }

                    continue;
                }


                bool validPreviousAutomaticPath =
                    previousApplication.IsFound;

                bool newScanDidNotFindApplication =
                    scannedApplication == null
                    || !scannedApplication.IsFound;


                if (preserveAutomaticFallback
                    && validPreviousAutomaticPath
                    && newScanDidNotFindApplication)
                {
                    if (scannedApplication == null)
                    {
                        InstalledApplication cachedApplication =
                            CloneApplication(
                                previousApplication);

                        cachedApplication.Source =
                            "Cached";

                        scannedApplications.Add(
                            cachedApplication);
                    }
                    else
                    {
                        scannedApplication.ExecutablePath =
                            previousApplication.ExecutablePath;

                        scannedApplication.LaunchValue =
                            previousApplication.LaunchValue;

                        scannedApplication.LaunchKind =
                            previousApplication.LaunchKind;

                        scannedApplication.Source =
                            "Cached";
                    }
                }
            }


            var catalog =
                new AppCatalog
                {
                    SchemaVersion =
                        CurrentSchemaVersion,
                    ScannedAtUtc =
                        DateTime.UtcNow,

                    Applications =
                        scannedApplications
                            .GroupBy(
                                application =>
                                    application.Id,
                                StringComparer.OrdinalIgnoreCase)
                            .Select(group =>
                                group.First())
                            .OrderBy(
                                application =>
                                    application.Name)
                            .ToList()
                };

            Save(catalog);

            return catalog;
        }

        private static void NormalizeCatalog(
            AppCatalog catalog)
        {
            catalog.SchemaVersion =
                CurrentSchemaVersion;
            catalog.Applications ??=
                new List<InstalledApplication>();

            foreach (InstalledApplication application
                     in catalog.Applications)
            {
                application.Id ??= string.Empty;
                application.Name ??= string.Empty;
                application.ExecutablePath ??=
                    string.Empty;
                application.LaunchValue ??=
                    string.Empty;
                application.Source ??= string.Empty;
                application.IconPath ??=
                    string.Empty;
                application.Category =
                    string.IsNullOrWhiteSpace(
                        application.Category)
                        ? "Другое"
                        : application.Category;

                if (string.IsNullOrWhiteSpace(
                        application.LaunchValue))
                {
                    application.LaunchValue =
                        application.ExecutablePath;
                }

                if (string.Equals(
                        application.Source,
                        "Manual",
                        StringComparison.OrdinalIgnoreCase))
                {
                    application.IsUserAdded = true;
                }
            }
        }


        private static InstalledApplication CloneApplication(
            InstalledApplication source)
        {
            return new InstalledApplication
            {
                Id = source.Id,
                Name = source.Name,
                ExecutablePath =
                    source.ExecutablePath,
                LaunchValue = source.LaunchValue,
                LaunchKind = source.LaunchKind,
                Source = source.Source,
                IconPath = source.IconPath,
                Category = source.Category,
                IsUserAdded = source.IsUserAdded
            };
        }
    }
}

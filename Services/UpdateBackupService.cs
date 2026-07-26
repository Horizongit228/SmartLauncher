using System.IO;
using System.IO.Compression;

namespace SmartLauncher.UI.Services
{
    public sealed class UpdateBackupService
    {
        private const int BackupsToKeep = 5;

        private readonly string _storageDirectory;
        private readonly string _updateBackupDirectory;

        public UpdateBackupService()
        {
            _storageDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData),
                    "SmartLauncher");

            _updateBackupDirectory =
                Path.Combine(
                    _storageDirectory,
                    "Backups",
                    "Updates");
        }

        public string CreateBeforeUpdateBackup(
            Version currentVersion,
            string targetVersion)
        {
            Directory.CreateDirectory(
                _storageDirectory);
            Directory.CreateDirectory(
                _updateBackupDirectory);
            SetHidden(_updateBackupDirectory);
            RemoveIncompleteBackups();

            string safeTargetVersion =
                string.Concat(
                    targetVersion.Where(character =>
                        char.IsLetterOrDigit(character)
                        || character == '.'));
            if (string.IsNullOrWhiteSpace(
                    safeTargetVersion))
            {
                safeTargetVersion = "unknown";
            }

            string backupName =
                $"before-update-{currentVersion.ToString(3)}"
                + $"-to-{safeTargetVersion}-"
                + $"{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
            string backupPath =
                Path.Combine(
                    _updateBackupDirectory,
                    backupName);
            string temporaryPath =
                backupPath + ".tmp";

            try
            {
                {
                    using FileStream archiveStream =
                        new(
                            temporaryPath,
                            FileMode.CreateNew,
                            FileAccess.ReadWrite,
                            FileShare.None);
                    using var archive =
                        new ZipArchive(
                            archiveStream,
                            ZipArchiveMode.Create,
                            leaveOpen: false);

                    foreach (string filePath
                             in EnumerateUserDataFiles())
                    {
                        AddFile(
                            archive,
                            filePath);
                    }
                }

                File.Move(
                    temporaryPath,
                    backupPath,
                    overwrite: true);
                SetHidden(backupPath);
                RemoveExpiredBackups();

                return backupPath;
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        private IEnumerable<string>
            EnumerateUserDataFiles()
        {
            if (!Directory.Exists(
                    _storageDirectory))
            {
                yield break;
            }

            string excludedDirectory =
                Path.GetFullPath(
                    _updateBackupDirectory)
                + Path.DirectorySeparatorChar;

            foreach (string fileName
                     in new[]
                     {
                         "modes.json",
                         "modes.json.bak",
                         "settings.json",
                         "settings.json.bak",
                         "apps.json",
                         "apps.json.bak"
                     })
            {
                string filePath =
                    Path.Combine(
                        _storageDirectory,
                        fileName);
                if (File.Exists(filePath))
                {
                    yield return filePath;
                }
            }

            foreach (string directoryName
                     in new[]
                     {
                         "Icons",
                         "Logs",
                         "Backups"
                     })
            {
                string directory =
                    Path.Combine(
                        _storageDirectory,
                        directoryName);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files =
                        Directory.EnumerateFiles(
                                directory,
                                "*",
                                SearchOption.AllDirectories)
                            .ToList();
                }
                catch (Exception exception)
                {
                    throw new IOException(
                        "Не удалось прочитать данные "
                        + "для резервного копирования.",
                        exception);
                }

                foreach (string filePath in files)
                {
                    string fullPath =
                        Path.GetFullPath(filePath);
                    if (fullPath.StartsWith(
                            excludedDirectory,
                            StringComparison.OrdinalIgnoreCase)
                        || fullPath.EndsWith(
                            ".download",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return fullPath;
                }
            }
        }

        private void AddFile(
            ZipArchive archive,
            string filePath)
        {
            string entryName =
                Path.GetRelativePath(
                    _storageDirectory,
                    filePath)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');

            try
            {
                ZipArchiveEntry entry =
                    archive.CreateEntry(
                        entryName,
                        CompressionLevel.Optimal);

                using Stream source =
                    new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite
                        | FileShare.Delete);
                using Stream destination =
                    entry.Open();
                source.CopyTo(destination);
            }
            catch (Exception exception)
            {
                AppLogService.Warning(
                    "Файл не добавлен в резервную копию: "
                    + $"{filePath}. {exception.Message}");
            }
        }

        private void RemoveExpiredBackups()
        {
            IEnumerable<string> expiredBackups =
                Directory.EnumerateFiles(
                        _updateBackupDirectory,
                        "before-update-*.zip",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(
                        File.GetLastWriteTimeUtc)
                    .Skip(BackupsToKeep)
                    .ToList();

            foreach (string backupPath
                     in expiredBackups)
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception exception)
                {
                    AppLogService.Warning(
                        "Не удалось удалить старую "
                        + "резервную копию: "
                        + exception.Message);
                }
            }
        }

        private void RemoveIncompleteBackups()
        {
            foreach (string temporaryPath
                     in Directory.EnumerateFiles(
                         _updateBackupDirectory,
                         "*.tmp",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    AppLogService.Warning(
                        "Не удалось удалить незавершённую "
                        + "резервную копию: "
                        + exception.Message);
                }
            }
        }

        private static void SetHidden(
            string path)
        {
            try
            {
                FileAttributes attributes =
                    File.GetAttributes(path);
                File.SetAttributes(
                    path,
                    attributes
                    | FileAttributes.Hidden);
            }
            catch (Exception exception)
            {
                AppLogService.Warning(
                    "Не удалось скрыть резервную копию: "
                    + exception.Message);
            }
        }
    }
}

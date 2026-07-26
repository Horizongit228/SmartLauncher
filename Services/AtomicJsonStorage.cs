using System;
using System.IO;
using System.Text.Json;

namespace SmartLauncher.UI.Services
{
    public static class AtomicJsonStorage
    {
        public static T? ReadWithBackup<T>(
            string filePath,
            JsonSerializerOptions options,
            out bool recoveredFromBackup)
        {
            recoveredFromBackup = false;

            List<string> candidates =
                new()
                {
                    filePath,
                    filePath + ".bak"
                };

            candidates.AddRange(
                EnumerateRollingBackups(
                    filePath));

            foreach (string candidate
                     in candidates.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    T? recovered =
                        Read<T>(
                            candidate,
                            options);

                    if (recovered == null)
                    {
                        continue;
                    }

                    recoveredFromBackup =
                        !string.Equals(
                            candidate,
                            filePath,
                            StringComparison.OrdinalIgnoreCase);

                    if (recoveredFromBackup)
                    {
                        AppLogService.Warning(
                            $"Recovered data from {candidate}.");
                    }

                    return recovered;
                }
                catch (Exception exception)
                {
                    AppLogService.Error(
                        $"Failed to read {candidate}.",
                        exception);
                }
            }

            return default;
        }

        public static void Write<T>(
            string filePath,
            T value,
            JsonSerializerOptions options)
        {
            string? directory =
                Path.GetDirectoryName(filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json =
                JsonSerializer.Serialize(
                    value,
                    options);

            _ = JsonSerializer.Deserialize<T>(
                    json,
                    options)
                ?? throw new InvalidDataException(
                    "JSON validation failed.");

            string temporaryPath =
                filePath + ".tmp";
            string backupPath =
                filePath + ".bak";

            try
            {
                File.WriteAllText(
                    temporaryPath,
                    json);

                if (File.Exists(filePath))
                {
                    File.Copy(
                        filePath,
                        backupPath,
                        overwrite: true);
                    CreateRollingBackup(
                        filePath);
                }

                File.Move(
                    temporaryPath,
                    filePath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static T? Read<T>(
            string filePath,
            JsonSerializerOptions options)
        {
            if (!File.Exists(filePath))
            {
                return default;
            }

            string json =
                File.ReadAllText(filePath);

            return JsonSerializer.Deserialize<T>(
                json,
                options);
        }

        private static void CreateRollingBackup(
            string filePath)
        {
            string backupDirectory =
                GetRollingBackupDirectory(
                    filePath);
            Directory.CreateDirectory(
                backupDirectory);

            string backupPath =
                Path.Combine(
                    backupDirectory,
                    $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-"
                    + Path.GetFileName(filePath));

            File.Copy(
                filePath,
                backupPath,
                overwrite: false);

            foreach (string oldBackup
                     in Directory
                         .EnumerateFiles(
                             backupDirectory,
                             "*-"
                             + Path.GetFileName(filePath))
                         .OrderByDescending(
                             File.GetLastWriteTimeUtc)
                         .Skip(5))
            {
                File.Delete(oldBackup);
            }
        }

        private static IEnumerable<string>
            EnumerateRollingBackups(
                string filePath)
        {
            string backupDirectory =
                GetRollingBackupDirectory(
                    filePath);
            if (!Directory.Exists(backupDirectory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory
                    .EnumerateFiles(
                        backupDirectory,
                        "*-" + Path.GetFileName(filePath))
                    .OrderByDescending(
                        File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string GetRollingBackupDirectory(
            string filePath)
        {
            return Path.Combine(
                Path.GetDirectoryName(filePath)
                    ?? AppContext.BaseDirectory,
                "Backups",
                Path.GetFileNameWithoutExtension(
                    filePath));
        }
    }
}

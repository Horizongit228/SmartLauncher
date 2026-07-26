using SmartLauncher.UI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartLauncher.UI.Services
{
    public sealed class LauncherService : IDisposable
    {
        private readonly Dictionary<string, List<TrackedProcess>>
            _modeProcesses =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly object _syncRoot = new();

        public async Task<ModeLaunchResult> StartModeAsync(
            LauncherMode mode,
            int delayMilliseconds,
            CancellationToken cancellationToken = default)
        {
            var result = new ModeLaunchResult();

            List<LaunchTarget> targets =
                mode.Targets
                    .Where(target =>
                        target.IsEnabled
                        && !string.IsNullOrWhiteSpace(target.Value))
                    .ToList();

            for (int index = 0;
                 index < targets.Count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LaunchTarget target = targets[index];

                if (IsTargetRunning(target))
                {
                    result.SkippedCount++;
                    AppLogService.Info(
                        $"Пропущено уже запущенное действие: "
                        + $"{mode.Name} / {target.DisplayName}");
                }
                else
                {
                    try
                    {
                        Process? process = StartTarget(target);

                        if (process != null)
                        {
                            try
                            {
                                if (!process.HasExited
                                    && target.Type
                                    is LaunchTargetType.Application
                                    or LaunchTargetType.Command)
                                {
                                    TrackProcess(mode.Id, process);
                                }
                                else
                                {
                                    process.Dispose();
                                }
                            }
                            catch
                            {
                                process.Dispose();
                            }
                        }

                        result.LaunchedCount++;
                        AppLogService.Info(
                            $"Запущено действие: "
                            + $"{mode.Name} / {target.DisplayName}");
                    }
                    catch (Exception exception)
                    {
                        result.Errors.Add(
                            $"{target.DisplayName}: {exception.Message}");
                        AppLogService.Error(
                            $"Ошибка запуска: "
                            + $"{mode.Name} / {target.DisplayName}",
                            exception);
                    }
                }

                if (index < targets.Count - 1
                    && delayMilliseconds > 0)
                {
                    await Task.Delay(
                        delayMilliseconds,
                        cancellationToken);
                }
            }

            return result;
        }

        public bool Open(string target)
        {
            try
            {
                StartTarget(new LaunchTarget
                {
                    Name = target,
                    Type = GuessType(target),
                    Value = target
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void StopMode(string modeId)
        {
            List<TrackedProcess> processes;

            lock (_syncRoot)
            {
                if (!_modeProcesses.TryGetValue(
                        modeId,
                        out List<TrackedProcess>? tracked))
                {
                    return;
                }

                processes = tracked.ToList();
                _modeProcesses.Remove(modeId);
            }

            foreach (TrackedProcess trackedProcess
                     in processes)
            {
                Process process = trackedProcess.Process;
                try
                {
                    if (!IsOwnedAndRunning(
                            trackedProcess))
                    {
                        process.Dispose();
                        continue;
                    }

                    if (process.CloseMainWindow())
                    {
                        process.WaitForExit(1500);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Процесс мог завершиться самостоятельно.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        public bool IsModeRunning(LauncherMode mode)
        {
            return HasTrackedProcesses(mode.Id)
                   || GetRunningApplicationCount(mode) > 0;
        }

        public bool HasTrackedProcesses(string modeId)
        {
            lock (_syncRoot)
            {
                if (_modeProcesses.TryGetValue(
                        modeId,
                        out List<TrackedProcess>? processes))
                {
                    processes.RemoveAll(process =>
                        !IsOwnedAndRunning(process));

                    if (processes.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public int GetRunningApplicationCount(
            LauncherMode mode)
        {
            return mode.Targets.Count(target =>
                target.IsEnabled
                && IsTargetRunning(target));
        }

        public bool IsTargetRunning(LaunchTarget target)
        {
            if (target.Type
                != LaunchTargetType.Application)
            {
                return false;
            }

            try
            {
                string processName =
                    Path.GetFileNameWithoutExtension(target.Value);

                if (string.IsNullOrWhiteSpace(processName))
                {
                    return false;
                }

                Process[] processes =
                    Process.GetProcessesByName(processName);

                bool isRunning = processes.Length > 0;

                foreach (Process process in processes)
                {
                    process.Dispose();
                }

                return isRunning;
            }
            catch
            {
                return false;
            }
        }

        private Process? StartTarget(LaunchTarget target)
        {
            return target.Type switch
            {
                LaunchTargetType.Application =>
                    StartApplication(target.Value),

                LaunchTargetType.File =>
                    StartExistingPath(target.Value, requireFile: true),

                LaunchTargetType.Folder =>
                    StartExistingPath(target.Value, requireFile: false),

                LaunchTargetType.Website =>
                    StartWebAddress(target.Value),

                LaunchTargetType.Steam =>
                    StartSteamTarget(target.Value),

                LaunchTargetType.Command =>
                    StartCommand(target.Value),

                LaunchTargetType.Project =>
                    StartProject(target),

                _ => throw new InvalidOperationException(
                    "Неизвестный тип цели запуска.")
            };
        }

        private static Process? StartProject(
            LaunchTarget target)
        {
            string projectDirectory =
                Path.GetFullPath(target.Value);

            if (!Directory.Exists(projectDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Папка проекта не найдена: {projectDirectory}");
            }

            List<string> fileReferences =
                target.ProjectFileSets.Count > 0
                    ? target.ProjectFileSets
                        .Where(set => set.IsEnabled)
                        .SelectMany(set => set.Files)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : target.ProjectFiles.ToList();

            if (fileReferences.Count == 0
                && !target.OpenProjectFolder)
            {
                throw new InvalidOperationException(
                    "Для проекта не выбраны файлы или папка.");
            }

            var errors = new List<string>();

            if (target.OpenProjectFolder)
            {
                try
                {
                    Process? folderProcess =
                        StartExistingPath(
                            projectDirectory,
                            requireFile: false);
                    folderProcess?.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Папка проекта: {exception.Message}");
                }
            }

            foreach (string fileReference
                     in fileReferences)
            {
                try
                {
                    string filePath =
                        Path.IsPathFullyQualified(fileReference)
                            ? fileReference
                            : Path.GetFullPath(
                                Path.Combine(
                                    projectDirectory,
                                    fileReference));

                    Process? process =
                        StartExistingPath(
                            filePath,
                            requireFile: true);

                    process?.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"{fileReference}: {exception.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Join(
                        Environment.NewLine,
                        errors));
            }

            return null;
        }

        private static Process? StartApplication(
            string launchValue)
        {
            if (launchValue.StartsWith(
                    "shell:AppsFolder\\",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = launchValue,
                        UseShellExecute = true
                    });
            }

            if (Uri.TryCreate(
                    launchValue,
                    UriKind.Absolute,
                    out Uri? protocolUri)
                && IsSupportedApplicationProtocol(
                    protocolUri.Scheme))
            {
                Process? protocolProcess =
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = launchValue,
                            UseShellExecute = true
                        });
                protocolProcess?.Dispose();
                return null;
            }

            return StartExistingPath(
                launchValue,
                requireFile: true);
        }

        private static bool IsSupportedApplicationProtocol(
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

        private static Process? StartExistingPath(
            string path,
            bool requireFile)
        {
            bool exists =
                requireFile
                    ? File.Exists(path)
                    : Directory.Exists(path);

            if (!exists)
            {
                throw new FileNotFoundException(
                    "Путь не найден.",
                    path);
            }

            return Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory =
                        requireFile
                            ? Path.GetDirectoryName(path)
                                ?? string.Empty
                            : path,
                    UseShellExecute = true
                });
        }

        private static Process? StartWebAddress(string address)
        {
            if (!Uri.TryCreate(
                    address,
                    UriKind.Absolute,
                    out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp
                    && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    "Некорректный адрес сайта.");
            }

            return Process.Start(
                new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
        }

        private static Process? StartSteamTarget(string value)
        {
            string address = value.StartsWith(
                "steam://",
                StringComparison.OrdinalIgnoreCase)
                    ? value
                    : "steam://rungameid/" + value.Trim();

            return Process.Start(
                new ProcessStartInfo
                {
                    FileName = address,
                    UseShellExecute = true
                });
        }

        private static Process? StartCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException(
                    "Команда не указана.");
            }

            return Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /s /c \"" + command + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
        }

        private void TrackProcess(
            string modeId,
            Process process)
        {
            lock (_syncRoot)
            {
                if (!_modeProcesses.TryGetValue(
                        modeId,
                        out List<TrackedProcess>? processes))
                {
                    processes =
                        new List<TrackedProcess>();
                    _modeProcesses[modeId] = processes;
                }

                try
                {
                    processes.Add(
                        new TrackedProcess(
                            process,
                            process.Id,
                            process.StartTime.ToUniversalTime()));
                }
                catch
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsOwnedAndRunning(
            TrackedProcess trackedProcess)
        {
            try
            {
                if (trackedProcess.Process.HasExited
                    || trackedProcess.Process.Id
                        != trackedProcess.ProcessId)
                {
                    return false;
                }

                using Process current =
                    Process.GetProcessById(
                        trackedProcess.ProcessId);

                TimeSpan startDifference =
                    (current.StartTime.ToUniversalTime()
                     - trackedProcess.StartTimeUtc)
                    .Duration();

                return startDifference
                    < TimeSpan.FromSeconds(1);
            }
            catch
            {
                return false;
            }
        }

        private static LaunchTargetType GuessType(string target)
        {
            if (Uri.TryCreate(
                    target,
                    UriKind.Absolute,
                    out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp
                    || uri.Scheme == Uri.UriSchemeHttps))
            {
                return LaunchTargetType.Website;
            }

            if (Directory.Exists(target))
            {
                return LaunchTargetType.Folder;
            }

            return LaunchTargetType.Application;
        }

        public void Dispose()
        {
            List<TrackedProcess> processes;

            lock (_syncRoot)
            {
                processes =
                    _modeProcesses.Values
                        .SelectMany(items => items)
                        .ToList();
                _modeProcesses.Clear();
            }

            foreach (TrackedProcess process
                     in processes)
            {
                process.Process.Dispose();
            }
        }

        private sealed record TrackedProcess(
            Process Process,
            int ProcessId,
            DateTime StartTimeUtc);
    }
}

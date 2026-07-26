using System.IO;
using System.Text.Json.Serialization;

namespace SmartLauncher.UI.Models
{
    public enum ApplicationLaunchKind
    {
        Executable,
        Shortcut,
        PackagedApp,
        WebApplication,
        Protocol
    }

    public class InstalledApplication
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ExecutablePath { get; set; } = string.Empty;

        public string LaunchValue { get; set; } = string.Empty;

        public ApplicationLaunchKind LaunchKind { get; set; }

        public string Source { get; set; } = string.Empty;

        public string IconPath { get; set; } = string.Empty;

        public string Category { get; set; } = "Другое";

        public bool IsUserAdded { get; set; }

        [JsonIgnore]
        public string EffectiveLaunchValue =>
            string.IsNullOrWhiteSpace(LaunchValue)
                ? ExecutablePath
                : LaunchValue;

        [JsonIgnore]
        public bool IsFound =>
            LaunchKind
                is ApplicationLaunchKind.PackagedApp
                    or ApplicationLaunchKind.Protocol
                ? !string.IsNullOrWhiteSpace(EffectiveLaunchValue)
                : LaunchKind
                    == ApplicationLaunchKind.Executable
                    ? File.Exists(EffectiveLaunchValue)
                      && string.Equals(
                          Path.GetExtension(
                              EffectiveLaunchValue),
                          ".exe",
                          StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrWhiteSpace(
                          EffectiveLaunchValue)
                      && File.Exists(
                          EffectiveLaunchValue);

        [JsonIgnore]
        public string PathText =>
            IsFound
                ? EffectiveLaunchValue
                : "Путь к приложению не указан";

        [JsonIgnore]
        public string StatusText
        {
            get
            {
                if (!IsFound)
                {
                    return "Не найдено";
                }

                return Source switch
                {
                    "Manual" => "Указано вручную",
                    "KnownPath" => "Стандартный путь",
                    "AppPaths" => "Найдено через App Paths",
                    "RegisteredCommand" => "Найдено через Windows",
                    "Registry" => "Найдено в реестре",
                    "EnvironmentPath" => "Найдено в PATH",
                    "StartMenu" => "Найдено в меню Пуск",
                    "Desktop" => "Найдено на рабочем столе",
                    "FolderScan" => "Найдено сканированием",
                    "RunningProcess" => "Найдено среди запущенных",
                    "Package" => "Приложение Microsoft Store",
                    "StartApps" => "Приложение Windows",
                    "ChromePwa" => "Приложение Chrome",
                    "EdgePwa" => "Приложение Edge",
                    "SteamGame" => "Игра Steam",
                    "EpicGame" => "Игра Epic Games",
                    "GogGame" => "Игра GOG",
                    "GameShortcut" => "Игровой ярлык",
                    "User" => "Добавлено пользователем",
                    "Cached" => "Сохранённый путь",
                    _ => "Найдено"
                };
            }
        }

        [JsonIgnore]
        public string StatusColor =>
            IsFound ? "#62D49A" : "#E6A85C";
    }
}

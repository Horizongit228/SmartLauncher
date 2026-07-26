using SmartLauncher.UI.Models;

namespace SmartLauncher.UI.Services
{
    public sealed class AssetIconService
    {
        public const string AppsIcon =
            "/Assets/Icons/Apps.png";
        public const string DashboardIcon =
            "/Assets/Icons/Dashboard.png";
        public const string GamingIcon =
            "/Assets/Icons/Gaming.png";
        public const string ModesIcon =
            "/Assets/Icons/Modes.png";
        public const string RelaxIcon =
            "/Assets/Icons/Relax.png";
        public const string SettingsIcon =
            "/Assets/Icons/Settings.png";
        public const string WorkIcon =
            "/Assets/Icons/Work.png";

        private static readonly IReadOnlyList<ModeIconOption>
            ModeOptions =
                new[]
                {
                    CreateOption(
                        "Работа",
                        WorkIcon),
                    CreateOption(
                        "Игры",
                        GamingIcon),
                    CreateOption(
                        "Отдых",
                        RelaxIcon),
                    CreateOption(
                        "Приложения",
                        AppsIcon),
                    CreateOption(
                        "Главная",
                        DashboardIcon),
                    CreateOption(
                        "Сценарии",
                        ModesIcon),
                    CreateOption(
                        "Настройки",
                        SettingsIcon)
                };

        private static readonly HashSet<string>
            AllowedModeIcons =
                ModeOptions
                    .Select(option => option.Path)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

        public void PopulateIcons(
            AppCatalog catalog)
        {
            foreach (InstalledApplication application
                     in catalog.Applications)
            {
                application.IconPath =
                    GetApplicationIcon(
                        application.Category);
            }
        }

        public IReadOnlyList<ModeIconOption>
            GetModeIconOptions() =>
            ModeOptions
                .Select(option =>
                    new ModeIconOption
                    {
                        Name = option.Name,
                        Path = option.Path,
                        SourceText =
                            option.SourceText
                    })
                .ToList();

        public static string NormalizeModeIcon(
            string? iconPath) =>
            !string.IsNullOrWhiteSpace(iconPath)
            && AllowedModeIcons.Contains(iconPath)
                ? iconPath
                : AppsIcon;

        public static string GetApplicationIcon(
            string? category) =>
            category switch
            {
                ApplicationCategories.Games =>
                    GamingIcon,
                ApplicationCategories.Development
                    or ApplicationCategories.Work =>
                    WorkIcon,
                ApplicationCategories.Multimedia
                    or ApplicationCategories.Design
                    or ApplicationCategories.Education =>
                    RelaxIcon,
                ApplicationCategories.System
                    or ApplicationCategories.Security =>
                    SettingsIcon,
                _ => AppsIcon
            };

        private static ModeIconOption CreateOption(
            string name,
            string path) =>
            new()
            {
                Name = name,
                Path = path,
                SourceText = "Assets"
            };
    }
}

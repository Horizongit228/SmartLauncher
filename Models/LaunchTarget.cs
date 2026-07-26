using System;
using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public enum LaunchTargetType
    {
        Application,
        Website,
        File,
        Folder,
        Steam,
        Command,
        Project
    }

    public class LaunchTarget
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } = string.Empty;

        public LaunchTargetType Type { get; set; }

        public string Value { get; set; } = string.Empty;

        public string ApplicationId { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public List<string> ProjectFiles { get; set; } = new();

        public bool OpenProjectFolder { get; set; }

        public List<ProjectFileSet> ProjectFileSets { get; set; } = new();

        public bool IsTrusted { get; set; } = true;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name) ? Value : Name;

        public string ValueSummary =>
            Type == LaunchTargetType.Project
                ? $"{Value}  •  {ProjectFileSets.Sum(set => set.Files.Count)} файлов"
                : Value;

        public string TypeText => Type switch
        {
            LaunchTargetType.Application => "Программа",
            LaunchTargetType.Website => "Сайт",
            LaunchTargetType.File => "Файл",
            LaunchTargetType.Folder => "Папка",
            LaunchTargetType.Steam => "Steam",
            LaunchTargetType.Command => "Команда",
            LaunchTargetType.Project => "Проект",
            _ => Type.ToString()
        };

        public LaunchTarget Clone()
        {
            return new LaunchTarget
            {
                Id = Id,
                Name = Name,
                Type = Type,
                Value = Value,
                ApplicationId = ApplicationId,
                IsEnabled = IsEnabled,
                ProjectFiles = new List<string>(ProjectFiles),
                OpenProjectFolder = OpenProjectFolder,
                ProjectFileSets =
                    ProjectFileSets.ConvertAll(
                        set => set.Clone()),
                IsTrusted = IsTrusted
            };
        }
    }
}

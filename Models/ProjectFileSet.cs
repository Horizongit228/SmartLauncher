using System;
using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public class ProjectFileSet
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } =
            "Основной набор";

        public bool IsEnabled { get; set; } = true;

        public List<string> Files { get; set; } = new();

        public ProjectFileSet Clone()
        {
            return new ProjectFileSet
            {
                Id = Id,
                Name = Name,
                IsEnabled = IsEnabled,
                Files = new List<string>(Files)
            };
        }
    }
}

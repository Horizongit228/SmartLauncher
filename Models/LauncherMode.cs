using System;
using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public class LauncherMode
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string AccentColor { get; set; } = "#2F6DF4";

        public List<LaunchTarget> Targets { get; set; } = new();

        public LauncherMode Clone()
        {
            return new LauncherMode
            {
                Id = Id,
                Name = Name,
                Icon = Icon,
                Description = Description,
                AccentColor = AccentColor,
                Targets = Targets.ConvertAll(target => target.Clone())
            };
        }
    }
}

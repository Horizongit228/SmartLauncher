using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public class LauncherData
    {
        public int Version { get; set; } = 3;

        public List<LauncherMode> Modes { get; set; } = new();
    }
}

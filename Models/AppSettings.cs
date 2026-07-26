using System.Text.Json.Serialization;

namespace SmartLauncher.UI.Models
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Dark;

        public bool StartWithWindows { get; set; }

        public bool CloseToTray { get; set; } = true;

        [JsonPropertyName("MinimizeToTray")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyMinimizeToTray { get; set; }

        public int LaunchDelayMilliseconds { get; set; } = 700;

        public bool IsSidebarCollapsed { get; set; }

        public bool CheckUpdatesAutomatically { get; set; } = true;

        public string UpdateManifestUrl { get; set; } = string.Empty;

        public double WindowTransparency { get; set; } = 0.94;
    }
}

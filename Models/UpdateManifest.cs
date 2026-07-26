namespace SmartLauncher.UI.Models
{
    public sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;

        public string InstallerUrl { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;

        public string ReleaseNotes { get; set; } = string.Empty;

        public DateTime PublishedAtUtc { get; set; }
    }

    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; init; }

        public required UpdateManifest Manifest { get; init; }
    }
}

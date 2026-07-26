using System;
using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public class AppCatalog
    {
        public int SchemaVersion { get; set; }

        public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

        public List<InstalledApplication> Applications { get; set; }
            = new List<InstalledApplication>();
    }
}

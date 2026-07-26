using System.Collections.Generic;

namespace SmartLauncher.UI.Models
{
    public class ModeLaunchResult
    {
        public int LaunchedCount { get; set; }

        public int SkippedCount { get; set; }

        public List<string> Errors { get; set; } = new();

        public bool IsSuccessful => Errors.Count == 0;
    }
}

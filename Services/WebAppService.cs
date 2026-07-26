using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SmartLauncher.UI.Services
{
    public static class WebAppService
    {
        public static string FindShortcut(
            string applicationName)
        {
            var startMenuRoots =
                new List<string>
                {
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),
                        "Microsoft",
                        "Windows",
                        "Start Menu",
                        "Programs"),

                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft",
                        "Windows",
                        "Start Menu",
                        "Programs")
                };

            foreach (string root in startMenuRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    string? shortcut =
                        Directory.EnumerateFiles(
                                root,
                                applicationName + ".lnk",
                                SearchOption.AllDirectories)
                            .OrderBy(path =>
                                path.Contains(
                                    $"{Path.DirectorySeparatorChar}Startup{Path.DirectorySeparatorChar}",
                                    StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(
                            shortcut))
                    {
                        return shortcut;
                    }
                }
                catch
                {
                    // Недоступную папку меню Пуск пропускаем.
                }
            }

            return string.Empty;
        }
    }
}

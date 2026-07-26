using SmartLauncher.UI.Models;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SmartLauncher.UI.Services
{
    public class IconExtractionService
    {
        private readonly string _iconDirectory;

        public IconExtractionService()
        {
            _iconDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SmartLauncher",
                "Icons");
        }

        public void PopulateIcons(AppCatalog catalog)
        {
            Directory.CreateDirectory(_iconDirectory);

            foreach (InstalledApplication application
                     in catalog.Applications)
            {
                if (!application.IsFound)
                {
                    if (!File.Exists(application.IconPath))
                    {
                        application.IconPath = string.Empty;
                    }

                    continue;
                }

                string previousIconPath =
                    application.IconPath;

                string iconSource =
                    ResolveIconSource(application);

                string extractedIconPath =
                    ExtractIcon(
                        iconSource,
                        application.Id);

                if (!string.IsNullOrWhiteSpace(
                        extractedIconPath))
                {
                    application.IconPath =
                        extractedIconPath;
                }
                else if (File.Exists(previousIconPath))
                {
                    application.IconPath =
                        previousIconPath;
                }
            }
        }

        private static string ResolveIconSource(
            InstalledApplication application)
        {
            if (File.Exists(application.ExecutablePath))
            {
                return application.ExecutablePath;
            }

            if (File.Exists(application.IconPath))
            {
                return application.IconPath;
            }

            if (File.Exists(application.EffectiveLaunchValue))
            {
                return application.EffectiveLaunchValue;
            }

            return string.Empty;
        }

        public string ExtractIcon(
            string executablePath,
            string cacheKey)
        {
            Directory.CreateDirectory(_iconDirectory);

            string safeKey = string.Concat(
                cacheKey.Where(character =>
                    char.IsLetterOrDigit(character)
                    || character is '-' or '_'));

            if (string.IsNullOrWhiteSpace(safeKey))
            {
                safeKey = Guid.NewGuid().ToString("N");
            }

            try
            {
                long executableStamp =
                    File.GetLastWriteTimeUtc(
                        executablePath)
                        .Ticks;

                string iconPath = Path.Combine(
                    _iconDirectory,
                    safeKey
                    + "-"
                    + executableStamp
                    + ".png");

                if (File.Exists(iconPath))
                {
                    return iconPath;
                }

                using Icon? icon =
                    Icon.ExtractAssociatedIcon(executablePath);

                if (icon == null)
                {
                    return string.Empty;
                }

                using Bitmap bitmap = icon.ToBitmap();
                bitmap.Save(iconPath, ImageFormat.Png);
                return iconPath;
            }
            catch
            {
                string? cachedIcon =
                    Directory.EnumerateFiles(
                            _iconDirectory,
                            safeKey + "*.png")
                        .OrderByDescending(
                            File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                return cachedIcon ?? string.Empty;
            }
        }
    }
}

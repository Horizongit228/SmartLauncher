using SmartLauncher.UI.Models;

namespace SmartLauncher.UI.Services
{
    [Obsolete(
        "Иконки Smart Launcher загружаются только из Assets.")]
    public sealed class IconExtractionService
    {
        private readonly AssetIconService
            _assetIcons = new();

        public void PopulateIcons(
            AppCatalog catalog) =>
            _assetIcons.PopulateIcons(catalog);

        public string ExtractIcon(
            string executablePath,
            string cacheKey) =>
            AssetIconService.AppsIcon;
    }
}

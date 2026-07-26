using SmartLauncher.UI.Infrastructure;
using SmartLauncher.UI.Models;
using System.Collections.ObjectModel;

namespace SmartLauncher.UI.ViewModels
{
    public sealed class MainViewModel :
        ObservableObject
    {
        private readonly List<InstalledApplication>
            _allApplications = new();
        private string _catalogSearchText = string.Empty;
        private string _selectedCatalogCategory =
            "Разработка";

        public MainViewModel()
        {
            foreach (string category
                     in new[]
                     {
                         "Разработка",
                         "Игры",
                         "Браузеры",
                         "Общение",
                         "Мультимедиа",
                         "Работа",
                         "Другое"
                     })
            {
                CatalogCategories.Add(category);
            }
        }

        public ObservableCollection<LauncherMode>
            Modes { get; } = new();

        public ObservableCollection<InstalledApplication>
            CatalogItems { get; } = new();

        public ObservableCollection<InstalledApplication>
            ApplicationOptions { get; } = new();

        public ObservableCollection<string>
            CatalogCategories { get; } = new();

        public ModeEditorViewModel ModeEditor { get; } =
            new();

        public string CatalogSearchText
        {
            get => _catalogSearchText;
            set
            {
                if (SetProperty(
                        ref _catalogSearchText,
                        value))
                {
                    ApplyCatalogFilter();
                }
            }
        }

        public string SelectedCatalogCategory
        {
            get => _selectedCatalogCategory;
            set
            {
                if (SetProperty(
                        ref _selectedCatalogCategory,
                        value))
                {
                    ApplyCatalogFilter();
                }
            }
        }

        public void SetModes(
            IEnumerable<LauncherMode> modes)
        {
            Modes.Clear();
            foreach (LauncherMode mode in modes)
            {
                Modes.Add(mode);
            }
        }

        public void SetApplications(
            IEnumerable<InstalledApplication> applications)
        {
            _allApplications.Clear();
            _allApplications.AddRange(applications);

            ApplicationOptions.Clear();
            foreach (InstalledApplication application
                     in _allApplications
                         .Where(application =>
                             application.IsFound)
                         .OrderBy(application =>
                             application.Category)
                         .ThenBy(application =>
                             application.Name))
            {
                ApplicationOptions.Add(application);
            }

            ApplyCatalogFilter();
        }

        private void ApplyCatalogFilter()
        {
            IEnumerable<InstalledApplication> filtered =
                _allApplications;

            string query =
                CatalogSearchText.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(application =>
                    application.Name.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase)
                    || application.Category.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase)
                    || application.PathText.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(
                    SelectedCatalogCategory))
            {
                filtered = filtered.Where(application =>
                    string.Equals(
                        application.Category,
                        SelectedCatalogCategory,
                        StringComparison.CurrentCultureIgnoreCase));
            }

            CatalogItems.Clear();
            foreach (InstalledApplication application
                     in filtered
                         .OrderByDescending(application =>
                             application.IsFound)
                         .ThenBy(application =>
                             application.Name))
            {
                CatalogItems.Add(application);
            }
        }
    }
}

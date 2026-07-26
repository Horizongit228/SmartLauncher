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
        private string _applicationSearchText =
            string.Empty;
        private string _catalogSearchText = string.Empty;
        private string _selectedCatalogCategory =
            "Разработка";

        public MainViewModel()
        {
            foreach (string category
                     in ApplicationCategories.All)
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

        public string ApplicationSearchText
        {
            get => _applicationSearchText;
            set
            {
                if (SetProperty(
                        ref _applicationSearchText,
                        value))
                {
                    ApplyApplicationFilter();
                }
            }
        }

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

            ApplyApplicationFilter();
            ApplyCatalogFilter();
        }

        private void ApplyApplicationFilter()
        {
            string query =
                ApplicationSearchText.Trim();

            IEnumerable<InstalledApplication> filtered =
                _allApplications.Where(application =>
                    application.IsFound);

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

            ApplicationOptions.Clear();
            foreach (InstalledApplication application
                     in filtered
                         .OrderByDescending(application =>
                             application.Name.StartsWith(
                                 query,
                                 StringComparison
                                     .CurrentCultureIgnoreCase))
                         .ThenBy(application =>
                             application.Category)
                         .ThenBy(application =>
                             application.Name))
            {
                ApplicationOptions.Add(application);
            }
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

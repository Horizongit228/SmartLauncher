using SmartLauncher.UI.Infrastructure;
using SmartLauncher.UI.Models;
using System.Collections.ObjectModel;

namespace SmartLauncher.UI.ViewModels
{
    public sealed class ModeEditorViewModel :
        ObservableObject
    {
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _accentColor = "#2F6DF4";
        private ModeIconOption? _selectedIcon;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(
                ref _description,
                value);
        }

        public string AccentColor
        {
            get => _accentColor;
            set => SetProperty(
                ref _accentColor,
                value);
        }

        public ModeIconOption? SelectedIcon
        {
            get => _selectedIcon;
            set => SetProperty(
                ref _selectedIcon,
                value);
        }

        public ObservableCollection<ModeIconOption>
            IconOptions { get; } = new();

        public void SetIconOptions(
            IEnumerable<ModeIconOption> options)
        {
            IconOptions.Clear();
            foreach (ModeIconOption option in options)
            {
                IconOptions.Add(option);
            }
        }
    }
}

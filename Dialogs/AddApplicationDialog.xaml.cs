using Microsoft.Win32;
using SmartLauncher.UI.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SmartLauncher.UI.Dialogs
{
    public partial class AddApplicationDialog : Window
    {
        private bool _nameWasSuggested;

        public AddApplicationDialog()
        {
            InitializeComponent();
            CategoryBox.ItemsSource =
                ApplicationCategories.All;
            CategoryBox.SelectedItem =
                ApplicationCategories.Other;
        }

        public string ApplicationName => NameBox.Text.Trim();

        public string ExecutablePath => PathBox.Text.Trim();

        public string Category =>
            CategoryBox.SelectedItem as string
            ?? ApplicationCategories.Other;

        private void BrowseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите приложение",
                Filter = "Приложения Windows|*.exe"
            };

            if (dialog.ShowDialog(this) == true)
            {
                PathBox.Text = dialog.FileName;
            }
        }

        private void PathBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            string path = PathBox.Text.Trim();
            if (!File.Exists(path))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(NameBox.Text)
                || _nameWasSuggested)
            {
                NameBox.Text =
                    Path.GetFileNameWithoutExtension(path);
                _nameWasSuggested = true;
            }
        }

        private void AddButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApplicationName))
            {
                ValidationText.Text =
                    "Введите понятное название приложения.";
                return;
            }

            if (!File.Exists(ExecutablePath)
                || !string.Equals(
                    Path.GetExtension(ExecutablePath),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidationText.Text =
                    "Выберите существующий исполняемый файл .exe.";
                return;
            }

            DialogResult = true;
        }
    }
}

using System.Windows;

namespace SmartLauncher.UI.Dialogs
{
    public partial class TextInputDialog : Window
    {
        public TextInputDialog(
            string title,
            string prompt,
            string initialValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = initialValue;
            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        public string Value => ValueBox.Text.Trim();

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                ValidationText.Text =
                    "Название не может быть пустым.";
                return;
            }

            DialogResult = true;
        }
    }
}

using System.Windows;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class SettingsExportDialog : Window
    {
        public bool IncludeModelList => IncludeJobsCheckBox.IsChecked ?? true;

        public SettingsExportDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

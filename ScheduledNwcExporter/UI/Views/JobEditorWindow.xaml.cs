using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class JobEditorWindow : Window
    {
        private readonly JobEditorViewModel _viewModel;
        public ModelExportJob? Job => _viewModel.Job;

        public JobEditorWindow(ModelExportJob? job)
        {
            InitializeComponent();
            _viewModel = new JobEditorViewModel(job);
            DataContext = _viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Validate())
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Revit Models (*.rvt)|*.rvt|All Files (*.*)|*.*",
                Title = "Select Revit Source Model"
            };
            if (dlg.ShowDialog() == true)
            {
                _viewModel.Job.SourceModelPath = dlg.FileName;
            }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Output Folder for NWC Files"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _viewModel.Job.OutputDirectory = dlg.SelectedPath;
            }
        }
    }

    public class ValidationColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string message = value as string ?? string.Empty;
            if (message.StartsWith("✓", StringComparison.Ordinal))
            {
                return new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(192, 57, 43)); // Red
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

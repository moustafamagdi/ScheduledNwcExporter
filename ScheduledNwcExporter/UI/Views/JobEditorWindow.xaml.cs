using System.Windows;
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
    }
}

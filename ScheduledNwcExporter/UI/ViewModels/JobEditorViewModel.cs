using System;
using System.IO;
using System.Windows.Input;
using ScheduledNwcExporter.Configuration;

using ScheduledNwcExporter.UI;

namespace ScheduledNwcExporter.UI.ViewModels
{
    public class JobEditorViewModel : BindableBase
    {
        public ModelExportJob Job { get; set; }

        private string _validationMessage = "Ready";
        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseCloudCommand { get; }
        public ICommand BrowseOutputCommand { get; }

        public JobEditorViewModel(ModelExportJob? job)
        {
            Job = job != null ? new ModelExportJob
            {
                Id = job.Id,
                SourceModelPath = job.SourceModelPath,
                OutputDirectory = job.OutputDirectory,
                OutputFileNameTemplate = job.OutputFileNameTemplate,
                IsEnabled = job.IsEnabled,
                RetryCount = job.RetryCount
            } : new ModelExportJob();

            BrowseSourceCommand = new RelayCommand(_ => BrowseSourceFile());
            BrowseCloudCommand = new RelayCommand(_ => BrowseCloudFile());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutputFolder());
            Validate();
        }

        private void BrowseCloudFile()
        {
            string token = Core.CloudAuthenticationService.GetAccessToken();
            if (string.IsNullOrEmpty(token))
            {
                System.Windows.MessageBox.Show("Unable to retrieve Revit session token. Please ensure you are logged into Autodesk in Revit.", "Hatco Cloud Explorer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            var cloudVm = new CloudBrowserViewModel(token, new Logging.FileLogger()); // In real app, pass the logger instance
            var cloudWindow = new Views.CloudBrowserWindow(cloudVm);
            if (cloudWindow.ShowDialog() == true && cloudWindow.SelectedNode != null)
            {
                // Format: acc://ProjectName|ProjectId/ModelName|VersionId.rvt
                var node = cloudWindow.SelectedNode;
                string path = $"acc://{node.Name}|{node.VersionId}";
                if (!path.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    path += ".rvt";
                }
                Job.SourceModelPath = path; 
                OnPropertyChanged(nameof(Job));
                Validate();
            }
        }

        private void BrowseSourceFile()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Revit Models (*.rvt)|*.rvt|All Files (*.*)|*.*",
                Title = "Select Source Revit Model"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Job.SourceModelPath = openFileDialog.FileName;
                if (string.IsNullOrEmpty(Job.OutputDirectory))
                {
                    Job.OutputDirectory = Path.GetDirectoryName(openFileDialog.FileName) ?? string.Empty;
                }
                OnPropertyChanged(nameof(Job));
                Validate();
            }
        }

        private void BrowseOutputFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Output Directory for NWC Files"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Job.OutputDirectory = dlg.SelectedPath;
                OnPropertyChanged(nameof(Job));
                Validate();
            }
        }

        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(Job.SourceModelPath))
            {
                ValidationMessage = "✕ Source model path is required.";
                return false;
            }

            bool isCloud = Job.SourceModelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);

            if (!isCloud && !File.Exists(Job.SourceModelPath))
            {
                ValidationMessage = "✕ Source model file not found.";
                return false;
            }
            
            // For cloud paths, we check if it contains .rvt since it might be followed by URN
            if (!Job.SourceModelPath.ToLower().Contains(".rvt"))
            {
                ValidationMessage = "✕ Source file must be a .rvt Revit model.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(Job.OutputDirectory))
            {
                ValidationMessage = "✕ Output directory is required.";
                return false;
            }
            ValidationMessage = "✓ Job settings are valid.";
            return true;
        }
    }
}

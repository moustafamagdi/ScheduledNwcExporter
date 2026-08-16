using System;
using System.IO;
using System.Windows.Input;
using ScheduledNwcExporter.Configuration;

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
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutputFolder());
            Validate();
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
            if (string.IsNullOrWhiteSpace(Job.SourceModelPath) || !File.Exists(Job.SourceModelPath))
            {
                ValidationMessage = "✕ Source model file not found.";
                return false;
            }
            if (!Job.SourceModelPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
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

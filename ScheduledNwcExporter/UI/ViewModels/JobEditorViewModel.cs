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

        private bool _useCustomSettings;
        public bool UseCustomSettings
        {
            get => _useCustomSettings;
            set
            {
                if (SetProperty(ref _useCustomSettings, value))
                {
                    if (value && Job.CustomExportSettings == null)
                    {
                        Job.CustomExportSettings = new ExportSettings();
                    }
                    else if (!value)
                    {
                        Job.CustomExportSettings = null;
                    }
                    OnPropertyChanged(nameof(Job));
                }
            }
        }

        public JobEditorViewModel(ModelExportJob? job)
        {
            Job = job != null ? new ModelExportJob
            {
                Id = job.Id,
                SourceModelPath = job.SourceModelPath,
                OutputDirectory = job.OutputDirectory,
                OutputFileNameTemplate = job.OutputFileNameTemplate,
                IsEnabled = job.IsEnabled,
                RetryCount = job.RetryCount,
                RetryDelaySeconds = job.RetryDelaySeconds,
                CustomExportSettings = job.CustomExportSettings != null ? new ExportSettings
                {
                    ExportLinks = job.CustomExportSettings.ExportLinks,
                    ExportScope = job.CustomExportSettings.ExportScope,
                    Coordinates = job.CustomExportSettings.Coordinates,
                    OverwritePolicy = job.CustomExportSettings.OverwritePolicy,
                    ExportElementIds = job.CustomExportSettings.ExportElementIds,
                    ExportRoomGeometry = job.CustomExportSettings.ExportRoomGeometry,
                    UseTemporaryCopyWithoutRevitLinks = job.CustomExportSettings.UseTemporaryCopyWithoutRevitLinks,
                    DivideFileIntoLevels = job.CustomExportSettings.DivideFileIntoLevels,
                    ExportParts = job.CustomExportSettings.ExportParts,
                    FacetingFactor = job.CustomExportSettings.FacetingFactor,
                    ParameterExportMode = job.CustomExportSettings.ParameterExportMode,
                    ExportUrls = job.CustomExportSettings.ExportUrls,
                    ExportRoomAsAttribute = job.CustomExportSettings.ExportRoomAsAttribute,
                    ConvertLights = job.CustomExportSettings.ConvertLights,
                    FindMissingMaterials = job.CustomExportSettings.FindMissingMaterials
                } : null
            } : new ModelExportJob();

            _useCustomSettings = Job.CustomExportSettings != null;

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
                // Format: acc://ModelName.rvt|Region|ProjectGUID|ModelGUID
                var node = cloudWindow.SelectedNode;
                string modelName = node.Name;
                if (!modelName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                {
                    modelName += ".rvt";
                }

                // We need at least ProjectGUID and ModelGUID for proper opening
                if (string.IsNullOrEmpty(node.RevitProjectGuid) || string.IsNullOrEmpty(node.RevitModelGuid))
                {
                    System.Windows.MessageBox.Show("This file is not initiated as a Revit Cloud Model and cannot be opened directly. Please ensure it is a workshared cloud model.", "Incompatible Model", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                Job.SourceModelPath = $"acc://{modelName}|{node.Region}|{node.RevitProjectGuid}|{node.RevitModelGuid}"; 
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
            if (Job.RetryCount < 0)
            {
                ValidationMessage = "✕ Retry count cannot be negative.";
                return false;
            }
            if (Job.RetryDelaySeconds < 0)
            {
                ValidationMessage = "✕ Retry delay cannot be negative.";
                return false;
            }
            ValidationMessage = "✓ Job settings are valid.";
            return true;
        }
    }
}

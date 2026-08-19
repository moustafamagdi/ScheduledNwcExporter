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
                CloudDisplayPath = job.CloudDisplayPath,
                CloudDataProjectId = job.CloudDataProjectId,
                CloudItemId = job.CloudItemId,
                CloudVersionId = job.CloudVersionId,
                LastSourceModifiedUtc = job.LastSourceModifiedUtc,
                LastMetadataRefreshUtc = job.LastMetadataRefreshUtc,
                SourceMetadataError = job.SourceMetadataError,
                OutputDirectory = job.OutputDirectory,
                OutputFileNameTemplate = job.OutputFileNameTemplate,
                IsEnabled = job.IsEnabled,
                RetryCount = job.RetryCount,
                RetryDelaySeconds = job.RetryDelaySeconds,
                CustomExportSettings = job.CustomExportSettings != null ? new ExportSettings
                {
                    ConvertElementProperties = job.CustomExportSettings.ConvertElementProperties,
                    DivideFileIntoLevels = job.CustomExportSettings.DivideFileIntoLevels,
                    ExportElementIds = job.CustomExportSettings.ExportElementIds,
                    ExportParts = job.CustomExportSettings.ExportParts,
                    ExportInternalCoordinates = job.CustomExportSettings.ExportInternalCoordinates,
                    ConvertLights = job.CustomExportSettings.ConvertLights,
                    ExportRoomAsAttribute = job.CustomExportSettings.ExportRoomAsAttribute,
                    ExportRoomGeometry = job.CustomExportSettings.ExportRoomGeometry,
                    ExportUrls = job.CustomExportSettings.ExportUrls,
                    FindMissingMaterials = job.CustomExportSettings.FindMissingMaterials,
                    ExportAllParameters = job.CustomExportSettings.ExportAllParameters,
                    ExportElementParameters = job.CustomExportSettings.ExportElementParameters,
                    FacetingFactor = job.CustomExportSettings.FacetingFactor,
                    OverwritePolicy = job.CustomExportSettings.OverwritePolicy,
                    UseTemporaryCopyWithoutRevitLinks = job.CustomExportSettings.UseTemporaryCopyWithoutRevitLinks
                } : null
            } : new ModelExportJob();

            _useCustomSettings = Job.CustomExportSettings != null;

            BrowseSourceCommand = new RelayCommand(_ => BrowseSourceFile());
            BrowseCloudCommand = new RelayCommand(_ => BrowseCloudFile());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutputFolder());
            
            Job.PropertyChanged += (s, e) => Validate();
            if (Job.CustomExportSettings != null)
            {
                Job.CustomExportSettings.PropertyChanged += (s, e) => Validate();
            }

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
                string cloudDisplayPath = node.GetReadableCloudPath();
                Job.CloudDisplayPath = cloudDisplayPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                    ? cloudDisplayPath
                    : cloudDisplayPath + ".rvt";
                Job.CloudOpenAccessDenied = false;
                Job.CloudOpenAccessDeniedAt = null;
                Job.CloudDataProjectId = node.ProjectId ?? string.Empty;
                Job.CloudItemId = node.Id ?? string.Empty;
                Job.CloudVersionId = node.VersionId ?? string.Empty;
                Job.LastSourceModifiedUtc = node.LastModifiedUtc;
                Job.LastMetadataRefreshUtc = node.LastModifiedUtc.HasValue ? DateTime.UtcNow : null;
                Job.SourceMetadataError = node.LastModifiedUtc.HasValue ? string.Empty : "ACC modification date was not returned for this item. Use Refresh Dates after adding it.";
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
                Job.CloudDisplayPath = string.Empty;
                Job.CloudOpenAccessDenied = false;
                Job.CloudOpenAccessDeniedAt = null;
                Job.CloudDataProjectId = string.Empty;
                Job.CloudItemId = string.Empty;
                Job.CloudVersionId = string.Empty;
                Job.LastSourceModifiedUtc = File.GetLastWriteTimeUtc(openFileDialog.FileName);
                Job.LastMetadataRefreshUtc = DateTime.UtcNow;
                Job.SourceMetadataError = string.Empty;
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

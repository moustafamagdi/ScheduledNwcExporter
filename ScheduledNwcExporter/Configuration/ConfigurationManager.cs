using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Configuration
{
    public class ModelExportJob : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        private string _sourceModelPath = string.Empty;
        public string SourceModelPath
        {
            get => _sourceModelPath;
            set { _sourceModelPath = value; OnPropertyChanged(); }
        }

        private string _outputDirectory = string.Empty;
        public string OutputDirectory
        {
            get => _outputDirectory;
            set { _outputDirectory = value; OnPropertyChanged(); }
        }

        private string _outputFileNameTemplate = "{ModelName}_{Date}_{Time}.nwc";
        public string OutputFileNameTemplate
        {
            get => _outputFileNameTemplate;
            set { _outputFileNameTemplate = value; OnPropertyChanged(); }
        }

        private int _retryCount = 1;
        public int RetryCount
        {
            get => _retryCount;
            set { _retryCount = value; OnPropertyChanged(); }
        }

        private string _lastStatus = "Ready";
        public string LastStatus
        {
            get => _lastStatus;
            set { _lastStatus = value; OnPropertyChanged(); }
        }

        private string _lastRun = "Never";
        public string LastRun
        {
            get => _lastRun;
            set { _lastRun = value; OnPropertyChanged(); }
        }

        private string _lastDuration = "-";
        public string LastDuration
        {
            get => _lastDuration;
            set { _lastDuration = value; OnPropertyChanged(); }
        }

        private string _lastError = string.Empty;
        public string LastError
        {
            get => _lastError;
            set { _lastError = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ExportSettings
    {
        public bool ExportLinks { get; set; } = false;
        public string ExportScope { get; set; } = "View";
        public string Coordinates { get; set; } = "Shared";
        public string OverwritePolicy { get; set; } = "Overwrite"; // Overwrite, Skip, TimestampedCopy
        public bool ExportElementIds { get; set; } = true;
        public bool ExportRoomGeometry { get; set; } = true;

        /// <summary>
        /// When enabled, the exporter creates a local temporary RVT copy and marks top-level Revit links
        /// as unloaded in its TransmissionData before the copy is opened for export.
        /// </summary>
        public bool UseTemporaryCopyWithoutRevitLinks { get; set; } = true;
    }

    public class SchedulerSettings
    {
        public bool IsSchedulerEnabled { get; set; } = false;
        public int ScheduledHour { get; set; } = 19;
        public int ScheduledMinute { get; set; } = 0;
    }

    public class AppSettings
    {
        public bool DebugMode { get; set; } = false;
        public ExportSettings Export { get; set; } = new ExportSettings();
        public SchedulerSettings Scheduler { get; set; } = new SchedulerSettings();
        public List<ModelExportJob> Jobs { get; set; } = new List<ModelExportJob>();
    }

    public class ConfigurationManager
    {
        private readonly string _configDirectory;
        private readonly string _configFilePath;
        private readonly FileLogger _logger;

        public AppSettings CurrentSettings { get; private set; }

        public ConfigurationManager()
        {
            _logger = new FileLogger();
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MoustafaMagdi",
                "ScheduledNwcExporter");
            _configFilePath = Path.Combine(_configDirectory, "config.json");
            CurrentSettings = LoadConfiguration();
        }

        public AppSettings LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        _logger.Debug("Config", $"Loaded configuration from {_configFilePath}");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Config", $"Failed to load configuration: {ex.Message}", string.Empty, string.Empty, ex);
            }

            var defaultSettings = new AppSettings();
            // Add a sample job if none exist
            defaultSettings.Jobs.Add(new ModelExportJob
            {
                SourceModelPath = @"C:\Projects\SampleModel.rvt",
                OutputDirectory = @"C:\ExportedNwc",
                OutputFileNameTemplate = "{ModelName}_{Date}.nwc",
                IsEnabled = false
            });
            return defaultSettings;
        }

        public void SaveConfiguration()
        {
            try
            {
                Directory.CreateDirectory(_configDirectory);
                string json = JsonConvert.SerializeObject(CurrentSettings, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                _logger.Debug("Config", $"Saved configuration to {_configFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error("Config", $"Failed to save configuration: {ex.Message}", string.Empty, string.Empty, ex);
            }
        }
    }
}

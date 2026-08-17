using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Configuration
{
    public enum JobStatus
    {
        Ready,
        Processing,
        Success,
        Failed,
        Skipped,
        Cancelled,
        Retrying
    }

    public class RunResult
    {
        public DateTime Timestamp { get; set; }
        public JobStatus Status { get; set; }
        public string Duration { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class ModelExportJob : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

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
            set { _outputFileNameTemplate = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResolvedOutputFilename)); }
        }

        [JsonIgnore]
        public bool IsCloud => _sourceModelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string ResolvedOutputFilename
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_sourceModelPath)) return "Model.nwc";
                    
                    string modelName = "Model";
                    string modelFileName = "Model.rvt";

                    if (_sourceModelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase))
                    {
                        // Format: acc://ModelName.rvt|Region|ProjectGUID|ModelGUID
                        string temp = _sourceModelPath.Substring(6); // Remove acc://
                        string[] parts = temp.Split('|');
                        if (parts.Length > 0)
                        {
                            modelFileName = parts[0];
                            modelName = Path.GetFileNameWithoutExtension(modelFileName);
                        }
                    }
                    else
                    {
                        modelFileName = Path.GetFileName(_sourceModelPath);
                        modelName = Path.GetFileNameWithoutExtension(modelFileName);
                    }

                    DateTime now = DateTime.Now;
                    string resolved = _outputFileNameTemplate
                        .Replace("{ModelName}", modelName)
                        .Replace("{ModelFileName}", modelFileName)
                        .Replace("{Date}", now.ToString("yyyy-MM-dd"))
                        .Replace("{Time}", now.ToString("HH-mm-ss"))
                        .Replace("{Year}", now.ToString("yyyy"))
                        .Replace("{Month}", now.ToString("MM"))
                        .Replace("{Day}", now.ToString("dd"))
                        .Replace("{Hour}", now.ToString("HH"))
                        .Replace("{Minute}", now.ToString("mm"));

                    foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                    {
                        resolved = resolved.Replace(invalidCharacter, '_');
                    }

                    return resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ? resolved : resolved + ".nwc";
                }
                catch
                {
                    return "Model.nwc";
                }
            }
        }

        private int _retryCount = 1;
        public int RetryCount
        {
            get => _retryCount;
            set { _retryCount = value; OnPropertyChanged(); }
        }

        private int _retryDelaySeconds = 30;
        public int RetryDelaySeconds
        {
            get => _retryDelaySeconds;
            set { _retryDelaySeconds = value; OnPropertyChanged(); }
        }

        private ExportSettings? _customExportSettings;
        public ExportSettings? CustomExportSettings
        {
            get => _customExportSettings;
            set { _customExportSettings = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCustomSettings)); }
        }

        [JsonIgnore]
        public bool HasCustomSettings => _customExportSettings != null;

        private JobStatus _status = JobStatus.Ready;
        public JobStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        [JsonIgnore]
        public string StatusText => Status.ToString();

        private int _progressPercentage;
        [JsonIgnore]
        public int ProgressPercentage
        {
            get => _progressPercentage;
            set { _progressPercentage = value; OnPropertyChanged(); }
        }

        private string _currentStage = string.Empty;
        [JsonIgnore]
        public string CurrentStage
        {
            get => _currentStage;
            set { _currentStage = value; OnPropertyChanged(); }
        }

        private DateTime? _lastRun;
        public DateTime? LastRun
        {
            get => _lastRun;
            set { _lastRun = value; OnPropertyChanged(); }
        }

        private string _lastError = string.Empty;
        public string LastError
        {
            get => _lastError;
            set { _lastError = value; OnPropertyChanged(); }
        }

        private List<RunResult> _runHistory = new List<RunResult>();
        public List<RunResult> RunHistory
        {
            get => _runHistory;
            set { _runHistory = value; OnPropertyChanged(); }
        }

        public void AddRunResult(RunResult result)
        {
            if (_runHistory == null) _runHistory = new List<RunResult>();
            _runHistory.Insert(0, result);
            if (_runHistory.Count > 10)
            {
                _runHistory.RemoveAt(_runHistory.Count - 1);
            }
            OnPropertyChanged(nameof(RunHistory));
        }

        // Compatibility property for older JSON configs
        [JsonProperty("LastStatus")]
        private string LastStatusString
        {
            set
            {
                if (Enum.TryParse(value, true, out JobStatus result))
                    Status = result;
                else
                    Status = JobStatus.Ready;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ExportSettings : INotifyPropertyChanged
    {
        private bool _exportLinks = false;
        public bool ExportLinks { get => _exportLinks; set { _exportLinks = value; OnPropertyChanged(); } }

        private string _exportScope = "View";
        public string ExportScope { get => _exportScope; set { _exportScope = value; OnPropertyChanged(); } }

        private string _coordinates = "Shared";
        public string Coordinates { get => _coordinates; set { _coordinates = value; OnPropertyChanged(); } }

        private string _overwritePolicy = "Overwrite";
        public string OverwritePolicy { get => _overwritePolicy; set { _overwritePolicy = value; OnPropertyChanged(); } }

        private bool _exportElementIds = true;
        public bool ExportElementIds { get => _exportElementIds; set { _exportElementIds = value; OnPropertyChanged(); } }

        private bool _exportRoomGeometry = true;
        public bool ExportRoomGeometry { get => _exportRoomGeometry; set { _exportRoomGeometry = value; OnPropertyChanged(); } }

        private bool _useTemporaryCopyWithoutRevitLinks = true;
        public bool UseTemporaryCopyWithoutRevitLinks { get => _useTemporaryCopyWithoutRevitLinks; set { _useTemporaryCopyWithoutRevitLinks = value; OnPropertyChanged(); } }

        private bool _divideFileIntoLevels = false;
        public bool DivideFileIntoLevels { get => _divideFileIntoLevels; set { _divideFileIntoLevels = value; OnPropertyChanged(); } }

        private bool _exportParts = false;
        public bool ExportParts { get => _exportParts; set { _exportParts = value; OnPropertyChanged(); } }

        private double _facetingFactor = 1.0;
        public double FacetingFactor { get => _facetingFactor; set { _facetingFactor = value; OnPropertyChanged(); } }

        private string _parameterExportMode = "All";
        public string ParameterExportMode { get => _parameterExportMode; set { _parameterExportMode = value; OnPropertyChanged(); } }

        private bool _exportUrls = false;
        public bool ExportUrls { get => _exportUrls; set { _exportUrls = value; OnPropertyChanged(); } }

        private bool _exportRoomAsAttribute = false;
        public bool ExportRoomAsAttribute { get => _exportRoomAsAttribute; set { _exportRoomAsAttribute = value; OnPropertyChanged(); } }

        private bool _convertLights = false;
        public bool ConvertLights { get => _convertLights; set { _convertLights = value; OnPropertyChanged(); } }

        private bool _findMissingMaterials = false;
        public bool FindMissingMaterials { get => _findMissingMaterials; set { _findMissingMaterials = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ScheduleSlot : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }

        private int _hour = 19;
        public int Hour { get => _hour; set { _hour = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay)); } }

        private int _minute = 0;
        public int Minute { get => _minute; set { _minute = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay)); } }

        private List<DayOfWeek> _days = new List<DayOfWeek> 
        { 
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, 
            DayOfWeek.Thursday, DayOfWeek.Friday 
        };
        public List<DayOfWeek> Days { get => _days; set { _days = value; OnPropertyChanged(); OnPropertyChanged(nameof(DaysDisplay)); } }

        [JsonIgnore]
        public string TimeDisplay => $"{Hour:D2}:{Minute:D2}";
        
        [JsonIgnore]
        public string DaysDisplay => Days.Count == 7 ? "Daily" : string.Join(", ", Days.Select(d => d.ToString().Substring(0, 3)));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class SchedulerSettings
    {
        public bool IsSchedulerEnabled { get; set; } = false;
        public List<ScheduleSlot> Slots { get; set; } = new List<ScheduleSlot>();
        
        // Legacy support
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

        public void ExportUnifiedSettings(string filePath, bool includeModelList)
        {
            try
            {
                var package = new
                {
                    Export = CurrentSettings.Export,
                    Scheduler = CurrentSettings.Scheduler,
                    DebugMode = CurrentSettings.DebugMode,
                    Jobs = includeModelList ? CurrentSettings.Jobs : null
                };
                string json = JsonConvert.SerializeObject(package, Formatting.Indented);
                File.WriteAllText(filePath, json);
                _logger.Info("Config", $"Exported unified settings to {filePath} (IncludeModelList: {includeModelList})");
            }
            catch (Exception ex)
            {
                _logger.Error("Config", $"Failed to export unified settings: {ex.Message}", string.Empty, string.Empty, ex);
                throw;
            }
        }

        public void ImportUnifiedSettings(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("Import file not found.", filePath);

                string json = File.ReadAllText(filePath);
                var imported = JsonConvert.DeserializeObject<AppSettings>(json);
                
                if (imported == null) throw new InvalidOperationException("The imported configuration file is empty or invalid.");

                // 1. Create backup of current config
                if (File.Exists(_configFilePath))
                {
                    string backupPath = _configFilePath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(_configFilePath, backupPath, true);
                    _logger.Info("Config", $"Created backup of current configuration at {backupPath}");
                }

                // 2. Apply settings
                CurrentSettings.Export = imported.Export ?? CurrentSettings.Export;
                CurrentSettings.Scheduler = imported.Scheduler ?? CurrentSettings.Scheduler;
                CurrentSettings.DebugMode = imported.DebugMode;
                
                if (imported.Jobs != null)
                {
                    CurrentSettings.Jobs = imported.Jobs;
                }

                // 3. Save and log
                SaveConfiguration();
                _logger.Success("Config", $"Successfully imported unified settings from {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Error("Config", $"Failed to import unified settings: {ex.Message}", string.Empty, string.Empty, ex);
                throw;
            }
        }
    }
}

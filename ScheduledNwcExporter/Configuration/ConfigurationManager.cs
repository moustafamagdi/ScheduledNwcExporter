using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ScheduledNwcExporter.Configuration
{
    public class ModelExportJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SourceModelPath { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;
        public string OutputFileNameTemplate { get; set; } = "{ModelName}_{Date}.nwc";
        public bool IsEnabled { get; set; } = true;
        public int RetryCount { get; set; } = 2;
        public string LastStatus { get; set; } = "Ready";
        public string LastRun { get; set; } = "--";
        public string LastDuration { get; set; } = "--";
        public string LastError { get; set; } = string.Empty;
    }

    public class ExportSettings
    {
        public bool ExportLinks { get; set; } = false;
        public string ExportScope { get; set; } = "Model";
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
        public bool Is24HourFormat { get; set; } = true;
    }

    public class AppSettings
    {
        public SchedulerSettings Scheduler { get; set; } = new SchedulerSettings();
        public ExportSettings Export { get; set; } = new ExportSettings();
        public List<ModelExportJob> Jobs { get; set; } = new List<ModelExportJob>();
        public bool DebugMode { get; set; } = false;
    }

    public class ConfigurationManager
    {
        private readonly string _configDirectory;
        private readonly string _configFilePath;
        public AppSettings CurrentSettings { get; private set; }

        public ConfigurationManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configDirectory = Path.Combine(appData, "MoustafaMagdi", "ScheduledNwcExporter");
            Directory.CreateDirectory(_configDirectory);
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
                    if (settings != null) return settings;
                }
            }
            catch (Exception)
            {
                // Fallback to default
            }
            return new AppSettings();
        }

        public void SaveConfiguration()
        {
            try
            {
                string json = JsonConvert.SerializeObject(CurrentSettings, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to save configuration: {ex.Message}", ex);
            }
        }
    }
}

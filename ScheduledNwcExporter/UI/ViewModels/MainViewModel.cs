using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Queue;
using ScheduledNwcExporter.Scheduler;
using ScheduledNwcExporter.Revit;

namespace ScheduledNwcExporter.UI.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly ConfigurationManager _configManager;
        private readonly FileLogger _logger;
        private readonly ScheduleManager _scheduleManager;
        private JobProcessor? _jobProcessor;

        private bool _isSchedulerEnabled;
        public bool IsSchedulerEnabled
        {
            get => _isSchedulerEnabled;
            set
            {
                if (SetProperty(ref _isSchedulerEnabled, value))
                {
                    _configManager.CurrentSettings.Scheduler.IsSchedulerEnabled = value;
                    _configManager.SaveConfiguration();
                    UpdateNextRunText();
                }
            }
        }

        private string _scheduledTimeString = "19:00";
        public string ScheduledTimeString
        {
            get => _scheduledTimeString;
            set
            {
                if (SetProperty(ref _scheduledTimeString, value))
                {
                    if (TimeSpan.TryParse(value, out TimeSpan ts))
                    {
                        _configManager.CurrentSettings.Scheduler.ScheduledHour = ts.Hours;
                        _configManager.CurrentSettings.Scheduler.ScheduledMinute = ts.Minutes;
                        _configManager.SaveConfiguration();
                        UpdateNextRunText();
                    }
                }
            }
        }

        private string _nextRunText = "Today 07:00 PM";
        public string NextRunText
        {
            get => _nextRunText;
            set => SetProperty(ref _nextRunText, value);
        }

        private bool _exportLinks;
        public bool ExportLinks
        {
            get => _exportLinks;
            set
            {
                if (SetProperty(ref _exportLinks, value))
                {
                    _configManager.CurrentSettings.Export.ExportLinks = value;
                    _configManager.SaveConfiguration();
                }
            }
        }

        private string _exportScope = "Model";
        public string ExportScope
        {
            get => _exportScope;
            set
            {
                if (SetProperty(ref _exportScope, value))
                {
                    _configManager.CurrentSettings.Export.ExportScope = value;
                    _configManager.SaveConfiguration();
                }
            }
        }

        private string _coordinates = "Shared";
        public string Coordinates
        {
            get => _coordinates;
            set
            {
                if (SetProperty(ref _coordinates, value))
                {
                    _configManager.CurrentSettings.Export.Coordinates = value;
                    _configManager.SaveConfiguration();
                }
            }
        }

        private string _overwritePolicy = "Overwrite";
        public string OverwritePolicy
        {
            get => _overwritePolicy;
            set
            {
                if (SetProperty(ref _overwritePolicy, value))
                {
                    _configManager.CurrentSettings.Export.OverwritePolicy = value;
                    _configManager.SaveConfiguration();
                }
            }
        }

        private string _currentActivityModel = "None";
        public string CurrentActivityModel
        {
            get => _currentActivityModel;
            set => SetProperty(ref _currentActivityModel, value);
        }

        private string _currentActivityStage = "Idle";
        public string CurrentActivityStage
        {
            get => _currentActivityStage;
            set => SetProperty(ref _currentActivityStage, value);
        }

        private int _overallProgressPercentage = 0;
        public int OverallProgressPercentage
        {
            get => _overallProgressPercentage;
            set => SetProperty(ref _overallProgressPercentage, value);
        }

        private ModelExportJob? _selectedJob;
        public ModelExportJob? SelectedJob
        {
            get => _selectedJob;
            set => SetProperty(ref _selectedJob, value);
        }

        public ObservableCollection<ModelExportJob> Jobs { get; set; }

        public ICommand AddModelCommand { get; }
        public ICommand EditModelCommand { get; }
        public ICommand RemoveModelCommand { get; }
        public ICommand RunNowCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand TestSelectedCommand { get; }
        public ICommand ViewLogCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveConfigurationCommand { get; }

        public MainViewModel(Autodesk.Revit.ApplicationServices.Application app)
        {
            _app = app;
            _configManager = new ConfigurationManager();
            _logger = new FileLogger();
            _logger.DebugMode = _configManager.CurrentSettings.DebugMode;

            var settings = _configManager.CurrentSettings;
            _isSchedulerEnabled = settings.Scheduler.IsSchedulerEnabled;
            _scheduledTimeString = $"{settings.Scheduler.ScheduledHour:D2}:{settings.Scheduler.ScheduledMinute:D2}";
            _exportLinks = settings.Export.ExportLinks;
            _exportScope = settings.Export.ExportScope;
            _coordinates = settings.Export.Coordinates;
            _overwritePolicy = settings.Export.OverwritePolicy;

            Jobs = new ObservableCollection<ModelExportJob>(settings.Jobs);

            _scheduleManager = new ScheduleManager(settings, _logger);
            _scheduleManager.ScheduledTimeReached += (s, e) => ExecuteRunQueueAsync();
            if (_isSchedulerEnabled)
            {
                _scheduleManager.Start();
            }

            UpdateNextRunText();

            AddModelCommand = new RelayCommand(_ => AddModel());
            EditModelCommand = new RelayCommand(_ => EditModel(), _ => SelectedJob != null);
            RemoveModelCommand = new RelayCommand(_ => RemoveModel(), _ => SelectedJob != null);
            RunNowCommand = new RelayCommand(_ => ExecuteRunQueueAsync());
            PauseCommand = new RelayCommand(_ => PauseQueue());
            TestSelectedCommand = new RelayCommand(_ => TestSelectedJob(), _ => SelectedJob != null);
            ViewLogCommand = new RelayCommand(_ => ViewLogFile());
            OpenSettingsCommand = new RelayCommand(_ => OpenDiagnostics());
            SaveConfigurationCommand = new RelayCommand(_ => SaveConfig());
        }

        private void AddModel()
        {
            var dialog = new Views.JobEditorWindow(null);
            if (dialog.ShowDialog() == true && dialog.Job != null)
            {
                Jobs.Add(dialog.Job);
                _configManager.CurrentSettings.Jobs = new System.Collections.Generic.List<ModelExportJob>(Jobs);
                _configManager.SaveConfiguration();
                _logger.Info("UI", $"Added export job for model: {dialog.Job.SourceModelPath}");
            }
        }

        private void EditModel()
        {
            if (SelectedJob == null) return;
            var dialog = new Views.JobEditorWindow(SelectedJob);
            if (dialog.ShowDialog() == true)
            {
                int index = Jobs.IndexOf(SelectedJob);
                if (index >= 0)
                {
                    Jobs[index] = dialog.Job!;
                    _configManager.CurrentSettings.Jobs = new System.Collections.Generic.List<ModelExportJob>(Jobs);
                    _configManager.SaveConfiguration();
                    _logger.Info("UI", $"Updated export job: {dialog.Job!.SourceModelPath}");
                }
            }
        }

        private void RemoveModel()
        {
            if (SelectedJob == null) return;
            _logger.Info("UI", $"Removed export job: {SelectedJob.SourceModelPath}");
            Jobs.Remove(SelectedJob);
            _configManager.CurrentSettings.Jobs = new System.Collections.Generic.List<ModelExportJob>(Jobs);
            _configManager.SaveConfiguration();
        }

        private async void ExecuteRunQueueAsync()
        {
            if (_jobProcessor != null)
            {
                MessageBox.Show("An export session is already running.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verify exporter availability first
            var exporterService = new NwcExporterService(_logger);
            if (!exporterService.IsExporterAvailable())
            {
                MessageBox.Show("The compatible Navisworks NWC Exporter is not available in this Revit session. Cannot start export.", "Navisworks Exporter Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _jobProcessor = new JobProcessor(_app, _configManager.CurrentSettings, _logger);
            _jobProcessor.JobStatusUpdated += (s, status) =>
            {
                CurrentActivityStage = status;
            };
            _jobProcessor.OverallProgressUpdated += (s, progress) =>
            {
                OverallProgressPercentage = progress;
            };

            var queue = new ExportQueue(Jobs);
            var result = await Task.Run(() => _jobProcessor.ProcessQueueAsync(queue.GetActiveJobs()));

            _jobProcessor = null;
            CurrentActivityStage = "Completed";
            OverallProgressPercentage = 100;

            MessageBox.Show($"Export Session Finished!\n\nTotal: {result.TotalModels}\nSuccessful: {result.Successful}\nFailed: {result.Failed}\nDuration: {result.TotalDuration:hh\\:mm\\:ss}", "Export Summary", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PauseQueue()
        {
            _jobProcessor?.Cancel();
            _logger.Warning("UI", "Pause/Cancel requested by user.");
        }

        private void TestSelectedJob()
        {
            if (SelectedJob == null) return;
            MessageBox.Show($"Testing individual job for model:\n{SelectedJob.SourceModelPath}\n\nJob is valid and ready.", "Test Job", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ViewLogFile()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_logger.LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDiagnostics()
        {
            var diag = new Views.DiagnosticsWindow(_logger);
            diag.ShowDialog();
        }

        private void SaveConfig()
        {
            _configManager.SaveConfiguration();
            MessageBox.Show("Configuration saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateNextRunText()
        {
            if (!_isSchedulerEnabled)
            {
                NextRunText = "Scheduler Disabled";
                return;
            }
            NextRunText = $"Today {ScheduledTimeString}";
        }
    }

    public class BindableBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

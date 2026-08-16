using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit.ExternalEvents;
using ScheduledNwcExporter.Scheduler;

namespace ScheduledNwcExporter.UI.ViewModels
{
    /// <summary>
    /// View model for the modeless Scheduled NWC Export Manager window.
    /// It never calls the Revit API directly; Revit operations are raised to ExportQueueExternalEventHandler.
    /// </summary>
    public class MainViewModel : BindableBase
    {
        private readonly ConfigurationManager _configManager;
        private readonly FileLogger _logger;
        private readonly ScheduleManager _scheduleManager;
        private readonly ExportQueueExternalEventHandler _queueHandler;

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

                    if (value)
                    {
                        _scheduleManager.Start();
                    }
                    else
                    {
                        _scheduleManager.Stop();
                    }
                }
            }
        }

        private string _scheduledTimeString = "19:00";
        public string ScheduledTimeString
        {
            get => _scheduledTimeString;
            set
            {
                if (SetProperty(ref _scheduledTimeString, value) && TimeSpan.TryParse(value, out TimeSpan time))
                {
                    _configManager.CurrentSettings.Scheduler.ScheduledHour = time.Hours;
                    _configManager.CurrentSettings.Scheduler.ScheduledMinute = time.Minutes;
                    _configManager.SaveConfiguration();
                    UpdateNextRunText();
                }
            }
        }

        private string _nextRunText = "Scheduler Disabled";
        public string NextRunText
        {
            get => _nextRunText;
            private set => SetProperty(ref _nextRunText, value);
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

        private bool _useTemporaryCopyWithoutRevitLinks = true;
        public bool UseTemporaryCopyWithoutRevitLinks
        {
            get => _useTemporaryCopyWithoutRevitLinks;
            set
            {
                if (SetProperty(ref _useTemporaryCopyWithoutRevitLinks, value))
                {
                    _configManager.CurrentSettings.Export.UseTemporaryCopyWithoutRevitLinks = value;
                    _configManager.SaveConfiguration();
                }
            }
        }

        private bool _divideFileIntoLevels;
        public bool DivideFileIntoLevels
        {
            get => _divideFileIntoLevels;
            set { if (SetProperty(ref _divideFileIntoLevels, value)) { _configManager.CurrentSettings.Export.DivideFileIntoLevels = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportParts;
        public bool ExportParts
        {
            get => _exportParts;
            set { if (SetProperty(ref _exportParts, value)) { _configManager.CurrentSettings.Export.ExportParts = value; _configManager.SaveConfiguration(); } }
        }

        private double _facetingFactor = 1.0;
        public double FacetingFactor
        {
            get => _facetingFactor;
            set { if (SetProperty(ref _facetingFactor, value)) { _configManager.CurrentSettings.Export.FacetingFactor = value; _configManager.SaveConfiguration(); } }
        }

        private string _parameterExportMode = "All";
        public string ParameterExportMode
        {
            get => _parameterExportMode;
            set { if (SetProperty(ref _parameterExportMode, value)) { _configManager.CurrentSettings.Export.ParameterExportMode = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportUrls;
        public bool ExportUrls
        {
            get => _exportUrls;
            set { if (SetProperty(ref _exportUrls, value)) { _configManager.CurrentSettings.Export.ExportUrls = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportRoomAsAttribute;
        public bool ExportRoomAsAttribute
        {
            get => _exportRoomAsAttribute;
            set { if (SetProperty(ref _exportRoomAsAttribute, value)) { _configManager.CurrentSettings.Export.ExportRoomAsAttribute = value; _configManager.SaveConfiguration(); } }
        }

        private bool _convertLights;
        public bool ConvertLights
        {
            get => _convertLights;
            set { if (SetProperty(ref _convertLights, value)) { _configManager.CurrentSettings.Export.ConvertLights = value; _configManager.SaveConfiguration(); } }
        }

        private bool _findMissingMaterials;
        public bool FindMissingMaterials
        {
            get => _findMissingMaterials;
            set { if (SetProperty(ref _findMissingMaterials, value)) { _configManager.CurrentSettings.Export.FindMissingMaterials = value; _configManager.SaveConfiguration(); } }
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

        private string _currentActivityModel = string.Empty;
        public string CurrentActivityModel
        {
            get => _currentActivityModel;
            private set => SetProperty(ref _currentActivityModel, value);
        }

        private string _currentActivityStage = "Idle";
        public string CurrentActivityStage
        {
            get => _currentActivityStage;
            private set => SetProperty(ref _currentActivityStage, value);
        }

        private string _estimatedTimeRemaining = "Calculating…";
        public string EstimatedTimeRemaining
        {
            get => _estimatedTimeRemaining;
            private set => SetProperty(ref _estimatedTimeRemaining, value);
        }

        private DateTime _sessionStartTime;
        private int _totalJobsInSession;

        private int _overallProgressPercentage;
        public int OverallProgressPercentage
        {
            get => _overallProgressPercentage;
            private set => SetProperty(ref _overallProgressPercentage, value);
        }

        private ModelExportJob? _selectedJob;
        public ModelExportJob? SelectedJob
        {
            get => _selectedJob;
            set => SetProperty(ref _selectedJob, value);
        }

        public ObservableCollection<ModelExportJob> Jobs { get; }

        public ICommand AddModelCommand { get; }
        public ICommand EditModelCommand { get; }
        public ICommand RemoveModelCommand { get; }
        public ICommand RunNowCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand TestSelectedCommand { get; }
        public ICommand ViewLogCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveConfigurationCommand { get; }

        public MainViewModel(ConfigurationManager configManager, FileLogger logger, ExportQueueExternalEventHandler queueHandler)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queueHandler = queueHandler ?? throw new ArgumentNullException(nameof(queueHandler));

            AppSettings settings = _configManager.CurrentSettings;
            _logger.DebugMode = settings.DebugMode;
            _isSchedulerEnabled = settings.Scheduler.IsSchedulerEnabled;
            _scheduledTimeString = $"{settings.Scheduler.ScheduledHour:D2}:{settings.Scheduler.ScheduledMinute:D2}";
            _exportLinks = settings.Export.ExportLinks;
            _useTemporaryCopyWithoutRevitLinks = settings.Export.UseTemporaryCopyWithoutRevitLinks;
            _divideFileIntoLevels = settings.Export.DivideFileIntoLevels;
            _exportParts = settings.Export.ExportParts;
            _facetingFactor = settings.Export.FacetingFactor;
            _parameterExportMode = settings.Export.ParameterExportMode;
            _exportUrls = settings.Export.ExportUrls;
            _exportRoomAsAttribute = settings.Export.ExportRoomAsAttribute;
            _convertLights = settings.Export.ConvertLights;
            _findMissingMaterials = settings.Export.FindMissingMaterials;
            _coordinates = settings.Export.Coordinates;
            _overwritePolicy = settings.Export.OverwritePolicy;
            Jobs = new ObservableCollection<ModelExportJob>(settings.Jobs);

            _scheduleManager = new ScheduleManager(settings, _logger);
            _scheduleManager.ScheduledTimeReached += (_, __) => StartQueue(Jobs.Where(job => job.IsEnabled));
            if (_isSchedulerEnabled)
            {
                _scheduleManager.Start();
            }

            _queueHandler.ProgressChanged += QueueHandler_ProgressChanged;
            _queueHandler.SessionCompleted += QueueHandler_SessionCompleted;

            AddModelCommand = new RelayCommand(AddModel);
            EditModelCommand = new RelayCommand(EditModel, () => SelectedJob != null);
            RemoveModelCommand = new RelayCommand(RemoveModel, () => SelectedJob != null);
            RunNowCommand = new RelayCommand(() => StartQueue(Jobs.Where(job => job.IsEnabled)));
            PauseCommand = new RelayCommand(PauseQueue, () => _queueHandler.IsSessionRunning);
            TestSelectedCommand = new RelayCommand(() =>
            {
                if (SelectedJob != null) StartQueue(new[] { SelectedJob });
            }, () => SelectedJob != null && !_queueHandler.IsSessionRunning);
            ViewLogCommand = new RelayCommand(ViewLogFile);
            OpenSettingsCommand = new RelayCommand(OpenDiagnostics);
            SaveConfigurationCommand = new RelayCommand(SaveConfig);

            UpdateNextRunText();
        }

        public void Shutdown()
        {
            _scheduleManager.Stop();
            _queueHandler.RequestCancellation();
            _queueHandler.ProgressChanged -= QueueHandler_ProgressChanged;
            _queueHandler.SessionCompleted -= QueueHandler_SessionCompleted;
        }

        private void AddModel()
        {
            var dialog = new Views.JobEditorWindow(null);
            if (dialog.ShowDialog() == true && dialog.Job != null)
            {
                Jobs.Add(dialog.Job);
                SaveJobs();
                _logger.Info("UI", $"Added export job: {dialog.Job.SourceModelPath}");
            }
        }

        private void EditModel()
        {
            if (SelectedJob == null) return;

            var dialog = new Views.JobEditorWindow(SelectedJob);
            if (dialog.ShowDialog() == true && dialog.Job != null)
            {
                int index = Jobs.IndexOf(SelectedJob);
                if (index >= 0)
                {
                    Jobs[index] = dialog.Job;
                    SaveJobs();
                    _logger.Info("UI", $"Updated export job: {dialog.Job.SourceModelPath}");
                }
            }
        }

        private void RemoveModel()
        {
            if (SelectedJob == null) return;

            _logger.Info("UI", $"Removed export job: {SelectedJob.SourceModelPath}");
            Jobs.Remove(SelectedJob);
            SaveJobs();
        }

        private void StartQueue(IEnumerable<ModelExportJob> jobs)
        {
            if (_queueHandler.IsSessionRunning)
            {
                MessageBox.Show("An export session is already running.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var jobList = jobs.ToList();
            if (jobList.Count == 0)
            {
                MessageBox.Show("There are no enabled export jobs to run.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveJobs();
            OverallProgressPercentage = 0;
            CurrentActivityModel = string.Empty;
            CurrentActivityStage = "Queueing Revit external event…";
            EstimatedTimeRemaining = "Calculating…";
            _sessionStartTime = DateTime.Now;
            _totalJobsInSession = jobList.Count;

            if (!_queueHandler.Start(jobList))
            {
                CurrentActivityStage = "Unable to queue export session.";
                MessageBox.Show("The export queue could not be started. The add-in may be shutting down.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PauseQueue()
        {
            _queueHandler.RequestCancellation();
            CurrentActivityStage = "Cancellation requested; waiting for the current safe boundary.";
        }

        private void QueueHandler_ProgressChanged(object? sender, ExportSessionProgress progress)
        {
            CurrentActivityModel = progress.ModelName;
            CurrentActivityStage = progress.Stage;
            OverallProgressPercentage = progress.PercentComplete;

            if (progress.CompletedJobs > 0 && progress.TotalJobs > 0)
            {
                var elapsed = DateTime.Now - _sessionStartTime;
                double avgTimePerJob = elapsed.TotalSeconds / progress.CompletedJobs;
                int remainingJobs = progress.TotalJobs - progress.CompletedJobs;
                double remainingSeconds = avgTimePerJob * remainingJobs;
                var remainingSpan = TimeSpan.FromSeconds(remainingSeconds);
                EstimatedTimeRemaining = remainingSpan.TotalMinutes >= 1
                    ? $"{remainingSpan.Minutes}m {remainingSpan.Seconds}s remaining"
                    : $"{remainingSpan.Seconds}s remaining";
            }
            else
            {
                EstimatedTimeRemaining = "Calculating…";
            }
        }

        private void QueueHandler_SessionCompleted(object? sender, ExportSessionSummary summary)
        {
            CurrentActivityModel = string.Empty;
            CurrentActivityStage = string.IsNullOrWhiteSpace(summary.SessionError) ? "Completed" : summary.SessionError;
            OverallProgressPercentage = 100;
            SaveJobs();

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string message = $"Export session finished.\n\nTotal: {summary.TotalModels}\nSuccessful: {summary.Successful}\nFailed: {summary.Failed}\nSkipped: {summary.Skipped}\nCancelled: {summary.Cancelled}\nDuration: {summary.Duration:hh\\:mm\\:ss}";
                if (summary.FailedModels.Count > 0)
                {
                    message += "\n\nFailed Models:\n- " + string.Join("\n- ", summary.FailedModels);
                }
                if (!string.IsNullOrWhiteSpace(summary.SessionError))
                {
                    message += $"\n\nSession Error:\n{summary.SessionError}";
                }

                MessageBox.Show(message, "Scheduled NWC Export Manager", MessageBoxButton.OK,
                    summary.Failed > 0 || !string.IsNullOrWhiteSpace(summary.SessionError) ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }), DispatcherPriority.Background);
        }

        private void ViewLogFile()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_logger.LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the log file: {ex.Message}", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDiagnostics()
        {
            new Views.DiagnosticsWindow(_logger).ShowDialog();
        }

        private void SaveConfig()
        {
            SaveJobs();
            MessageBox.Show("Configuration saved successfully.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveJobs()
        {
            _configManager.CurrentSettings.Jobs = new List<ModelExportJob>(Jobs);
            _configManager.SaveConfiguration();
        }

        private void UpdateNextRunText()
        {
            NextRunText = _isSchedulerEnabled ? $"Daily at {ScheduledTimeString}" : "Scheduler Disabled";
        }
    }

    public class BindableBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

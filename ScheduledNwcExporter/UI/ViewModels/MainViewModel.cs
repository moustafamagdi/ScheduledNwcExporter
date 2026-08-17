using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit.ExternalEvents;
using ScheduledNwcExporter.Scheduler;

using ScheduledNwcExporter.UI;

namespace ScheduledNwcExporter.UI.ViewModels
{
    /// <summary>
    /// View model for the modeless Scheduled NWC Export Manager window.
    /// It never calls the Revit API directly; Revit operations are raised to ExportQueueExternalEventHandler.
    /// </summary>
    public class MainViewModel : BindableBase
    {
        private readonly ConfigurationManager _configManager;
        private readonly ILogger _logger;
        private readonly Dispatcher _uiDispatcher;
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

        private bool _convertElementProperties;
        public bool ConvertElementProperties
        {
            get => _convertElementProperties;
            set { if (SetProperty(ref _convertElementProperties, value)) { _configManager.CurrentSettings.Export.ConvertElementProperties = value; _configManager.SaveConfiguration(); } }
        }

        private bool _divideFileIntoLevels;
        public bool DivideFileIntoLevels
        {
            get => _divideFileIntoLevels;
            set { if (SetProperty(ref _divideFileIntoLevels, value)) { _configManager.CurrentSettings.Export.DivideFileIntoLevels = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportElementIds;
        public bool ExportElementIds
        {
            get => _exportElementIds;
            set { if (SetProperty(ref _exportElementIds, value)) { _configManager.CurrentSettings.Export.ExportElementIds = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportParts;
        public bool ExportParts
        {
            get => _exportParts;
            set { if (SetProperty(ref _exportParts, value)) { _configManager.CurrentSettings.Export.ExportParts = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportInternalCoordinates;
        public bool ExportInternalCoordinates
        {
            get => _exportInternalCoordinates;
            set { if (SetProperty(ref _exportInternalCoordinates, value)) { _configManager.CurrentSettings.Export.ExportInternalCoordinates = value; _configManager.SaveConfiguration(); } }
        }

        private bool _convertLights;
        public bool ConvertLights
        {
            get => _convertLights;
            set { if (SetProperty(ref _convertLights, value)) { _configManager.CurrentSettings.Export.ConvertLights = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportRoomAsAttribute;
        public bool ExportRoomAsAttribute
        {
            get => _exportRoomAsAttribute;
            set { if (SetProperty(ref _exportRoomAsAttribute, value)) { _configManager.CurrentSettings.Export.ExportRoomAsAttribute = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportRoomGeometry;
        public bool ExportRoomGeometry
        {
            get => _exportRoomGeometry;
            set { if (SetProperty(ref _exportRoomGeometry, value)) { _configManager.CurrentSettings.Export.ExportRoomGeometry = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportUrls;
        public bool ExportUrls
        {
            get => _exportUrls;
            set { if (SetProperty(ref _exportUrls, value)) { _configManager.CurrentSettings.Export.ExportUrls = value; _configManager.SaveConfiguration(); } }
        }

        private bool _findMissingMaterials;
        public bool FindMissingMaterials
        {
            get => _findMissingMaterials;
            set { if (SetProperty(ref _findMissingMaterials, value)) { _configManager.CurrentSettings.Export.FindMissingMaterials = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportAllParameters;
        public bool ExportAllParameters
        {
            get => _exportAllParameters;
            set { if (SetProperty(ref _exportAllParameters, value)) { _configManager.CurrentSettings.Export.ExportAllParameters = value; _configManager.SaveConfiguration(); } }
        }

        private bool _exportElementParameters;
        public bool ExportElementParameters
        {
            get => _exportElementParameters;
            set { if (SetProperty(ref _exportElementParameters, value)) { _configManager.CurrentSettings.Export.ExportElementParameters = value; _configManager.SaveConfiguration(); } }
        }

        private double _facetingFactor = 1.0;
        public double FacetingFactor
        {
            get => _facetingFactor;
            set { if (SetProperty(ref _facetingFactor, value)) { _configManager.CurrentSettings.Export.FacetingFactor = value; _configManager.SaveConfiguration(); } }
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
        public ObservableCollection<ScheduleSlot> ScheduleSlots { get; } = new ObservableCollection<ScheduleSlot>();

        private ScheduleSlot? _selectedSlot;
        public ScheduleSlot? SelectedSlot
        {
            get => _selectedSlot;
            set => SetProperty(ref _selectedSlot, value);
        }

        public ICommand AddModelCommand { get; }
        public ICommand EditModelCommand { get; }
        public ICommand RemoveModelCommand { get; }
        public ICommand RunNowCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand TestSelectedCommand { get; }
        public ICommand ViewLogCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveConfigurationCommand { get; }
        public ICommand ExportSettingsCommand { get; }
        public ICommand ImportSettingsCommand { get; }
        public ICommand AddSlotCommand { get; }
        public ICommand RemoveSlotCommand { get; }
        // Removed separate job commands in favor of unified settings export/import

        public MainViewModel(ConfigurationManager configManager, ILogger logger, ExportQueueExternalEventHandler queueHandler, ScheduleManager? scheduleManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queueHandler = queueHandler ?? throw new ArgumentNullException(nameof(queueHandler));
            _scheduleManager = scheduleManager ?? throw new ArgumentNullException(nameof(scheduleManager));
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            AppSettings settings = _configManager.CurrentSettings;
            _logger.DebugMode = settings.DebugMode;
            _isSchedulerEnabled = settings.Scheduler.IsSchedulerEnabled;
            _scheduledTimeString = $"{settings.Scheduler.ScheduledHour:D2}:{settings.Scheduler.ScheduledMinute:D2}";
            
            // Initialize Export Settings
            _convertElementProperties = settings.Export.ConvertElementProperties;
            _divideFileIntoLevels = settings.Export.DivideFileIntoLevels;
            _exportElementIds = settings.Export.ExportElementIds;
            _exportParts = settings.Export.ExportParts;
            _exportInternalCoordinates = settings.Export.ExportInternalCoordinates;
            _convertLights = settings.Export.ConvertLights;
            _exportRoomAsAttribute = settings.Export.ExportRoomAsAttribute;
            _exportRoomGeometry = settings.Export.ExportRoomGeometry;
            _exportUrls = settings.Export.ExportUrls;
            _findMissingMaterials = settings.Export.FindMissingMaterials;
            _exportAllParameters = settings.Export.ExportAllParameters;
            _exportElementParameters = settings.Export.ExportElementParameters;
            _facetingFactor = settings.Export.FacetingFactor;
            _overwritePolicy = settings.Export.OverwritePolicy;
            _useTemporaryCopyWithoutRevitLinks = settings.Export.UseTemporaryCopyWithoutRevitLinks;
            foreach (var job in settings.Jobs)
            {
                // Reset status on startup so they appear clean without success colors until run
                if (job.Status == JobStatus.Success || job.Status == JobStatus.Failed || job.Status == JobStatus.Processing)
                {
                    job.Status = JobStatus.Ready;
                }
                job.ProgressPercentage = 0;
                job.CurrentStage = string.Empty;
            }
            Jobs = new ObservableCollection<ModelExportJob>(settings.Jobs);
            if (settings.Scheduler.Slots != null)
            {
                foreach (var slot in settings.Scheduler.Slots) ScheduleSlots.Add(slot);
            }

            // AUDIT FIX: Scheduler lifecycle is now managed at App level. 
            // The ViewModel just listens for UI updates.
            _scheduleManager.ScheduledTimeReached += ScheduleManager_ScheduledTimeReached;
            
            _queueHandler.ProgressChanged += QueueHandler_ProgressChanged;
            _queueHandler.SessionCompleted += QueueHandler_SessionCompleted;

            AddModelCommand = new RelayCommand(AddModel);
            EditModelCommand = new RelayCommand(EditModel, () => SelectedJob != null);
            RemoveModelCommand = new RelayCommand(RemoveModel, () => SelectedJob != null);
            AddSlotCommand = new RelayCommand(AddSlot);
            RemoveSlotCommand = new RelayCommand(RemoveSlot, () => SelectedSlot != null);
            RunNowCommand = new RelayCommand(() => StartQueue(Jobs.Where(job => job.IsEnabled), SessionTriggerSource.Manual));
            PauseCommand = new RelayCommand(PauseQueue, () => _queueHandler.IsSessionRunning);
            TestSelectedCommand = new RelayCommand(() =>
            {
                if (SelectedJob != null) StartQueue(new[] { SelectedJob }, SessionTriggerSource.Manual);
            }, () => SelectedJob != null && !_queueHandler.IsSessionRunning);
            ViewLogCommand = new RelayCommand(ViewLogFile);
            OpenSettingsCommand = new RelayCommand(OpenDiagnostics);
            SaveConfigurationCommand = new RelayCommand(SaveConfig);
            ExportSettingsCommand = new RelayCommand(ExportSettingsToFile);
            ImportSettingsCommand = new RelayCommand(ImportSettingsFromFile);

            UpdateNextRunText();
        }

        public void Shutdown()
        {
            // AUDIT FIX: Do NOT stop the scheduler on window close. It must persist at App level.
            if (_scheduleManager != null)
            {
                _scheduleManager.ScheduledTimeReached -= ScheduleManager_ScheduledTimeReached;
            }
            _queueHandler.ProgressChanged -= QueueHandler_ProgressChanged;
            _queueHandler.SessionCompleted -= QueueHandler_SessionCompleted;
        }

        private void ScheduleManager_ScheduledTimeReached(object sender, EventArgs e)
        {
            _uiDispatcher.Invoke(() => StartQueue(Jobs.Where(job => job.IsEnabled), SessionTriggerSource.Scheduler));
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

        private void StartQueue(IEnumerable<ModelExportJob> jobs, SessionTriggerSource triggerSource = SessionTriggerSource.Manual)
        {
            if (_queueHandler.IsSessionRunning)
            {
                if (triggerSource == SessionTriggerSource.Manual)
                {
                    MessageBox.Show("An export session is already running.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    _logger.Warning("Scheduler", "A scheduled export was skipped because a session is already in progress.");
                }
                return;
            }

            var jobList = jobs.ToList();
            if (jobList.Count == 0)
            {
                if (triggerSource == SessionTriggerSource.Manual)
                {
                    MessageBox.Show("There are no enabled export jobs to run.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            SaveJobs();
            OverallProgressPercentage = 0;
            CurrentActivityModel = string.Empty;
            CurrentActivityStage = "Queueing Revit external event…";
            EstimatedTimeRemaining = "Calculating…";
            _sessionStartTime = DateTime.Now;
            _totalJobsInSession = jobList.Count;

            if (!_queueHandler.Start(jobList, triggerSource))
            {
                CurrentActivityStage = "Unable to queue export session.";
                if (triggerSource == SessionTriggerSource.Manual)
                {
                    MessageBox.Show("The export queue could not be started. The add-in may be shutting down.", "Scheduled NWC Export Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

            // AUDIT FIX: Only show MessageBox for manual runs. Scheduled runs should remain unattended.
            if (summary.TriggerSource == SessionTriggerSource.Manual)
            {
                _uiDispatcher.BeginInvoke(new Action(() =>
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
            else
            {
                _logger.Info("Scheduler", $"Scheduled export session completed. Successful: {summary.Successful}, Failed: {summary.Failed}. Check log for details.");
            }
        }

        private void ViewLogFile()
        {
            try
            {
                if (File.Exists(_logger.LogFilePath))
                {
                    new Views.LogViewerWindow(_logger.LogFilePath).Show();
                }
                else
                {
                    MessageBox.Show("Log file does not exist yet.", "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the log file: {ex.Message}", "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDiagnostics()
        {
            new Views.DiagnosticsWindow(_logger, _configManager.CurrentSettings).ShowDialog();
        }

        private void SaveConfig()
        {
            _configManager.SaveConfiguration();
            MessageBox.Show("Configuration saved successfully.", "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportSettingsToFile()
        {
            var dialog = new Views.SettingsExportDialog();
            if (dialog.ShowDialog() == true)
            {
                bool includeJobs = dialog.IncludeModelList;
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Hatco NWC Exporter Configuration (*.json)|*.json|All Files (*.*)|*.*",
                    Title = "Export Hatco Configuration",
                    FileName = "HatcoNwcExporter_Config.json"
                };
                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        _configManager.ExportUnifiedSettings(dlg.FileName, includeJobs);
                        MessageBox.Show("Configuration exported successfully to:\n" + dlg.FileName, "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to export configuration:\n" + ex.Message, "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ImportSettingsFromFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Hatco NWC Exporter Configuration (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Import Hatco Configuration"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _configManager.ImportUnifiedSettings(dlg.FileName);
                    // Refresh view model properties from updated config
                    AppSettings settings = _configManager.CurrentSettings;
                    IsSchedulerEnabled = settings.Scheduler.IsSchedulerEnabled;
                    ScheduledTimeString = $"{settings.Scheduler.ScheduledHour:D2}:{settings.Scheduler.ScheduledMinute:D2}";
                    UseTemporaryCopyWithoutRevitLinks = settings.Export.UseTemporaryCopyWithoutRevitLinks;
                    ConvertElementProperties = settings.Export.ConvertElementProperties;
                    DivideFileIntoLevels = settings.Export.DivideFileIntoLevels;
                    ExportElementIds = settings.Export.ExportElementIds;
                    ExportParts = settings.Export.ExportParts;
                    ExportInternalCoordinates = settings.Export.ExportInternalCoordinates;
                    ConvertLights = settings.Export.ConvertLights;
                    ExportRoomAsAttribute = settings.Export.ExportRoomAsAttribute;
                    ExportRoomGeometry = settings.Export.ExportRoomGeometry;
                    ExportUrls = settings.Export.ExportUrls;
                    FindMissingMaterials = settings.Export.FindMissingMaterials;
                    ExportAllParameters = settings.Export.ExportAllParameters;
                    ExportElementParameters = settings.Export.ExportElementParameters;
                    FacetingFactor = settings.Export.FacetingFactor;
                    OverwritePolicy = settings.Export.OverwritePolicy;

                    Jobs.Clear();
                    foreach (var job in settings.Jobs)
                    {
                        Jobs.Add(job);
                    }

                    ScheduleSlots.Clear();
                    if (settings.Scheduler.Slots != null)
                    {
                        foreach (var slot in settings.Scheduler.Slots)
                        {
                            ScheduleSlots.Add(slot);
                        }
                    }

                    MessageBox.Show("Configuration imported successfully. UI and queue have been updated.", "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to import configuration:\n" + ex.Message, "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveJobs()
        {
            _configManager.CurrentSettings.Jobs = Jobs.ToList();
            _configManager.CurrentSettings.Scheduler.Slots = ScheduleSlots.ToList();
            _configManager.SaveConfiguration();
        }

        private void AddSlot()
        {
            var newSlot = new ScheduleSlot();
            ScheduleSlots.Add(newSlot);
            SaveJobs();
        }

        private void RemoveSlot()
        {
            if (SelectedSlot != null)
            {
                ScheduleSlots.Remove(SelectedSlot);
                SaveJobs();
            }
        }

        private void UpdateNextRunText()
        {
            if (!_isSchedulerEnabled)
            {
                NextRunText = "Scheduler Disabled";
                return;
            }

            var activeSlots = ScheduleSlots.Where(s => s.IsEnabled).ToList();
            if (activeSlots.Count == 0)
            {
                NextRunText = "Scheduler Active (No Slots)";
                return;
            }

            if (activeSlots.Count == 1)
            {
                NextRunText = $"Next run: {activeSlots[0].TimeDisplay} ({activeSlots[0].DaysDisplay})";
            }
            else
            {
                NextRunText = $"Scheduler Active ({activeSlots.Count} slots)";
            }
        }
    }


}

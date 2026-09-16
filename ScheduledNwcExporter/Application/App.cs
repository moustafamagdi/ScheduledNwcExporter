using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.Reliability;
using ScheduledNwcExporter.UI.Views;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Revit external application entry point and owner of the modeless export manager window.
    /// </summary>
    public class App : IExternalApplication
    {
        internal static MainWindow? ExportManagerWindow { get; set; }
        internal static Configuration.ConfigurationManager? ConfigManager { get; private set; }
        internal static Logging.ILogger? Logger { get; private set; }
        internal static Scheduler.ScheduleManager? Scheduler { get; private set; }
        internal static Revit.ExternalEvents.ExportQueueExternalEventHandler? QueueHandler { get; private set; }
        internal static ExternalEvent? QueueEvent { get; private set; }
        private static bool _dialogHandlerSubscribed;
        private static bool _scheduledSelectionInProgress;

        public Result OnStartup(UIControlledApplication application)
        {
            Core.AssemblyLoader.Register();

            try
            {
                InitializeCoreServices();

                if (!_dialogHandlerSubscribed)
                {
                    application.DialogBoxShowing += UnattendedDialogHandler.OnDialogBoxShowing;
                    _dialogHandlerSubscribed = true;
                }

                const string tabName = "Hatco";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                }

                RibbonPanel panel = null;
                foreach (var existingPanel in application.GetRibbonPanels(tabName))
                {
                    if (existingPanel.Name == "Navisworks Export")
                    {
                        panel = existingPanel;
                        break;
                    }
                }

                if (panel == null)
                    panel = application.CreateRibbonPanel(tabName, "Navisworks Export");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    "CmdHatcoNwcExport",
                    "Hatco NWC\nExporter",
                    assemblyPath,
                    "ScheduledNwcExporter.Application.Command")
                {
                    ToolTip = "Launch the Hatco NWC Exporter.",
                    LongDescription = "Configure automated batch NWC exports with advanced geometry, parameter, and schedule controls for Revit 2024."
                };

                var pushButton = panel.AddItem(buttonData) as PushButton;
                if (pushButton != null)
                    pushButton.AvailabilityClassName = "ScheduledNwcExporter.Application.CommandAvailability";

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scheduled NWC Export Manager", $"Failed to initialize the add-in:\n{ex.Message}");
                return Result.Failed;
            }
        }

        internal static void EnsureServicesInitialized(UIApplication uiApplication)
        {
            if (uiApplication == null) throw new ArgumentNullException(nameof(uiApplication));

            InitializeCoreServices();

            if (!_dialogHandlerSubscribed)
            {
                uiApplication.DialogBoxShowing += UnattendedDialogHandler.OnDialogBoxShowing;
                _dialogHandlerSubscribed = true;
            }
        }

        private static void InitializeCoreServices()
        {
            if (Logger == null)
                Logger = new Logging.FileLogger();

            if (ConfigManager == null)
                ConfigManager = new Configuration.ConfigurationManager(Logger);

            Logger.DebugMode = ConfigManager.CurrentSettings.DebugMode;

            // Older builds reused IsEnabled for the automatic freshness selection and persisted
            // those transient choices. Run this once before the ViewModel calculates the new
            // IsSelectedForRun state so legacy jobs do not appear incorrectly disabled/dimmed.
            LegacyRunSelectionMigration.RunOnce(ConfigManager, Logger);

            NormalizeScheduleDays();

            if (QueueHandler == null)
            {
                QueueHandler = new Revit.ExternalEvents.ExportQueueExternalEventHandler(
                    Logger,
                    ConfigManager.CurrentSettings,
                    System.Windows.Threading.Dispatcher.CurrentDispatcher,
                    ConfigManager);
            }

            if (QueueEvent == null)
            {
                QueueEvent = ExternalEvent.Create(QueueHandler);
                QueueHandler.AttachExternalEvent(QueueEvent);
            }

            if (Scheduler == null)
            {
                Scheduler = new Scheduler.ScheduleManager(ConfigManager.CurrentSettings, Logger);
                Scheduler.ScheduledTimeReached += OnScheduledTimeReachedStatic;
                if (ConfigManager.CurrentSettings.Scheduler.IsSchedulerEnabled)
                    Scheduler.Start();
            }
        }

        private static void NormalizeScheduleDays()
        {
            if (ConfigManager == null || Logger == null) return;

            bool scheduleNormalized = false;
            if (ConfigManager.CurrentSettings.Scheduler.Slots != null)
            {
                foreach (var slot in ConfigManager.CurrentSettings.Scheduler.Slots)
                {
                    var normalizedDays = (slot.Days ?? new List<DayOfWeek>())
                        .Distinct()
                        .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
                        .ToList();

                    if (slot.Days == null || !slot.Days.SequenceEqual(normalizedDays))
                    {
                        slot.Days = normalizedDays;
                        scheduleNormalized = true;
                    }
                }
            }

            if (scheduleNormalized)
            {
                ConfigManager.SaveConfiguration();
                Logger.Info("Scheduler", "Normalized duplicate schedule-day entries in configuration.");
            }
        }

        private static async void OnScheduledTimeReachedStatic(object sender, EventArgs e)
        {
            if (ConfigManager == null || QueueHandler == null || Logger == null) return;

            // When the manager is visible, its ViewModel owns the scheduled callback so the UI can
            // stay synchronized. The app-level path handles schedules while the window is closed.
            if (ExportManagerWindow != null && ExportManagerWindow.IsVisible)
                return;

            if (QueueHandler.IsSessionRunning)
            {
                Logger.Warning("Scheduler", "Scheduled export was skipped because another export session is already running.");
                return;
            }

            if (_scheduledSelectionInProgress)
            {
                Logger.Warning("Scheduler", "Scheduled selection is already refreshing cloud metadata; duplicate trigger ignored.");
                return;
            }

            _scheduledSelectionInProgress = true;
            try
            {
                await RefreshCloudMetadataForScheduledRunAsync();

                if (ConfigManager == null || QueueHandler == null || Logger == null) return;

                var activeJobs = ConfigManager.CurrentSettings.Jobs
                    .Where(job => job.IsEnabled && FreshnessEvaluator.Evaluate(job, ConfigManager.CurrentSettings).NeedsExport)
                    .ToList();

                if (activeJobs.Count > 0)
                {
                    Logger.Info("Scheduler", $"Starting unattended scheduled export of {activeJobs.Count} stale/unverified model(s).");
                    QueueHandler.Start(activeJobs, Revit.ExternalEvents.SessionTriggerSource.Scheduler);
                }
                else
                {
                    Logger.Info("Scheduler", "Scheduled time reached, but no enabled model currently requires a verified export.");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error("Scheduler", $"Could not prepare the scheduled export selection: {ex.Message}", string.Empty, "FreshnessSelection", ex);
            }
            finally
            {
                _scheduledSelectionInProgress = false;
            }
        }

        private static async Task RefreshCloudMetadataForScheduledRunAsync()
        {
            if (ConfigManager == null || Logger == null) return;

            var cloudJobs = ConfigManager.CurrentSettings.Jobs
                .Where(job => job.IsEnabled && job.IsCloud)
                .ToList();
            if (cloudJobs.Count == 0) return;

            string accessToken = Core.CloudAuthenticationService.GetAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                foreach (var job in cloudJobs)
                    job.SourceMetadataError = "No Autodesk session is available to verify the current ACC version before the scheduled run.";

                ConfigManager.SaveConfiguration();
                Logger.Warning("Scheduler", "ACC metadata refresh could not run because no Autodesk session token was available. Cloud jobs remain conservatively unverified.", string.Empty, "FreshnessSelection");
                return;
            }

            var apsClient = new Core.APSClient(accessToken, Logger);
            foreach (var job in cloudJobs)
            {
                if (string.IsNullOrWhiteSpace(job.CloudDataProjectId) || string.IsNullOrWhiteSpace(job.CloudItemId))
                {
                    job.SourceMetadataError = "ACC identifiers are missing. Re-select this model in Cloud Explorer to enable version-aware freshness checks.";
                    continue;
                }

                try
                {
                    Core.CloudItemMetadata metadata = await apsClient.GetLatestItemMetadataAsync(job.CloudDataProjectId, job.CloudItemId);
                    job.LastSourceModifiedUtc = metadata.LastModifiedUtc;
                    job.LastMetadataRefreshUtc = DateTime.UtcNow;
                    job.CloudVersionId = metadata.VersionId;
                    job.SourceMetadataError = !string.IsNullOrWhiteSpace(metadata.VersionId) || metadata.LastModifiedUtc.HasValue
                        ? string.Empty
                        : "APS did not return a version or modification date for this ACC item.";
                }
                catch (Exception ex)
                {
                    job.SourceMetadataError = $"Could not refresh ACC metadata before scheduled export: {ex.Message}";
                    Logger.Warning("Scheduler", job.SourceMetadataError, job.DisplaySourcePath, "FreshnessSelection", ex);
                }
            }

            ConfigManager.SaveConfiguration();
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_dialogHandlerSubscribed)
            {
                application.DialogBoxShowing -= UnattendedDialogHandler.OnDialogBoxShowing;
                _dialogHandlerSubscribed = false;
            }

            if (Scheduler != null)
            {
                Scheduler.ScheduledTimeReached -= OnScheduledTimeReachedStatic;
                Scheduler.Stop();
            }

            Core.AssemblyLoader.Unregister();

            if (ExportManagerWindow != null)
            {
                ExportManagerWindow.Close();
                ExportManagerWindow = null;
            }

            QueueEvent?.Dispose();
            QueueEvent = null;
            QueueHandler = null;
            Scheduler = null;
            ConfigManager = null;
            Logger = null;
            _scheduledSelectionInProgress = false;

            return Result.Succeeded;
        }
    }
}

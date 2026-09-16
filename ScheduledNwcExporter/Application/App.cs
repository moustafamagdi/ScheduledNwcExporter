using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            // Create one logger for the entire Revit session and inject it into configuration and
            // downstream services. This prevents one add-in session from fragmenting diagnostics
            // across multiple unrelated log files.
            if (Logger == null)
                Logger = new Logging.FileLogger();

            if (ConfigManager == null)
                ConfigManager = new Configuration.ConfigurationManager(Logger);

            Logger.DebugMode = ConfigManager.CurrentSettings.DebugMode;

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

        private static void OnScheduledTimeReachedStatic(object sender, EventArgs e)
        {
            if (ConfigManager == null || QueueHandler == null || Logger == null) return;

            // When the manager is visible, its ViewModel owns the scheduled callback so the UI can
            // stay synchronized. The app-level path handles schedules while the window is closed.
            if (ExportManagerWindow != null && ExportManagerWindow.IsVisible)
                return;

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

            return Result.Succeeded;
        }
    }
}

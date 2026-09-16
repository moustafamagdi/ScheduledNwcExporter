using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
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
                {
                    panel = application.CreateRibbonPanel(tabName, "Navisworks Export");
                }

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
                {
                    pushButton.AvailabilityClassName = "ScheduledNwcExporter.Application.CommandAvailability";
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scheduled NWC Export Manager", $"Failed to initialize the add-in:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Makes direct loading through Revit Add-In Manager work as well as a normal .addin startup.
        /// ExternalEvent.Create must happen inside a valid Revit API execution context, so the command
        /// calls this method before constructing the modeless window.
        /// </summary>
        internal static void EnsureServicesInitialized(UIApplication uiApplication)
        {
            if (uiApplication == null) throw new ArgumentNullException(nameof(uiApplication));

            InitializeCoreServices();

            // Add-In Manager does not execute IExternalApplication.OnStartup when only the command
            // class is loaded. UIApplication exposes the same dialog event and lets this test path
            // retain unattended dialog handling without requiring a second application startup.
            if (!_dialogHandlerSubscribed)
            {
                uiApplication.DialogBoxShowing += UnattendedDialogHandler.OnDialogBoxShowing;
                _dialogHandlerSubscribed = true;
            }
        }

        private static void InitializeCoreServices()
        {
            if (ConfigManager == null)
            {
                ConfigManager = new Configuration.ConfigurationManager();
            }

            if (Logger == null)
            {
                Logger = new Logging.FileLogger { DebugMode = ConfigManager.CurrentSettings.DebugMode };
            }

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
                // This method is called only from OnStartup or IExternalCommand.Execute: both are
                // valid Revit API contexts for creating an ExternalEvent.
                QueueEvent = ExternalEvent.Create(QueueHandler);
                QueueHandler.AttachExternalEvent(QueueEvent);
            }

            if (Scheduler == null)
            {
                Scheduler = new Scheduler.ScheduleManager(ConfigManager.CurrentSettings, Logger);
                Scheduler.ScheduledTimeReached += OnScheduledTimeReachedStatic;
                if (ConfigManager.CurrentSettings.Scheduler.IsSchedulerEnabled)
                {
                    Scheduler.Start();
                }
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

            if (ExportManagerWindow != null && ExportManagerWindow.IsVisible)
            {
                return;
            }

            var activeJobs = ConfigManager.CurrentSettings.Jobs.Where(j => j.IsEnabled).ToList();
            if (activeJobs.Count > 0)
            {
                Logger.Info("Scheduler", $"Starting unattended scheduled export of {activeJobs.Count} models.");
                QueueHandler.Start(activeJobs, Revit.ExternalEvents.SessionTriggerSource.Scheduler);
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
            Logger = null;
            ConfigManager = null;

            return Result.Succeeded;
        }
    }
}

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

        public Result OnStartup(UIControlledApplication application)
        {
            // Register assembly resolver to handle dependencies like Autodesk.Forge
            Core.AssemblyLoader.Register();

            try
            {
                // AUDIT FIX: Initialize core services at App level to support unattended scheduling
                ConfigManager = new Configuration.ConfigurationManager();
                Logger = new Logging.FileLogger { DebugMode = ConfigManager.CurrentSettings.DebugMode };
                
                // Use current dispatcher (Revit main thread) for the queue handler
                QueueHandler = new Revit.ExternalEvents.ExportQueueExternalEventHandler(
                    Logger,
                    ConfigManager.CurrentSettings,
                    System.Windows.Threading.Dispatcher.CurrentDispatcher,
                    ConfigManager);
                QueueEvent = ExternalEvent.Create(QueueHandler);
                QueueHandler.AttachExternalEvent(QueueEvent);

                Scheduler = new Scheduler.ScheduleManager(ConfigManager.CurrentSettings, Logger);
                Scheduler.ScheduledTimeReached += OnScheduledTimeReached;
                
                if (ConfigManager.CurrentSettings.Scheduler.IsSchedulerEnabled)
                {
                    Scheduler.Start();
                }

                const string tabName = "Hatco";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // The tab may have been created by another add-in or an earlier load.
                }

                // Create or find panel safely to avoid conflicts with other Hatco add-ins
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

        private void OnScheduledTimeReached(object sender, EventArgs e)
        {
            if (ConfigManager == null || QueueHandler == null || Logger == null) return;

            // If the window is open, let the ViewModel handle it to update the UI
            if (ExportManagerWindow != null && ExportManagerWindow.IsVisible)
            {
                // The ViewModel is already subscribed to this event in the current implementation
                return;
            }

            // AUDIT FIX: Unattended background run when window is closed
            var activeJobs = ConfigManager.CurrentSettings.Jobs.Where(j => j.IsEnabled).ToList();
            if (activeJobs.Count > 0)
            {
                Logger.Info("Scheduler", $"Starting unattended scheduled export of {activeJobs.Count} models.");
                QueueHandler.Start(activeJobs, Revit.ExternalEvents.SessionTriggerSource.Scheduler);
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            Scheduler?.Stop();
            Core.AssemblyLoader.Unregister();

            if (ExportManagerWindow != null)
            {
                ExportManagerWindow.Close();
                ExportManagerWindow = null;
            }

            QueueEvent?.Dispose();

            return Result.Succeeded;
        }
    }
}

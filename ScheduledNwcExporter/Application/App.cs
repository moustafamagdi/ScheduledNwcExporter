using System;
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

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                const string tabName = "BIM Automation";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // The tab may have been created by another add-in or an earlier load.
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Navisworks Export");
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    "CmdScheduledNwcExport",
                    "Scheduled NWC\nManager",
                    assemblyPath,
                    "ScheduledNwcExporter.Application.Command")
                {
                    ToolTip = "Launch the Scheduled NWC Export Manager.",
                    LongDescription = "Configure model queues and start Revit 2024 NWC exports through a safe ExternalEvent-driven workflow."
                };

                panel.AddItem(buttonData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scheduled NWC Export Manager", $"Failed to initialize the add-in:\n{ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (ExportManagerWindow != null)
            {
                ExportManagerWindow.Close();
                ExportManagerWindow = null;
            }

            return Result.Succeeded;
        }
    }
}

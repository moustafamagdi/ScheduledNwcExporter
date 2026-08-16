using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Windows.Media.Imaging;

namespace ScheduledNwcExporter.Application
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // Create Ribbon Tab
                string tabName = "BIM Automation";
                try
                {
                    app.CreateRibbonTab(tabName);
                }
                catch
                {
                    // Tab might already exist
                }

                // Create Ribbon Panel
                RibbonPanel panel = app.CreateRibbonPanel(tabName, "Navisworks Export");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData buttonData = new PushButtonData(
                    "CmdScheduledNwcExport",
                    "Scheduled NWC\nManager",
                    assemblyPath,
                    "ScheduledNwcExporter.Application.Command"
                );

                PushButton pushButton = panel.AddItem(buttonData) as PushButton;
                if (pushButton != null)
                {
                    pushButton.ToolTip = "Launch the Scheduled NWC Export Manager to configure automated batch NWC exports.";
                    pushButton.LongDescription = "Provides a modeless WPF interface to manage model export queues, daily schedules, Navisworks export options, and detailed session logging.";
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scheduled NWC Exporter", $"Failed to initialize OnStartup: {ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}

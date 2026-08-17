using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.UI.Views;

namespace ScheduledNwcExporter.Application
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Ensure dependencies are resolvable (crucial for Add-In Manager usage)
            Core.AssemblyLoader.Register();

            try
            {
                if (App.ExportManagerWindow == null || !App.ExportManagerWindow.IsVisible)
                {
                    var window = new MainWindow();
                    window.Closed += (_, __) => App.ExportManagerWindow = null;
                    App.ExportManagerWindow = window;
                    window.Show();
                }
                else
                {
                    App.ExportManagerWindow.Activate();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Scheduled NWC Export Manager", $"Unable to launch the export manager:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}

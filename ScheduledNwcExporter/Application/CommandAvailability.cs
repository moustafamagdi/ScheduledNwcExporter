using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Makes the Hatco NWC Exporter command available even when no project or document is open in Revit.
    /// </summary>
    public class CommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            // Always available, regardless of whether a document is open or not.
            return true;
        }
    }
}

using System.Collections.Generic;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    public class LinkManager
    {
        private readonly ILogger _logger;

        public LinkManager(ILogger logger)
        {
            _logger = logger;
        }

        public void InspectAndLogRevitLinks(Document doc, string modelName)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfClass(typeof(RevitLinkInstance));

                int linkCount = 0;
                var linkNames = new List<string>();

                foreach (RevitLinkInstance linkInstance in collector)
                {
                    linkCount++;
                    string linkName = linkInstance.Name;
                    linkNames.Add(linkName);
                    _logger.Debug("Links", $"Detected Revit link instance: {linkName}", modelName, "LinkInspection");
                }

                _logger.Info("Links", $"Revit Links Detected: {linkCount} links found ({(linkNames.Count > 0 ? string.Join(", ", linkNames) : "None")})", modelName, "LinkInspection");
            }
            catch (Exception ex)
            {
                _logger.Warning("Links", $"Error inspecting Revit links: {ex.Message}", modelName, "LinkInspection", ex);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    public class WorksetInfo
    {
        public string Name { get; set; } = string.Empty;
        public WorksetId Id { get; set; } = WorksetId.InvalidWorksetId;
        public bool IsOpen { get; set; }
        public WorksetKind Kind { get; set; }
    }

    public class WorksetManager
    {
        private readonly ILogger _logger;

        public WorksetManager(ILogger logger)
        {
            _logger = logger;
        }

        public bool VerifyAndOpenAllWorksets(Document doc, string modelName)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    _logger.Info("Worksets", "Model is not workshared. No workset checks needed.", modelName, "WorksetVerification");
                    return true;
                }

                FilteredWorksetCollector collector = new FilteredWorksetCollector(doc);
                collector.OfKind(WorksetKind.UserWorkset);

                int totalWorksets = 0;
                int openWorksets = 0;

                foreach (Workset workset in collector)
                {
                    totalWorksets++;
                    bool isOpen = workset.IsOpen;
                    string wsName = workset.Name;
                    
                    _logger.Debug("Worksets", $"Workset '{wsName}' (ID: {workset.Id.IntegerValue}) State: {(isOpen ? "Open" : "Closed")}", modelName, "WorksetVerification");

                    if (isOpen)
                    {
                        openWorksets++;
                    }
                    else
                    {
                        // Attempt to open if closed
                        try
                        {
                            WorksetTable.OpenWorksets(doc, new List<WorksetId> { workset.Id });
                            _logger.Info("Worksets", $"Successfully opened closed user workset: {wsName}", modelName, "WorksetVerification");
                            openWorksets++;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("Worksets", $"Failed to open user workset '{wsName}': {ex.Message}", modelName, "WorksetVerification", ex);
                        }
                    }
                }

                _logger.Info("Worksets", $"Workset verification complete. Total user worksets: {totalWorksets}, Open: {openWorksets}", modelName, "WorksetVerification");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Worksets", $"Error during workset verification: {ex.Message}", modelName, "WorksetVerification", ex);
                return false;
            }
        }
    }
}

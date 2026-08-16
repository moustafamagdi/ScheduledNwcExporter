using System;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Verifies the workset state of a document after it has been opened.
    /// </summary>
    public class WorksetManager
    {
        private readonly ILogger _logger;

        public WorksetManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Logs each user workset's state and returns false if a workshared document still contains
        /// closed user worksets. Worksets are configured before opening in <see cref="DocumentManager"/>;
        /// Revit does not expose a supported API to open them after the document has loaded.
        /// </summary>
        public bool VerifyAllUserWorksetsOpen(Document doc, string modelName)
        {
            try
            {
                if (!doc.IsWorkshared)
                {
                    _logger.Info("Worksets", "Model is not workshared; workset verification is not required.", modelName, "WorksetVerification");
                    return true;
                }

                int totalUserWorksets = 0;
                int openUserWorksets = 0;
                int closedUserWorksets = 0;

                var collector = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                foreach (Workset workset in collector)
                {
                    totalUserWorksets++;
                    bool isOpen = workset.IsOpen;

                    _logger.Debug(
                        "Worksets",
                        $"Workset '{workset.Name}' (ID: {workset.Id.IntegerValue}) state: {(isOpen ? "Open" : "Closed")}",
                        modelName,
                        "WorksetVerification");

                    if (isOpen)
                    {
                        openUserWorksets++;
                    }
                    else
                    {
                        closedUserWorksets++;
                        _logger.Error(
                            "Worksets",
                            $"Required user workset is closed after opening: '{workset.Name}' (ID: {workset.Id.IntegerValue}).",
                            modelName,
                            "WorksetVerification");
                    }
                }

                _logger.Info(
                    "Worksets",
                    $"Workset verification complete. User worksets: {totalUserWorksets}; open: {openUserWorksets}; closed: {closedUserWorksets}.",
                    modelName,
                    "WorksetVerification");

                return closedUserWorksets == 0;
            }
            catch (Exception ex)
            {
                _logger.Error("Worksets", $"Error during workset verification: {ex.Message}", modelName, "WorksetVerification", ex);
                return false;
            }
        }
    }
}
